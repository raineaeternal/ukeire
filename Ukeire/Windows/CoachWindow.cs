using System;
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
        using (var tab = ImRaii.TabItem("Learn"))
            if (tab.Success) DrawLearn();
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
        if (a == null || a.Snapshot.Fingerprint() != snap.Fingerprint())
        {
            ImGui.TextColored(TileWidgets.Muted, "Analyzing...");
            DrawContext(snap);
            return;
        }

        if (Config.ShowShanten) DrawStatus(a);
        DrawWarnings(a);

        if (snap.IsDiscardTurn) DrawHints(a);
        else ImGui.TextColored(TileWidgets.Muted, "Waiting for your turn. Hints open up when you have a tile to discard.");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Yaku you can aim for", ImGuiTreeNodeFlags.DefaultOpen)) DrawYaku(a);
        if (ImGui.CollapsingHeader("Round info")) DrawContext(snap);
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
        if (s.IsComplete)
            ImGui.TextColored(TileWidgets.Good, "Your hand is complete. You can win!");
        else if (s.IsTenpai && a.Acceptances != null)
        {
            ImGui.TextColored(TileWidgets.Good, $"Tenpai{formNote}. Waiting on {a.Acceptances.Sum(x => x.Remaining)} unseen tiles:");
            TileWidgets.Row(a.Acceptances.Select(x => x.Kind), "wait");
            if (a.Acceptances.All(x => x.Remaining == 0))
                ImGui.TextColored(TileWidgets.Bad, "Every copy of your waits is already visible. This wait can't win.");
        }
        else
        {
            ImGui.TextUnformatted($"Shanten: {s.Value}{formNote}");
            TileWidgets.HelpMarker("How many more useful tiles you need before you're one tile from winning (tenpai). See the Learn tab.");
            if (a.Acceptances is { Count: > 0 } acc)
            {
                ImGui.TextColored(TileWidgets.Muted, $"{acc.Sum(x => x.Remaining)} unseen tiles would improve your hand.");
            }
        }
    }

    private void DrawWarnings(HandAnalysis a)
    {
        if (a.OpenWithoutYaku)
            ImGui.TextColored(TileWidgets.Bad, "Your hand is open but has no yaku in reach. It can't win as it stands. See the Learn tab: \"You need a yaku\".");
        if (a.Threats.Count > 0)
            ImGui.TextColored(TileWidgets.Warn, $"{string.Join(", ", a.Threats.Select(t => SeatName(a.Snapshot, t.Seat)))} declared riichi. Check the Defense tab before pushing.");
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
        foreach (var y in a.YakuPaths)
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
    }

    private void DrawContext(TableSnapshot snap)
    {
        ImGui.TextUnformatted($"{Tiles.Name(snap.RoundWind)} round, hand {snap.Dealer + 1}  ·  You are {Tiles.Name(snap.SeatWind)}");
        ImGui.TextUnformatted($"Honba: {snap.Honba}  Riichi sticks: {snap.RiichiSticks}  Tiles left in the wall: {snap.WallTilesRemaining}");

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
                ? "Each discard you make will be reviewed here: what it did to your hand, and what the most efficient choice would have been."
                : "Discard reviews are turned off in the settings.");
            return;
        }

        var sound = reviews.Count(r => r.Verdict is Verdict.Best or Verdict.Good or Verdict.Defensive);
        var unaided = reviews.Where(r => r.HintUsed == HintTier.None).ToList();
        ImGui.TextUnformatted($"Sound discards: {sound}/{reviews.Count} ({100 * sound / reviews.Count}%)");
        if (unaided.Count > 0)
        {
            var unaidedSound = unaided.Count(r => r.Verdict is Verdict.Best or Verdict.Good or Verdict.Defensive);
            ImGui.SameLine();
            ImGui.TextColored(TileWidgets.Muted, $"| without hints: {unaidedSound}/{unaided.Count}");
        }
        ImGui.SameLine();
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
                Verdict.Slow => TileWidgets.Warn,
                _ => TileWidgets.Bad,
            };
            TileWidgets.Tile(r.Discard, "d");
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                ImGui.TextColored(color, r.Verdict.ToString());
                ImGui.SameLine();
                ImGui.TextColored(TileWidgets.Muted, $"{r.Time:HH:mm:ss}  {r.Hand}{(r.HintUsed > HintTier.None ? $"  (hint: {r.HintUsed})" : string.Empty)}");
                ImGui.TextWrapped(r.Message);
            }
            ImGui.Separator();
        }
    }

    private static void DrawLearn()
    {
        foreach (var (title, body) in Lessons.All)
        {
            if (!ImGui.CollapsingHeader(title)) continue;
            using (ImRaii.PushIndent())
                ImGui.TextWrapped(body);
        }
    }

    private void DrawDebug(TableSnapshot snap)
    {
        ImGui.TextColored(TileWidgets.Muted, "Raw data read from the table. Useful for mapping unknown flags.");
        ImGui.TextUnformatted($"Status: {snap.Status}  Active: {snap.IsActive}  Local seat: {snap.LocalSeat}");
        if (!snap.IsActive) return;
        ImGui.TextUnformatted($"Hand tiles: {snap.HandTileCount}  Melds: {snap.OpenMeldCount}  Discard turn: {snap.IsDiscardTurn}  Game says can riichi: {snap.GameSaysCanRiichi}");
        ImGui.TextUnformatted($"Hand: {Tiles.Notation(snap.Hand)}  Drawn: {(snap.DrawnTile >= 0 ? Tiles.Short(snap.DrawnTile) : "-")}");
        ImGui.TextUnformatted($"Player.HandTileCount: {snap.RawHandTileCount}  HandTiles: {string.Join(" ", snap.RawHandTiles.Select(t => t.ToString("X")))}");
        ImGui.TextUnformatted($"Table.DrawnTile  Seat: {snap.RawDrawnTile.Seat}  Tile: {snap.RawDrawnTile.Tile:X}  Flags: {snap.RawDrawnTile.Flags:X}");
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
