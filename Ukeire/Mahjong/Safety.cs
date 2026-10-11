using System;
using System.Collections.Generic;
using System.Linq;

namespace Ukeire.Mahjong;

public enum SafetyLevel
{
    Safe = 0,
    VerySafe = 1,
    FairlySafe = 2,
    Risky = 3,
    Dangerous = 4,
}

public sealed record SafetyReading(int Kind, SafetyLevel Level, string Reason);

/// <summary>
/// Defense against one threatening opponent (usually one in riichi), using only public information:
/// that opponent's discards, plus every tile visible on the table and in the player's own hand.
/// </summary>
public static class Safety
{
    public static SafetyReading Read(int kind, ReadOnlySpan<int> opponentDiscards, ReadOnlySpan<int> seen)
    {
        if (opponentDiscards[kind] > 0)
            return new SafetyReading(kind, SafetyLevel.Safe, "Genbutsu: they discarded this tile themselves, so they can't win on it (furiten).");

        if (Tiles.IsHonor(kind))
        {
            return seen[kind] switch
            {
                >= 3 => new SafetyReading(kind, SafetyLevel.VerySafe, $"{seen[kind]} copies are already visible, so it can only be a single-tile wait, and that's unlikely."),
                2 => new SafetyReading(kind, SafetyLevel.FairlySafe, "Two copies are visible. Honors can only be shanpon or single waits, so this is fairly safe."),
                _ => new SafetyReading(kind, SafetyLevel.Risky, "Fewer than two copies visible. An honor pair waiting on shanpon is still possible."),
            };
        }

        var rank = Tiles.Rank(kind);
        var suitBase = Tiles.Suit(kind) * 9;

        // Suji: a two-sided (ryanmen) wait on this tile would also win on the tile three away, which they discarded.
        var lowSuji = rank <= 3 || Discarded(opponentDiscards, suitBase, rank - 3);
        var highSuji = rank >= 7 || Discarded(opponentDiscards, suitBase, rank + 3);
        // A ryanmen on this tile needs both neighbours on one side; four visible copies rule that side out (kabe / no-chance).
        var lowWall = rank <= 2 || Walled(seen, suitBase, rank - 1) || Walled(seen, suitBase, rank - 2);
        var highWall = rank >= 8 || Walled(seen, suitBase, rank + 1) || Walled(seen, suitBase, rank + 2);
        var lowCovered = lowSuji || lowWall;
        var highCovered = highSuji || highWall;

        if (lowCovered && highCovered)
        {
            var how = (lowSuji && highSuji) ? "Suji" : (lowWall && highWall) ? "No-chance (kabe)" : "Suji plus kabe";
            var level = Tiles.IsTerminal(kind) || rank is 2 or 8 ? SafetyLevel.VerySafe : SafetyLevel.FairlySafe;
            return new SafetyReading(kind, level, $"{how}: no two-sided wait can win on {Tiles.Short(kind)}. Kanchan, shanpon and single waits are still possible.");
        }

        if (lowCovered || highCovered)
            return new SafetyReading(kind, SafetyLevel.Risky, "Half suji: one side's two-sided wait is ruled out, but the other side is still open.");

        var midTile = rank is >= 4 and <= 6;
        return new SafetyReading(kind, midTile ? SafetyLevel.Dangerous : SafetyLevel.Risky,
            midTile ? "Middle tile with no suji. It fits the most two-sided waits, so it's the most dangerous kind of tile."
                    : "No suji or wall covers this tile.");
    }

    private static bool Discarded(ReadOnlySpan<int> discards, int suitBase, int rank) =>
        rank is >= 1 and <= 9 && discards[suitBase + rank - 1] > 0;

    private static bool Walled(ReadOnlySpan<int> seen, int suitBase, int rank) =>
        rank is >= 1 and <= 9 && seen[suitBase + rank - 1] >= 4;

    public static List<SafetyReading> ReadHand(ReadOnlySpan<int> hand, ReadOnlySpan<int> opponentDiscards, ReadOnlySpan<int> seen)
    {
        var list = new List<SafetyReading>();
        for (var k = 0; k < Tiles.KindCount; k++)
            if (hand[k] > 0) list.Add(Read(k, opponentDiscards, seen));
        return list.OrderBy(r => r.Level).ThenBy(r => r.Kind).ToList();
    }
}
