using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Ukeire.Mahjong;

namespace Ukeire.Game;

public enum Verdict
{
    Best,
    Good,
    Defensive,
    Slow,
    Backward,

    /// <summary>A call that made progress but left the hand exposed: an unsecured yaku, or called against riichi.</summary>
    Risky,

    /// <summary>A call that didn't bring the hand closer to tenpai.</summary>
    Pointless,

    /// <summary>A call that left an open hand with no yaku in reach.</summary>
    NoYaku,
}

public enum ReviewKind
{
    Discard,
    Call,
}

/// <param name="Tile">The discarded tile, or the tile taken by a call. For a closed or added kan, the kan's tile.</param>
/// <param name="Meld">The meld formed by a call. Empty for a discard.</param>
public sealed record ReviewEntry(
    DateTime Time,
    ReviewKind Kind,
    string Hand,
    int Tile,
    IReadOnlyList<int> Meld,
    Verdict Verdict,
    string Message,
    IReadOnlyList<DiscardOption> TopOptions,
    HintTier HintUsed);

/// <summary>
/// Watches the table, keeps an up-to-date analysis on a worker thread and reviews each discard and call after it is made.
/// It never sends input to the game.
/// </summary>
public sealed class Coach : IDisposable
{
    private readonly Configuration config;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly List<ReviewEntry> reviews = [];

    private TableSnapshot previous = TableSnapshot.Inactive;
    private long analysisKey;
    private int version;
    private HandAnalysis? analysis;
    private HandAnalysis? decision;
    private bool readFailed;

    /// <summary>Identifies the moment after an opponent's discard. Any draw (the wall shrinks) or call ends it.</summary>
    private readonly record struct CallWindow(int Discards, int Wall, int MeldTiles)
    {
        public static CallWindow Of(TableSnapshot snap) => new(snap.TotalDiscards, snap.WallTilesRemaining, snap.AllMeldTiles);
    }

    private CallWindow? callWindow;

    /// <summary>True from an opponent's discard until the next draw or call: the only time chi, pon or open kan is possible.</summary>
    public bool CallWindowOpen => callWindow != null;

    /// <summary>The table just before one of the player's melds appeared, kept until the hand catches up.</summary>
    private (TableSnapshot Before, DateTime Seen)? pendingCall;

    /// <summary>How long to wait for the hand to update after a new meld shows up before giving up on reviewing it.</summary>
    private static readonly TimeSpan CallSettleTime = TimeSpan.FromSeconds(5);

    public Coach(Configuration config, IFramework framework, IPluginLog log)
    {
        this.config = config;
        this.framework = framework;
        this.log = log;
        framework.Update += OnUpdate;
    }

    public TableSnapshot Snapshot { get; private set; } = TableSnapshot.Inactive;
    public HandAnalysis? Analysis => Volatile.Read(ref analysis);

    /// <summary>How far the player has opened the hints for the current decision.</summary>
    public HintTier HintTier { get; private set; }

    public IReadOnlyList<ReviewEntry> Reviews
    {
        get { lock (gate) return reviews.ToList(); }
    }

    public ReviewEntry? LastReview
    {
        get { lock (gate) return reviews.Count > 0 ? reviews[^1] : null; }
    }

    public void RevealHint(HintTier tier)
    {
        if (tier > HintTier && (int)tier <= config.MaxHintTier) HintTier = tier;
    }

    public void ClearReviews()
    {
        lock (gate) reviews.Clear();
    }

    private static TableContext Context(TableSnapshot snap) =>
        new(snap.RoundWind, snap.SeatWind, snap.DoraIndicators.ToList(), snap.Riichi.ToList());

