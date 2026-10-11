using System;
using System.Collections.Generic;
using System.Linq;

namespace Ukeire.Mahjong;

/// <summary>A tile that would move the hand closer to tenpai, and how many copies are still unseen.</summary>
public readonly record struct Acceptance(int Kind, int Remaining);

public sealed record DiscardOption(
    int Discard,
    ShantenResult ShantenAfter,
    IReadOnlyList<Acceptance> Accepts,
    int GoodShapeScore)
{
    public int UkeireTotal => Accepts.Sum(a => a.Remaining);
    public int UkeireKinds => Accepts.Count;
}

public sealed record WaitInfo(IReadOnlyList<Acceptance> Waits)
{
    public int Total => Waits.Sum(w => w.Remaining);
}

/// <summary>
/// Tile efficiency: which discard keeps the lowest shanten and accepts the most tiles.
/// "Remaining" counts subtract the player's own hand and every tile visible on the table.
/// </summary>
public static class Efficiency
{
    public static IReadOnlyList<Acceptance> Acceptances(ReadOnlySpan<int> hand, int openMelds, ReadOnlySpan<int> visible, out ShantenResult current)
    {
        Span<int> work = stackalloc int[Tiles.KindCount];
        hand.CopyTo(work);
        current = Shanten.Calculate(work, openMelds);
        var result = new List<Acceptance>();
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            if (work[k] >= 4) continue;
            work[k]++;
            var after = Shanten.Calculate(work, openMelds);
            work[k]--;
            if (after.Value >= current.Value) continue;
            var remaining = Math.Max(0, 4 - hand[k] - visible[k]);
            result.Add(new Acceptance(k, remaining));
        }
        return result;
    }

    /// <summary>Evaluates every distinct discard from a hand that has just drawn (3n+2 tiles).</summary>
    public static List<DiscardOption> RankDiscards(ReadOnlySpan<int> hand, int openMelds, ReadOnlySpan<int> visible)
    {
        Span<int> work = stackalloc int[Tiles.KindCount];
        hand.CopyTo(work);
        var options = new List<DiscardOption>();
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            if (work[k] == 0) continue;
            work[k]--;
            var accepts = Acceptances(work, openMelds, visible, out var shanten);
            options.Add(new DiscardOption(k, shanten, accepts, GoodShape(work, openMelds, shanten, accepts, visible)));
            work[k]++;
        }
        options.Sort(Compare);
        return options;
    }

    public static int Compare(DiscardOption a, DiscardOption b)
    {
        var c = a.ShantenAfter.Value.CompareTo(b.ShantenAfter.Value);
        if (c != 0) return c;
        c = b.UkeireTotal.CompareTo(a.UkeireTotal);
        if (c != 0) return c;
        c = b.GoodShapeScore.CompareTo(a.GoodShapeScore);
        if (c != 0) return c;
        // Prefer letting go of honors and terminals first when everything else ties.
        return Keepability(a.Discard).CompareTo(Keepability(b.Discard));
    }

    /// <summary>Waits of a 3n+1 tenpai hand.</summary>
    public static WaitInfo? Waits(ReadOnlySpan<int> hand, int openMelds, ReadOnlySpan<int> visible)
    {
        var accepts = Acceptances(hand, openMelds, visible, out var current);
        return current.IsTenpai ? new WaitInfo(accepts) : null;
    }

    /// <summary>
    /// Second-order measure for 1-shanten hands: after drawing each accepted tile and making the best discard,
    /// how many tiles does the resulting tenpai wait on? Rewards hands that become good waits.
    /// </summary>
    private static int GoodShape(Span<int> hand, int openMelds, ShantenResult shanten, IReadOnlyList<Acceptance> accepts, ReadOnlySpan<int> visible)
    {
        if (shanten.Value != 1) return 0;
        var score = 0;
        foreach (var a in accepts)
        {
            if (a.Remaining == 0) continue;
            hand[a.Kind]++;
            var bestWait = 0;
            for (var d = 0; d < Tiles.KindCount; d++)
            {
                if (hand[d] == 0) continue;
                hand[d]--;
                var waits = Acceptances(hand, openMelds, visible, out var s);
                if (s.IsTenpai) bestWait = Math.Max(bestWait, waits.Sum(w => w.Remaining));
                hand[d]++;
            }
            hand[a.Kind]--;
            score += a.Remaining * bestWait;
        }
        return score;
    }

    private static int Keepability(int kind)
    {
        if (Tiles.IsHonor(kind)) return 0;
        var rank = Tiles.Rank(kind);
        return rank is 1 or 9 ? 1 : rank is 2 or 8 ? 2 : 3;
    }
}
