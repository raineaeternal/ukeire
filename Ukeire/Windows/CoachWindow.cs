using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Ukeire.Game;
using Ukeire.Mahjong;

namespace Ukeire.Windows;

public class CoachWindow : Window, IDisposable
{
    private static readonly string[] RelativeSeat = ["You", "Right (shimocha)", "Across (toimen)", "Left (kamicha)"];

    private readonly Plugin plugin;
    private Coach Coach => plugin.Coach;
    private Configuration Config => plugin.Configuration;

    /// <summary>Lesson to open on the Learn tab next frame, set by the "Explain" buttons.</summary>
    private string? jumpToLesson;

    public CoachWindow(Plugin plugin) : base("Ukeire - Mahjong Tutor###UkeireCoach")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        TileWidgets.Design = TileIcons.Resolve(Config.TileDesign);
    }

    public override void Draw()
    {
        var snap = Coach.Snapshot;
        var analysis = Coach.Analysis;

        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success) return;

        using (var tab = ImRaii.TabItem("Table"))
            if (tab.Success) DrawTable(snap, analysis);
        using (var tab = ImRaii.TabItem("Defense"))
            if (tab.Success) DrawDefense(snap, analysis);
        using (var tab = ImRaii.TabItem("Review"))
            if (tab.Success) DrawReview();
        using (var tab = ImRaii.TabItem("History"))
            if (tab.Success) DrawHistory();
        var learnFlags = jumpToLesson != null ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using (var tab = ImRaii.TabItem("Learn", learnFlags))
            if (tab.Success) DrawLearn(snap, analysis);
        using (var tab = ImRaii.TabItem("Debug"))
            if (tab.Success) DrawDebug(snap);
    }

    private string SeatName(TableSnapshot snap, int seat) => RelativeSeat[(seat - snap.LocalSeat + 4) & 3];

    private void DrawTable(TableSnapshot snap, HandAnalysis? a)
    {
        if (!snap.IsActive)
        {
            ImGui.TextColored(TileWidgets.Muted, snap.Status);
            ImGui.TextWrapped("Sit down at a Doman Mahjong table in the Gold Saucer (or play against NPCs) and this window will follow your hand.");
            return;
        }

        ImGui.TextUnformatted("Your hand");
        TileWidgets.Hand(snap.Hand, snap.RedFives, "hand", drawn: snap.DrawnTile, drawnRed: snap.DrawnTileIsRed);
        if (snap.MyMelds.Count > 0)
        {
            ImGui.TextUnformatted("Called melds");
            for (var i = 0; i < snap.MyMelds.Count; i++)
            {
                if (i > 0) ImGui.SameLine(0, 10 * ImGuiHelpers.GlobalScale);
                using (ImRaii.Group()) TileWidgets.Row(snap.MyMelds[i].Kinds, $"meld{i}_");
            }
        }

        ImGui.Separator();
        if (!Coach.IsCurrent(a))
        {
            if (Config.ExplainCalls && Coach.CallWindowOpen) ImGui.TextColored(TileWidgets.Warn, "Checking whether you can call...");
            ImGui.TextColored(TileWidgets.Muted, "Analyzing...");
            DrawContext(snap);
            return;
        }

        if (Config.ExplainCalls) DrawCalls(a);
        if (Config.ShowShanten) DrawStatus(a);
        DrawWarnings(a);

        switch (Coach.IsRiichiLocked, snap.IsDiscardTurn)
        {
            case (true, _):
                ImGui.TextColored(TileWidgets.Muted, "You're in riichi, so your hand is locked: discard every tile you draw unless it wins. The analysis stays on the hand you declared with.");
                break;
            case (_, true):
                DrawHints(a);
                break;
            default:
                ImGui.TextColored(TileWidgets.Muted, "Waiting for your turn. Hints open up when you have a tile to discard.");
                break;
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Yaku you can aim for", ImGuiTreeNodeFlags.DefaultOpen)) DrawYaku(a);
        if (ImGui.CollapsingHeader("Round info")) DrawContext(snap);
    }

    private void DrawCalls(HandAnalysis a)
    {
        var calls = a.Calls.Where(c => !c.Option.OnDiscard || Coach.CallWindowOpen).ToList();
        if (calls.Count == 0) return;

        var snap = a.Snapshot;
        var header = calls[0].Option.OnDiscard
            ? $"Call chance: {SeatName(snap, snap.LastDiscardSeat)} discarded {Tiles.Name(snap.LastDiscardKind)}"
            : "You can declare a kan this turn";
        ImGui.TextColored(TileWidgets.Warn, header);
        TileWidgets.HelpMarker("Worked out from your hand and the visible tiles only. Passing is always an option: you don't have to call.");
        ExplainButton("calling");

        for (var i = 0; i < calls.Count; i++)
        {
            var c = calls[i];
            using var id = ImRaii.PushId($"call{i}");
            using (ImRaii.Group()) TileWidgets.Row(c.Option.Meld.Kinds, "m");
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                var color = c.Risk switch
                {
                    CallRisk.Low => TileWidgets.Good,
                    CallRisk.Medium => TileWidgets.Warn,
                    _ => TileWidgets.Bad,
                };
                ImGui.TextUnformatted(c.Option.Name);
                ImGui.SameLine();
                ImGui.TextColored(color, $"{c.Risk} risk");
                ImGui.TextWrapped(c.Summary);
            }
            using (ImRaii.PushIndent())
            {
                foreach (var pro in c.Pros)
                    using (ImRaii.PushColor(ImGuiCol.Text, TileWidgets.Good))
                        ImGui.TextWrapped($"+ {pro}");
                foreach (var con in c.Cons)
                    using (ImRaii.PushColor(ImGuiCol.Text, TileWidgets.Bad))
                        ImGui.TextWrapped($"- {con}");
            }
        }
        ImGui.Separator();
    }

    private void DrawStatus(HandAnalysis a)
    {
        var s = a.Shanten;
        var formNote = s.Form switch
        {
            HandForm.SevenPairs => " via Seven Pairs",
            HandForm.ThirteenOrphans => " via Thirteen Orphans",
            _ => string.Empty,
        };
        switch (s, a.Acceptances)
        {
            case ({ IsComplete: true }, _):
                ImGui.TextColored(TileWidgets.Good, "Your hand is complete. You can win!");
                break;
            case ({ IsTenpai: true }, { } waits):
                ImGui.TextColored(TileWidgets.Good, $"Tenpai{formNote}. Waiting on {waits.Sum(x => x.Remaining)} unseen tiles:");
                TileWidgets.Row(waits.Select(x => x.Kind), "wait");
                DrawWaitYaku(a);
                if (waits.All(x => x.Remaining == 0))
                    ImGui.TextColored(TileWidgets.Bad, "Every copy of your waits is already visible. This wait can't win.");
                break;
            default:
                ImGui.TextUnformatted($"Shanten: {s.Value}{formNote}");
                TileWidgets.HelpMarker("How many more useful tiles you need before you're one tile from winning (tenpai).");
                ExplainButton("shanten");
                if (a.Acceptances is { Count: > 0 } acc)
                    ImGui.TextColored(TileWidgets.Muted, $"{acc.Sum(x => x.Remaining)} unseen tiles would improve your hand.");
                break;
        }
    }

    private static void DrawWaitYaku(HandAnalysis a)
    {
        var closed = a.Snapshot.MyMelds.All(m => m.IsConcealedKan);
        foreach (var w in a.WaitReadings.Where(w => w.Remaining > 0))
        {
            var reading = w.Ron.HasYaku ? w.Ron : w.Tsumo;
            if (reading.HasYaku)
            {
                var names = string.Join(", ", reading.Yaku.Select(y => $"{y.Name} {y.Han}"));
                ImGui.TextColored(TileWidgets.Muted, $"  {Tiles.Short(w.Kind)}: {reading.Han} han ({names})");
            }
            else
                ImGui.TextColored(TileWidgets.Bad, $"  {Tiles.Short(w.Kind)}: no yaku, so it can't win.");
            if (closed && !w.Ron.HasYaku && w.Tsumo.HasYaku)
                ImGui.TextColored(TileWidgets.Warn, $"  {Tiles.Short(w.Kind)}: no yaku on another player's discard unless you declare riichi. Winning it yourself works.");
        }
    }

    private void DrawWarnings(HandAnalysis a)
    {
        if (a.FuritenWaits.Count > 0)
        {
            ImGui.TextColored(TileWidgets.Bad, $"Furiten: {Tiles.Join(a.FuritenWaits)} is in your own discards. You can't win on another player's discard.");
            ExplainButton("furiten");
        }
        foreach (var l in a.Liabilities)
        {
            ImGui.TextColored(TileWidgets.Bad, $"{SeatName(a.Snapshot, l.Seat)} is one set from {l.Yakuman}. If you discard {Tiles.Name(l.Kind)} and they pon it, you pay for the whole yakuman.");
            ExplainButton("doman");
        }
        if (a.OpenWithoutYaku)
        {
            ImGui.TextColored(TileWidgets.Bad, "Your hand is open but has no yaku in reach. It can't win as it stands.");
            ExplainButton("yaku");
        }
        if (a.Threats.Count > 0)
        {
            ImGui.TextColored(TileWidgets.Warn, $"{string.Join(", ", a.Threats.Select(t => SeatName(a.Snapshot, t.Seat)))} declared riichi. Check the Defense tab before pushing.");
            ExplainButton("pushfold");
        }
    }

    /// <summary>A small button after the last item that switches to the Learn tab with this lesson open.</summary>
    private void ExplainButton(string lessonId)
    {
        ImGui.SameLine();
        if (ImGui.SmallButton($"Explain##{lessonId}")) jumpToLesson = lessonId;
    }

    private void DrawHints(HandAnalysis a)
    {
        var max = (HintTier)Config.MaxHintTier;
        if (max == HintTier.None)
        {
            ImGui.TextColored(TileWidgets.Muted, "Hints are turned off in the settings. Your discards will still be reviewed.");
            return;
        }

        ImGui.TextUnformatted("Stuck on a discard?");
        TileWidgets.HelpMarker("Hints open one step at a time. Try the smallest one first. Reviews record which tier you used.");
        HintButton(HintTier.Nudge, "Nudge", max);
        ImGui.SameLine();
        HintButton(HintTier.Pointer, "Narrow it down", max);
        ImGui.SameLine();
        HintButton(HintTier.Answer, "Show the numbers", max);

        if (Coach.HintTier >= HintTier.Nudge)
            ImGui.TextWrapped($"Nudge: {Hints.Nudge(a)}");
        if (Coach.HintTier >= HintTier.Pointer)
            ImGui.TextWrapped($"Pointer: {Hints.Pointer(a)}");
        if (Coach.HintTier >= HintTier.Answer)
            DrawOptionsTable(Hints.Answer(a, 5), a, "answer");
    }

    private void HintButton(HintTier tier, string label, HintTier max)
    {
        var available = tier <= max && Coach.HintTier == tier - 1;
        using (ImRaii.Disabled(!available))
        {
            if (ImGui.Button(label)) Coach.RevealHint(tier);
        }
    }

    private void DrawOptionsTable(System.Collections.Generic.IReadOnlyList<DiscardOption> options, HandAnalysis a, string id)
    {
        var defense = a.Threats.Count > 0;
        using var table = ImRaii.Table($"##{id}", defense ? 4 : 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return;
        ImGui.TableSetupColumn("Discard");
        ImGui.TableSetupColumn("Shanten");
        ImGui.TableSetupColumn("Accepts", ImGuiTableColumnFlags.WidthStretch);
        if (defense) ImGui.TableSetupColumn("Danger");
        ImGui.TableHeadersRow();
        var i = 0;
        foreach (var o in options)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            TileWidgets.Tile(o.Discard, $"{id}{i++}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(o.ShantenAfter.IsTenpai ? "tenpai" : o.ShantenAfter.Value.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{o.UkeireTotal} tiles / {o.UkeireKinds} kinds");
            ImGui.TextColored(TileWidgets.Muted, Tiles.Join(o.Accepts.Select(x => x.Kind)));
            if (defense)
            {
                ImGui.TableNextColumn();
                var level = a.DangerOf(o.Discard);
                if (level.HasValue) ImGui.TextColored(TileWidgets.SafetyColor(level.Value), level.Value.ToString());
            }
        }
    }

    private static void DrawYaku(HandAnalysis a)
    {
        if (a.YakuPaths.Count == 0)
        {
            ImGui.TextColored(TileWidgets.Muted, "No clear yaku direction yet.");
            return;
        }
        foreach (var y in a.YakuPaths.Where(p => !p.Yakuman))
        {
            var color = y.Outlook switch
            {
                YakuOutlook.Secured => TileWidgets.Good,
                YakuOutlook.Likely => new Vector4(0.75f, 0.85f, 0.45f, 1f),
                _ => TileWidgets.Muted,
            };
            ImGui.TextColored(color, $"{y.Name} [{y.Outlook}]");
            using (ImRaii.PushIndent())
                ImGui.TextWrapped(y.Explanation);
        }

        var yakuman = a.YakuPaths.Where(p => p.Yakuman).ToList();
        if (yakuman.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextColored(TileWidgets.Yakuman, "Yakuman chance");
        TileWidgets.HelpMarker("A yakuman is a limit hand: 32,000 points (48,000 as dealer). They're rare and hard to finish, so treat this as a possibility, not a plan.");
        foreach (var y in yakuman)
        {
            ImGui.TextColored(TileWidgets.Yakuman, $"{y.Name} [{y.Outlook}]");
            using (ImRaii.PushIndent())
                ImGui.TextWrapped(y.Explanation);
        }
    }

    private void DrawContext(TableSnapshot snap)
    {
        ImGui.TextUnformatted($"{Tiles.Name(snap.RoundWind)} round, hand {snap.Dealer + 1}  ·  You are {Tiles.Name(snap.SeatWind)}");
        ImGui.TextUnformatted($"Honba: {snap.Honba}  Riichi sticks: {snap.RiichiSticks}  Tiles left in the wall: {snap.WallTilesRemaining}");
        ImGui.TextUnformatted($"You're in {Ordinal(snap.Placement)} place.");
        TileWidgets.HelpMarker("Doman Mahjong has no uma or oka: your rating only depends on where you finish, not how many points you finish with.");

        ImGui.TextUnformatted("Dora indicators:");
        for (var i = 0; i < snap.DoraIndicators.Count; i++)
        {
            ImGui.SameLine();
            TileWidgets.Tile(snap.DoraIndicators[i], $"dora{i}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Dora is {Tiles.Name(Tiles.DoraFromIndicator(snap.DoraIndicators[i]))}.");
        }
        if (snap.DoraIndicators.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(TileWidgets.Muted, "(none)");
        }

        for (var seat = 0; seat < 4; seat++)
        {
            if (seat > 0) ImGui.SameLine(0, 16 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted($"{SeatName(snap, seat)} ({Tiles.Name(Tiles.East + seat)}): {snap.Points[seat]}{(snap.Riichi[seat] ? " riichi" : string.Empty)}");
        }
    }

    private void DrawDefense(TableSnapshot snap, HandAnalysis? a)
    {
        if (!snap.IsActive)
        {
            ImGui.TextColored(TileWidgets.Muted, snap.Status);
            return;
        }

        ImGui.TextUnformatted("In riichi:");
        var anyRiichi = false;
        for (var seat = 0; seat < 4; seat++)
        {
            if (seat == snap.LocalSeat || !snap.Riichi[seat]) continue;
            ImGui.SameLine();
            ImGui.TextColored(TileWidgets.Bad, SeatName(snap, seat));
            anyRiichi = true;
        }
        if (!anyRiichi)
        {
            ImGui.SameLine();
            ImGui.TextColored(TileWidgets.Muted, "nobody");
        }
        ImGui.Separator();

        if (a != null && a.Liabilities.Count > 0)
        {
            foreach (var l in a.Liabilities)
                ImGui.TextColored(TileWidgets.Bad, $"Liability: {SeatName(snap, l.Seat)} needs one more set for {l.Yakuman}. Hold on to your {Tiles.Name(l.Kind)}: if they pon it from you, you pay the full yakuman.");
            ImGui.Separator();
        }

        if (a == null || a.Threats.Count == 0)
        {
            ImGui.TextWrapped("No threats yet. When someone declares riichi, each tile in your hand is rated here using only public information: their discards and every visible tile.");
            return;
        }

        if (!Config.ShowDefense)
        {
            ImGui.TextColored(TileWidgets.Muted, "Defense ratings are hidden in the settings. Try reading suji yourself!");
            return;
        }

        foreach (var threat in a.Threats)
        {
            ImGui.TextColored(TileWidgets.Warn, $"Against {SeatName(snap, threat.Seat)}:");
            ImGui.TextColored(TileWidgets.Muted, "Their discards:");
            TileWidgets.Row(snap.Discards[threat.Seat].Select(d => d.Kind), $"pile{threat.Seat}_");
            using var table = ImRaii.Table($"##safety{threat.Seat}", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
            if (!table.Success) continue;
            ImGui.TableSetupColumn("Tile");
            ImGui.TableSetupColumn("Rating");
            ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch);
            var i = 0;
            foreach (var r in threat.Tiles)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                TileWidgets.Tile(r.Kind, $"safe{threat.Seat}_{i++}");
                ImGui.TableNextColumn();
                ImGui.TextColored(TileWidgets.SafetyColor(r.Level), r.Level.ToString());
                ImGui.TableNextColumn();
                ImGui.TextWrapped(r.Reason);
            }
        }
    }

    private void DrawReview()
    {
        var reviews = Coach.Reviews;
        if (reviews.Count == 0)
        {
            ImGui.TextWrapped(Config.AutoReview
                ? "Each discard and call you make will be reviewed here: what it did to your hand, and what the better choice would have been."
                : "Reviews are turned off in the settings.");
            return;
        }

        static bool Sound(ReviewEntry r) => r.Verdict is Verdict.Best or Verdict.Good or Verdict.Defensive;
        var discards = reviews.Where(r => r.Kind == ReviewKind.Discard).ToList();
        var calls = reviews.Where(r => r.Kind == ReviewKind.Call).ToList();
        if (discards.Count > 0)
        {
            var sound = discards.Count(Sound);
            ImGui.TextUnformatted($"Sound discards: {sound}/{discards.Count} ({100 * sound / discards.Count}%)");
            var unaided = discards.Where(r => r.HintUsed == HintTier.None).ToList();
            if (unaided.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(TileWidgets.Muted, $"| without hints: {unaided.Count(Sound)}/{unaided.Count}");
            }
            ImGui.SameLine();
        }
        if (calls.Count > 0)
        {
            if (discards.Count > 0) ImGui.TextColored(TileWidgets.Muted, "|");
            ImGui.SameLine();
            ImGui.TextUnformatted($"Sound calls: {calls.Count(Sound)}/{calls.Count}");
            ImGui.SameLine();
        }
        if (ImGui.SmallButton("Clear")) Coach.ClearReviews();
        ImGui.Separator();

        using var child = ImRaii.Child("##reviews", Vector2.Zero);
        if (!child.Success) return;
        for (var i = reviews.Count - 1; i >= 0; i--)
        {
            var r = reviews[i];
            using var id = ImRaii.PushId(i);
            var color = r.Verdict switch
            {
                Verdict.Best or Verdict.Good => TileWidgets.Good,
                Verdict.Defensive => new Vector4(0.55f, 0.75f, 0.95f, 1f),
                Verdict.Slow or Verdict.Risky or Verdict.Pointless => TileWidgets.Warn,
                _ => TileWidgets.Bad,
            };
            using (ImRaii.Group())
            {
                if (r.Kind == ReviewKind.Call) TileWidgets.Row(r.Meld, "m");
                else TileWidgets.Tile(r.Tile, "d");
            }
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                ImGui.TextColored(color, r.Kind == ReviewKind.Call ? $"Call: {VerdictName(r.Verdict)}" : VerdictName(r.Verdict));
                ImGui.SameLine();
                ImGui.TextColored(TileWidgets.Muted, $"{r.Time:HH:mm:ss}  {r.Hand}{(r.HintUsed > HintTier.None ? $"  (hint: {r.HintUsed})" : string.Empty)}");
                ImGui.TextWrapped(r.Message);
            }
            ImGui.Separator();
        }
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        4 => "4th",
        _ => "-",
    };

    private static string Percent(IEnumerable<ReviewRecord> reviews)
    {
        var list = reviews.ToList();
        return list.Count == 0 ? "-" : $"{100 * list.Count(r => r.IsSound) / list.Count}% of {list.Count}";
    }

    private void DrawHistory()
    {
        var history = Coach.History;
        var games = history.Games;
        if (games.Count == 0)
        {
            ImGui.TextWrapped("Every game you play is saved here, with each hand's points and the reviews of your discards and calls. It's kept across plugin updates.");
            return;
        }

        var finished = games.Where(g => g.Placement > 0).ToList();
        var reviews = games.SelectMany(g => g.Reviews).ToList();
        ImGui.TextUnformatted($"Games: {games.Count}");
        if (finished.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"| Average placement: {finished.Average(g => g.Placement):0.00}  | 1st/2nd/3rd/4th: {string.Join("/", Enumerable.Range(1, 4).Select(p => finished.Count(g => g.Placement == p)))}");
            TileWidgets.HelpMarker("Doman Mahjong's rating only counts placement, so this matters more than your points.");
        }
        ImGui.TextUnformatted($"Sound discards: {Percent(reviews.Where(r => !r.IsCall))}  | Sound calls: {Percent(reviews.Where(r => r.IsCall))}");
        var hands = games.SelectMany(g => g.Hands).ToList();
        var yakumanWins = hands.Count(h => h.YakumanWin != null);
        var yakumanChances = hands.Count(h => h.YakumanChances.Count > 0);
        if (yakumanWins + yakumanChances > 0)
            ImGui.TextColored(TileWidgets.Yakuman, $"Yakuman wins: {yakumanWins}  | Hands with a yakuman chance: {yakumanChances}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy folder path")) ImGui.SetClipboardText(history.FolderPath);
        TileWidgets.HelpMarker($"Saved as one JSON file per game in {history.FolderPath}");
        ImGui.Separator();

        using var child = ImRaii.Child("##history", Vector2.Zero);
        if (!child.Success) return;
        var current = history.Current;
        foreach (var game in games)
        {
            using var id = ImRaii.PushId(game.Id);
            var outcome = (game == current, game.Ended.HasValue) switch
            {
                (true, _) => "in progress",
                (_, false) => "unfinished",
                _ => $"{Ordinal(game.Placement)}, {game.FinalPoints[0]:N0}",
            };
            var gameReviews = game.Reviews.ToList();
            var header = $"{game.Started:yyyy-MM-dd HH:mm}  ·  {outcome}  ·  {game.Hands.Count} hands  ·  discards {Percent(gameReviews.Where(r => !r.IsCall))}";
            if (!ImGui.CollapsingHeader($"{header}###game")) continue;

            using var indent = ImRaii.PushIndent();
            if (game.Ended.HasValue)
                ImGui.TextColored(TileWidgets.Muted, $"Final: you {game.FinalPoints[0]:N0}, right {game.FinalPoints[1]:N0}, across {game.FinalPoints[2]:N0}, left {game.FinalPoints[3]:N0}");
            if (game != current)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Delete") && ImGui.GetIO().KeyCtrl) history.Delete(game);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click to delete this game.");
            }

            for (var h = 0; h < game.Hands.Count; h++)
            {
                var hand = game.Hands[h];
                var change = hand.PointsChange;
                var tags = string.Concat(hand.WasDealer ? " · dealer" : "", hand.DeclaredRiichi ? " · riichi" : "", hand.MeldsCalled > 0 ? $" · {hand.MeldsCalled} called" : "",
                                         hand.YakumanWin != null ? " · YAKUMAN" : hand.YakumanChances.Count > 0 ? " · yakuman chance" : "");
                var color = (hand.YakumanWin != null, change) switch
                {
                    (true, _) => TileWidgets.Yakuman,
                    (_, > 0) => TileWidgets.Good,
                    (_, < 0) => TileWidgets.Bad,
                    _ => TileWidgets.Muted,
                };
                using (ImRaii.PushColor(ImGuiCol.Text, color))
                {
                    if (!ImGui.TreeNode($"{hand.Label}  {change:+#,0;-#,0;0}{tags}  ·  discards {Percent(hand.Reviews.Where(r => !r.IsCall))}###hand{h}"))
                        continue;
                }
                foreach (var r in hand.Reviews)
                {
                    var verdict = Enum.TryParse<Verdict>(r.Verdict, out var v) ? v : (Verdict?)null;
                    var vColor = verdict switch
                    {
                        Verdict.Best or Verdict.Good => TileWidgets.Good,
                        Verdict.Defensive => new Vector4(0.55f, 0.75f, 0.95f, 1f),
                        Verdict.Slow or Verdict.Risky or Verdict.Pointless => TileWidgets.Warn,
                        null => TileWidgets.Muted,
                        _ => TileWidgets.Bad,
                    };
                    var label = verdict is { } known ? VerdictName(known) : r.Verdict;
                    ImGui.TextColored(vColor, r.IsCall ? $"Call: {label}" : label);
                    ImGui.SameLine();
                    ImGui.TextColored(TileWidgets.Muted, $"{(r.IsCall ? Tiles.Join(r.Meld) : Tiles.IsValid(r.Tile) ? Tiles.Short(r.Tile) : "?")}  {r.Hand}{(r.IsUnaided ? "" : $"  (hint: {r.HintUsed})")}");
                    using (ImRaii.PushIndent())
                        ImGui.TextWrapped(r.Message);
                }
                if (hand.YakumanWin != null)
                    ImGui.TextColored(TileWidgets.Yakuman, $"Yakuman: {hand.YakumanWin}");
                if (hand.YakumanChances.Count > 0)
                    ImGui.TextColored(TileWidgets.Yakuman, $"Yakuman chances: {string.Join(", ", hand.YakumanChances.Select(s => $"{s.Name} ({s.BestOutlook})"))}");
                if (hand.Reviews.Count == 0) ImGui.TextColored(TileWidgets.Muted, "No reviews this hand.");
                ImGui.TreePop();
            }
        }
    }

    private static string VerdictName(Verdict v) => v switch
    {
        Verdict.NoYaku => "No yaku",
        _ => v.ToString(),
    };

    private void DrawLearn(TableSnapshot snap, HandAnalysis? a)
    {
        var jump = jumpToLesson;
        jumpToLesson = null;
        var spotlights = Lessons.Relevant(snap, Coach.IsCurrent(a) ? a : null, Coach.LastReview);

        switch (spotlights.Count, snap.IsActive)
        {
            case (> 0, _):
                ImGui.TextColored(TileWidgets.Good, "Right now at your table");
                TileWidgets.HelpMarker("Lessons picked from your current hand, the round and your last discard. They never point at a specific discard; use the hints for that.");
                foreach (var s in spotlights)
                    LessonHeader(s.Lesson, s.Why, jump, "now");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextUnformatted("All lessons");
                break;
            case (_, true):
                ImGui.TextColored(TileWidgets.Muted, "Nothing in particular stands out in this hand. Browse the basics below.");
                break;
            default:
                ImGui.TextColored(TileWidgets.Muted, "At the table, lessons that apply to your hand will show up here.");
                break;
        }

        foreach (var lesson in Lessons.All)
        {
            // When a lesson is spotlighted, the jump opens that copy instead of the one in the full list.
            var jumpHere = spotlights.Any(s => s.Lesson.Id == jump) ? null : jump;
            LessonHeader(lesson, null, jumpHere, "all");
        }
    }

    private static void LessonHeader(Lesson lesson, string? why, string? jump, string scope)
    {
        var target = jump == lesson.Id;
        if (target) ImGui.SetNextItemOpen(true);
        var open = ImGui.CollapsingHeader($"{lesson.Title}##{scope}_{lesson.Id}");
        if (open && target) ImGui.SetScrollHereY(0f);
        using (ImRaii.PushIndent())
        {
            if (why != null)
                using (ImRaii.PushColor(ImGuiCol.Text, TileWidgets.Warn))
                    ImGui.TextWrapped(why);
            if (open) ImGui.TextWrapped(lesson.Body);
        }
    }

    private void DrawDebug(TableSnapshot snap)
    {
        ImGui.TextColored(TileWidgets.Muted, "Raw data read from the table. Useful for mapping unknown flags.");
        ImGui.TextUnformatted($"Status: {snap.Status}  Active: {snap.IsActive}  Local seat: {snap.LocalSeat}");
        var emj = TileIcons.ReadGameSetting();
        ImGui.TextUnformatted($"EmjSetting: {(emj is { } e ? $"{e.Value} (0b{Convert.ToString(e.Value, 2)}) in {e.Section}" : "unreadable")}  Tile design drawn: {TileWidgets.Design}");
        TileWidgets.HelpMarker("Switch the tile design in the game's Doman Mahjong settings and watch which bit changes. That bit is what \"Match the game\" should read.");
        if (!snap.IsActive) return;
        ImGui.TextUnformatted($"Hand tiles: {snap.HandTileCount}  Melds: {snap.OpenMeldCount}  Discard turn: {snap.IsDiscardTurn}  Game says can riichi: {snap.GameSaysCanRiichi}");
        ImGui.TextUnformatted($"Hand: {Tiles.Notation(snap.Hand)}  Drawn: {(snap.DrawnTile >= 0 ? Tiles.Short(snap.DrawnTile) : "-")}");
        ImGui.TextUnformatted($"Player.HandTileCount: {snap.RawHandTileCount}  HandTiles: {string.Join(" ", snap.RawHandTiles.Select(t => t.ToString("X")))}");
        ImGui.TextUnformatted($"Table.DrawnTile  Seat: {snap.RawDrawnTile.Seat}  Tile: {snap.RawDrawnTile.Tile:X}  Flags: {snap.RawDrawnTile.Flags:X}");
        ImGui.TextUnformatted($"Table.LastDiscard  Seat: {snap.RawLastDiscard.Seat}  Tile: {snap.RawLastDiscard.Tile:X}  Flags: {snap.RawLastDiscard.Flags:X}  Call window open: {Coach.CallWindowOpen}  Wall: {snap.WallTilesRemaining}");
        for (var seat = 0; seat < 4; seat++)
        {
            if (!ImGui.TreeNode($"Seat {seat} ({SeatName(snap, seat)})##dbg{seat}")) continue;
            ImGui.TextUnformatted($"Melds: {string.Join(" | ", snap.Melds[seat].Select(m => Tiles.Join(m.Kinds) + (m.IsConcealedKan ? " (closed kan)" : "")))}");
            ImGui.TextUnformatted("Discards (kind : flags):");
            foreach (var d in snap.Discards[seat])
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"{Tiles.Short(d.Kind)}:{d.Flags:X}");
            }
            ImGui.TreePop();
        }

        var history = EmjReader.DrawHistory;
        if (ImGui.TreeNode($"Draw field changes ({history.Length})##dbgdraw"))
        {
            if (ImGui.Button("Copy")) ImGui.SetClipboardText(string.Join("\n", history));
            foreach (var line in history) ImGui.TextUnformatted(line);
            ImGui.TreePop();
        }
    }
}