    private void OnUpdate(IFramework _)
    {
        TableSnapshot snap;
        try
        {
            snap = EmjReader.Read();
            readFailed = false;
        }
        catch (Exception ex)
        {
            if (!readFailed) log.Error(ex, "Failed to read the Mahjong table");
            readFailed = true;
            snap = TableSnapshot.Inactive;
        }

        switch (snap.IsActive, previous.IsActive)
        {
            case (true, false):
            case (true, true) when snap.TotalDiscards < previous.TotalDiscards:
                OnNewHand();
                break;
            case (true, true):
                DetectOwnDiscard(previous, snap);
                break;
        }
        if (snap.IsActive && previous.IsActive && MeldTileCount(snap) > MeldTileCount(previous))
            pendingCall ??= (previous, DateTime.Now);
        if (pendingCall != null) ResolveCall(snap);

        var newOpponentDiscard = snap.IsActive && previous.IsActive && snap.TotalDiscards > previous.TotalDiscards &&
                                 snap.LastDiscardSeat >= 0 && snap.LastDiscardSeat != snap.LocalSeat;
        callWindow = (snap.IsActive, newOpponentDiscard, callWindow) switch
        {
            (false, _, _) => null,
            (_, true, _) => CallWindow.Of(snap),
            (_, _, { } open) when open != CallWindow.Of(snap) => null,
            _ => callWindow,
        };

        Snapshot = snap;
        previous = snap;
        if (!snap.IsActive)
        {
            Volatile.Write(ref analysis, null);
            return;
        }

        var ctx = Context(snap);
        var key = HashCode.Combine(snap.Fingerprint(), ctx.RoundWind, ctx.SeatWind, string.Join(",", ctx.DoraIndicators), string.Join(",", ctx.RiichiSeats));
        if (key == analysisKey) return;

        var handChanged = analysis == null || analysis.Snapshot.Fingerprint() != snap.Fingerprint();
        analysisKey = key;
        if (handChanged) HintTier = HintTier.None;

        var myVersion = Interlocked.Increment(ref version);
        Task.Run(() =>
        {
            try
            {
                var result = HandAnalysis.Build(snap, ctx);
                if (Volatile.Read(ref version) != myVersion) return;
                Volatile.Write(ref analysis, result);
                if (snap.IsDiscardTurn) Volatile.Write(ref decision, result);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Hand analysis failed");
            }
        });
    }

    private void OnNewHand()
    {
        HintTier = HintTier.None;
        Volatile.Write(ref decision, null);
    }

    private void DetectOwnDiscard(TableSnapshot before, TableSnapshot after)
    {
        if (!config.AutoReview || !before.IsDiscardTurn) return;
        var seat = after.LocalSeat;
        if (after.Discards[seat].Count != before.Discards[seat].Count + 1) return;

        var discard = after.Discards[seat][^1].Kind;
        var hint = HintTier;
        var ctx = Context(before);
        var ready = Volatile.Read(ref decision);
        Task.Run(() =>
        {
            try
            {
                var basis = ready != null && ready.Snapshot.Fingerprint() == before.Fingerprint() ? ready : HandAnalysis.Build(before, ctx);
                var entry = Review(basis, discard, hint);
                if (entry != null) AddReview(entry);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Discard review failed");
            }
        });
    }

    private static int MeldTileCount(TableSnapshot snap) => snap.MyMelds.Sum(m => m.Kinds.Count);

    /// <summary>
    /// The meld and the hand can update on different frames, so a call is only reviewed once the hand has changed too.
    /// </summary>
    private void ResolveCall(TableSnapshot snap)
    {
        var (before, seen) = pendingCall!.Value;
        if (!snap.IsActive || DateTime.Now - seen > CallSettleTime)
        {
            pendingCall = null;
            return;
        }
        if (snap.Hand.AsSpan().SequenceEqual(before.Hand)) return;

        pendingCall = null;
        if (!config.AutoReview) return;
        var ctx = Context(snap);
        Task.Run(() =>
        {
            try
            {
                var entry = ReviewCall(before, snap, ctx);
                if (entry != null) AddReview(entry);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Call review failed");
            }
        });
    }

    private void AddReview(ReviewEntry entry)
    {
        lock (gate)
        {
            reviews.Add(entry);
            if (reviews.Count > config.ReviewHistoryLimit) reviews.RemoveRange(0, reviews.Count - config.ReviewHistoryLimit);
        }
    }

