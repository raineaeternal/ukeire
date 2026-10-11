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
}

public sealed record ReviewEntry(
    DateTime Time,
    string Hand,
    int Discard,
    Verdict Verdict,
    string Message,
    IReadOnlyList<DiscardOption> TopOptions,
    HintTier HintUsed);

/// <summary>
/// Watches the table, keeps an up-to-date analysis on a worker thread and reviews each discard after it is made.
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

        if (snap.IsActive && previous.IsActive)
        {
            if (snap.TotalDiscards < previous.TotalDiscards) OnNewHand();
            else DetectOwnDiscard(previous, snap);
        }
        else if (snap.IsActive && !previous.IsActive)
        {
            OnNewHand();
        }

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
                if (entry == null) return;
                lock (gate)
                {
                    reviews.Add(entry);
                    if (reviews.Count > config.ReviewHistoryLimit) reviews.RemoveRange(0, reviews.Count - config.ReviewHistoryLimit);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Discard review failed");
            }
        });
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

        Verdict verdict;
        string message;
        var cName = Tiles.Short(discard);
        var bName = Tiles.Short(best.Discard);

        if (chosen.ShantenAfter.Value > best.ShantenAfter.Value)
        {
            if (saferThanBest)
            {
                verdict = Verdict.Defensive;
                message = $"You gave up a step toward tenpai to play safe. {cName} is {Describe(chosenDanger)}, while {bName} is {Describe(bestDanger)}. Folding against riichi is often right with a slow hand.";
            }
            else
            {
                verdict = Verdict.Backward;
                message = $"Discarding {cName} took you from {best.ShantenAfter.Value} to {chosen.ShantenAfter.Value} shanten. {bName} would have kept your progress ({best.UkeireTotal} tiles accepted). Check whether {cName} was part of a group or a needed shape.";
            }
        }
        else if (chosen.UkeireTotal >= best.UkeireTotal)
        {
            verdict = Verdict.Best;
            message = $"Top choice: {cName} keeps shanten {chosen.ShantenAfter.Value} with {chosen.UkeireTotal} tiles accepted ({Tiles.Join(chosen.Accepts.Select(a => a.Kind))}).";
        }
        else if (chosen.UkeireTotal * 100 >= best.UkeireTotal * 85)
        {
            verdict = Verdict.Good;
            message = $"Close: {cName} accepts {chosen.UkeireTotal} tiles. {bName} accepts {best.UkeireTotal}. The difference: {Difference(best, chosen)}.";
        }
        else if (saferThanBest)
        {
            verdict = Verdict.Defensive;
            message = $"Slower ({chosen.UkeireTotal} vs {best.UkeireTotal} tiles), but {cName} is {Describe(chosenDanger)} against riichi. A reasonable trade when the hand isn't worth much.";
        }
        else
        {
            verdict = Verdict.Slow;
            message = $"Same shanten, but {cName} accepts only {chosen.UkeireTotal} tiles. {bName} accepts {best.UkeireTotal}. You lose: {Difference(best, chosen)}.";
        }

        return new ReviewEntry(DateTime.Now, hand, discard, verdict, message, top, hint);
    }

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
