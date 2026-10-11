using System.Collections.Generic;
using System.Linq;
using Ukeire.Mahjong;

namespace Ukeire.Game;

public enum HintTier
{
    None = 0,
    Nudge = 1,
    Pointer = 2,
    Answer = 3,
}

/// <summary>
/// Tiered hints for a discard decision. Each tier gives away a bit more, so the player does the thinking first.
/// </summary>
public static class Hints
{
    public static string Nudge(HandAnalysis a)
    {
        var best = Best(a);
        if (best == null) return "Wait for your draw. Hints are for discard decisions.";

        if (a.Threats.Count > 0 && a.DangerOf(best.Discard) >= SafetyLevel.Risky)
            return "Someone is in riichi. Before you count tiles, ask whether this hand is worth pushing. If it's slow or cheap, look for your safest tile instead (Defense tab).";

        if (best.ShantenAfter.IsTenpai && a.Shanten.Value == 0)
            return "You're one discard away from tenpai. Find the tile that isn't part of any finished group or your waiting shape.";

        var shape = Describe(a.Snapshot.Hand, best.Discard);
        return shape switch
        {
            TileShape.IsolatedHonor => "Is there a tile that doesn't connect to anything and gives you no yaku? Those are usually the first to go.",
            TileShape.IsolatedTerminal => "Look for a lone 1 or 9. Edge tiles connect to fewer neighbours than middle tiles.",
            TileShape.Isolated => "One of your tiles isn't next to (or one gap from) anything in its suit. Floating tiles are the cheapest to give up.",
            TileShape.ExtraBlock => "Count your blocks: a winning hand needs 4 groups and a pair. If you have more partial shapes than that, drop the weakest one.",
            _ => "Think about which tiles are already in finished groups, then which shapes accept the most draws.",
        };
    }

    public static string Pointer(HandAnalysis a)
    {
        var best = Best(a);
        if (best == null) return "No decision to make right now.";
        var top = TopGroup(a);
        var where = top.Select(o => Tiles.IsHonor(o.Discard) ? "your honor tiles" : $"your {Tiles.SuitName(Tiles.Suit(o.Discard))}")
                       .Distinct().ToList();
        var steps = best.ShantenAfter.Value switch
        {
            0 => "The right discard leaves you in tenpai.",
            _ => $"The right discard leaves you {best.ShantenAfter.Value} away from tenpai (shanten {best.ShantenAfter.Value}).",
        };
        return $"Look among {string.Join(" or ", where)}. {steps} The best options accept {best.UkeireTotal} unseen tiles.";
    }

    public static List<DiscardOption> Answer(HandAnalysis a, int count = 3) =>
        a.Discards?.Take(count).ToList() ?? [];

    public static DiscardOption? Best(HandAnalysis a) => a.Discards?.FirstOrDefault();

    /// <summary>All discards tied with the best one on shanten and acceptance.</summary>
    public static List<DiscardOption> TopGroup(HandAnalysis a)
    {
        var best = Best(a);
        if (best == null) return [];
        return a.Discards!.TakeWhile(o => o.ShantenAfter.Value == best.ShantenAfter.Value && o.UkeireTotal == best.UkeireTotal).ToList();
    }

    public enum TileShape
    {
        InGroup,
        IsolatedHonor,
        IsolatedTerminal,
        Isolated,
        ExtraBlock,
    }

    public static TileShape Describe(int[] hand, int kind)
    {
        if (Tiles.IsHonor(kind))
            return hand[kind] == 1 ? TileShape.IsolatedHonor : TileShape.ExtraBlock;
        if (hand[kind] > 1) return TileShape.ExtraBlock;
        var rank = Tiles.Rank(kind);
        var suitBase = Tiles.Suit(kind) * 9;
        for (var d = -2; d <= 2; d++)
        {
            if (d == 0) continue;
            var r = rank + d;
            if (r is >= 1 and <= 9 && hand[suitBase + r - 1] > 0) return TileShape.ExtraBlock;
        }
        return Tiles.IsTerminal(kind) ? TileShape.IsolatedTerminal : TileShape.Isolated;
    }
}