    public static ReviewEntry? Review(HandAnalysis basis, int discard, HintTier hint)
    {
        var options = basis.Discards;
        if (options == null || options.Count == 0) return null;
        var best = options[0];
        var chosen = options.FirstOrDefault(o => o.Discard == discard);
        if (chosen == null) return null;

        var top = options.Take(3).ToList();
        var hand = Tiles.Notation(basis.Snapshot.Hand);
        var chosenDanger = basis.DangerOf(discard);
        var bestDanger = basis.DangerOf(best.Discard);
        var saferThanBest = chosenDanger.HasValue && bestDanger.HasValue && chosenDanger < bestDanger;

        var cName = Tiles.Short(discard);
        var bName = Tiles.Short(best.Discard);
        var lostStep = chosen.ShantenAfter.Value > best.ShantenAfter.Value;
        var acceptPercent = best.UkeireTotal == 0 ? 100 : chosen.UkeireTotal * 100 / best.UkeireTotal;

        var (verdict, message) = (lostStep, saferThanBest, acceptPercent) switch
        {
            (true, true, _) => (Verdict.Defensive,
                $"You gave up a step toward tenpai to play safe. {cName} is {Describe(chosenDanger)}, while {bName} is {Describe(bestDanger)}. Folding against riichi is often right with a slow hand."),
            (true, false, _) => (Verdict.Backward,
                $"Discarding {cName} took you from {best.ShantenAfter.Value} to {chosen.ShantenAfter.Value} shanten. {bName} would have kept your progress ({best.UkeireTotal} tiles accepted). Check whether {cName} was part of a group or a needed shape."),
            (_, _, >= 100) => (Verdict.Best,
                $"Top choice: {cName} keeps shanten {chosen.ShantenAfter.Value} with {chosen.UkeireTotal} tiles accepted ({Tiles.Join(chosen.Accepts.Select(a => a.Kind))})."),
            (_, _, >= 85) => (Verdict.Good,
                $"Close: {cName} accepts {chosen.UkeireTotal} tiles. {bName} accepts {best.UkeireTotal}. The difference: {Difference(best, chosen)}."),
            (_, true, _) => (Verdict.Defensive,
                $"Slower ({chosen.UkeireTotal} vs {best.UkeireTotal} tiles), but {cName} is {Describe(chosenDanger)} against riichi. A reasonable trade when the hand isn't worth much."),
            _ => (Verdict.Slow,
                $"Same shanten, but {cName} accepts only {chosen.UkeireTotal} tiles. {bName} accepts {best.UkeireTotal}. You lose: {Difference(best, chosen)}."),
        };

        return new ReviewEntry(DateTime.Now, ReviewKind.Discard, hand, discard, [], verdict, message, top, hint);
    }

    /// <summary>
    /// Rates a chi, pon or kan by what it did to the hand: shanten before and after, whether an open yaku is still
    /// in reach, and whether anyone was in riichi.
    /// </summary>
    public static ReviewEntry? ReviewCall(TableSnapshot before, TableSnapshot after, TableContext ctx)
    {
        OpenMeld? meld = null;
        var added = false;
        for (var i = 0; i < after.MyMelds.Count && meld == null; i++)
        {
            if (i >= before.MyMelds.Count) meld = after.MyMelds[i];
            else if (after.MyMelds[i].Kinds.Count > before.MyMelds[i].Kinds.Count) (meld, added) = (after.MyMelds[i], true);
        }
        if (meld == null) return null;

        // The called tile is the one in the meld that didn't come out of the hand. Closed and added kans have none.
        var called = meld.Kinds.Distinct()
            .Where(k => meld.Kinds.Count(x => x == k) > Math.Max(0, before.Hand[k] - after.Hand[k]))
            .DefaultIfEmpty(-1).First();
        var tile = called >= 0 ? called : meld.Kind;
        var kan = meld.Kinds.Count == 4;
        var name = (kan, meld.IsConcealedKan, added, meld.IsSequence) switch
        {
            (true, true, _, _) => "Closed kan",
            (true, _, true, _) => "Added kan",
            (true, _, _, _) => "Open kan",
            (_, _, _, true) => "Chi",
            _ => "Pon",
        };
        var label = $"{name} on {Tiles.Short(tile)}";

        var shantenBefore = Mahjong.Shanten.Calculate(before.Hand, before.OpenMeldCount).Value;
        var shantenAfter = Mahjong.Shanten.Calculate(after.Hand, after.OpenMeldCount).Value;
        var gained = shantenBefore - shantenAfter;
        var (noYaku, plan, newlySecured) = YakuAfterCall.Of(before, after.Hand, after.MyMelds, ctx);
        var opensHand = !meld.IsConcealedKan && before.MyMelds.All(m => m.IsConcealedKan);
        var threatened = Enumerable.Range(0, 4).Any(seat => seat != after.LocalSeat && ctx.IsThreat(seat));

        var (verdict, message) = (kan, meld.IsConcealedKan, noYaku, gained) switch
        {
            (_, false, true, _) => (Verdict.NoYaku,
                $"{label} left your open hand with no yaku in reach. It can't win unless you build one. Check for an open yaku before calling."),
            (_, _, _, < 0) => (Verdict.Backward,
                $"{label} broke up your shape: {ShantenText(shantenBefore)} before, {ShantenText(shantenAfter)} after. Those tiles were doing more work where they were."),
            (true, _, _, _) when threatened => (Verdict.Risky,
                $"{label} revealed a new dora indicator while someone is in riichi. It can make their hand more valuable just as much as yours."),
            (true, true, _, _) => (Verdict.Good,
                $"{label}: your hand stays closed (riichi is still on), you get a replacement draw and a new dora indicator."),
            (true, _, _, _) => (Verdict.Good,
                $"{label}: no change to your shape, plus a replacement draw and a new dora indicator.{(opensHand ? " It opened your hand, so riichi is gone." : "")}"),
            (_, _, _, 0) when newlySecured != null => (Verdict.Good,
                $"{label} didn't change your shanten, but it locked in {newlySecured.Name} as your yaku.{(opensHand ? " Staying closed for riichi was also an option, since the call didn't speed you up." : "")}"),
            (_, _, _, 0) => (Verdict.Pointless,
                shantenAfter == 0
                    ? $"{label} kept you at tenpai but didn't improve anything. You were already waiting, so the call only gave up options{(opensHand ? " like riichi" : "")}."
                    : $"{label} didn't bring you closer to tenpai (still {ShantenText(shantenAfter)}). A call is only worth opening up for when it speeds you up."),
            _ when threatened && shantenAfter >= 2 => (Verdict.Risky,
                $"{label} while someone is in riichi, and you're still {ShantenText(shantenAfter)}. Every call leaves fewer tiles in your hand to defend with."),
            _ when plan is { Outlook: YakuOutlook.Possible } => (Verdict.Risky,
                $"{label} sped you up to {ShantenText(shantenAfter)}, but your only open yaku, {plan.Name}, isn't secured yet. If it falls apart, the hand can't win."),
            _ => (Verdict.Good,
                $"{label} took you to {ShantenText(shantenAfter)} with {plan?.Name ?? "a yaku"} {(plan?.Outlook == YakuOutlook.Secured ? "locked in" : "on track")}.{(opensHand ? " You gave up riichi for speed, a fair trade with a yaku in reach." : "")}"),
        };

        return new ReviewEntry(DateTime.Now, ReviewKind.Call, Tiles.Notation(before.Hand), tile, meld.Kinds, verdict, message, [], HintTier.None);
    }

    private static string ShantenText(int shanten) => CallAdvisor.ShantenText(shanten);

    private static string Describe(SafetyLevel? level) => level switch
    {
        SafetyLevel.Safe => "100% safe (genbutsu)",
        SafetyLevel.VerySafe => "very safe",
        SafetyLevel.FairlySafe => "fairly safe",
        SafetyLevel.Risky => "risky",
        SafetyLevel.Dangerous => "dangerous",
        _ => "unrated",
    };

    private static string Difference(DiscardOption better, DiscardOption worse)
    {
        var missing = better.Accepts.Where(a => worse.Accepts.All(w => w.Kind != a.Kind)).Select(a => a.Kind).ToList();
        return missing.Count == 0 ? "fewer copies of the same tiles" : $"draws on {Tiles.Join(missing)}";
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
