using System;
using System.Collections.Generic;
using System.Linq;

namespace Ukeire.Mahjong;

/// <summary>
/// Spots hands that are realistically heading toward a yakuman. Thresholds are deliberately strict: a yakuman
/// only shows up when the tiles are already most of the way there, and every one comes with a warning.
/// </summary>
public static class YakumanAdvisor
{
    private const string Rare = "Yakuman are rare: only chase one when your tiles are already heading there, and don't break up a good hand for it.";

    private const int Green = 32;
    private static readonly int[] GreenTiles = [19, 20, 21, 23, 25, Green]; // 2s 3s 4s 6s 8s, Green Dragon
    private static readonly int[] NineGates = [3, 1, 1, 1, 1, 1, 1, 1, 3];

    public static List<YakuPath> Suggest(ReadOnlySpan<int> concealed, IReadOnlyList<OpenMeld> melds)
    {
        var hand = concealed.ToArray();
        var paths = new List<YakuPath>();
        var closed = melds.All(m => m.IsConcealedKan);
        Span<int> all = stackalloc int[Tiles.KindCount];
        hand.CopyTo(all);
        foreach (var m in melds)
            foreach (var k in m.Kinds)
                all[k]++;
        var total = Tiles.Total(all);

        // A set is a called pon/kan or a concealed triplet; a pair only counts when there's no set of that kind.
        var sets = new int[Tiles.KindCount];
        var pairs = new int[Tiles.KindCount];
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            var called = melds.Any(m => !m.IsSequence && m.Kind == k);
            sets[k] = called || hand[k] >= 3 ? 1 : 0;
            pairs[k] = sets[k] == 0 && hand[k] == 2 ? 1 : 0;
        }
        int Count(int[] per, int from, int to) => per.Skip(from).Take(to - from + 1).Sum();
        int Held(ReadOnlySpan<int> counts, Func<int, bool> match)
        {
            var n = 0;
            for (var k = 0; k < Tiles.KindCount; k++)
                if (match(k)) n += counts[k];
            return n;
        }

        // Kokushi musou: one of each terminal and honor plus a pair. Closed only.
        if (melds.Count == 0)
        {
            var shanten = Shanten.ThirteenOrphans(hand);
            var kinds = Enumerable.Range(0, Tiles.KindCount).Count(k => Tiles.IsTerminalOrHonor(k) && hand[k] > 0);
            var thirteenSided = shanten == 0 && Tiles.Total(hand) == 13 && kinds == 13;
            var path = shanten switch
            {
                < 0 => Yakuman("Kokushi Musou (Thirteen Orphans)", YakuOutlook.Secured, "Your hand is a complete Thirteen Orphans!"),
                0 => Yakuman("Kokushi Musou (Thirteen Orphans)", YakuOutlook.Likely,
                    $"You're tenpai for Thirteen Orphans.{(thirteenSided ? " With all 13 kinds held you're waiting on any of them: the 13-sided wait, counted as a double yakuman in some rule sets." : "")}"),
                <= 2 => Yakuman("Kokushi Musou (Thirteen Orphans)", YakuOutlook.Possible,
                    $"You hold {kinds} of the 13 terminal and honor kinds. One of each plus a pair wins, but every missing kind needs exactly the right draw."),
                _ => null,
            };
            if (path != null) paths.Add(path with { ClosedOnly = true });
        }

        // Daisangen: sets of all three dragons.
        var dragonSets = Count(sets, Tiles.White, Tiles.White + 2);
        var dragonPairs = Count(pairs, Tiles.White, Tiles.White + 2);
        var daisangen = (dragonSets, dragonPairs) switch
        {
            (3, _) => Yakuman("Daisangen (Big Three Dragons)", YakuOutlook.Secured, "You have sets of all three dragons. Finish the hand and it's a yakuman."),
            (2, 1) => Yakuman("Daisangen (Big Three Dragons)", YakuOutlook.Likely, "Two dragon sets and a pair of the third. Every opponent will see it coming once the second pon is down, so expect them to hold the last dragon."),
            (2, 0) or (1, 2) => Yakuman("Daisangen (Big Three Dragons)", YakuOutlook.Possible, "You're building toward sets of all three dragons. Little Three Dragons (two sets and a pair) is the more common outcome."),
            _ => null,
        };
        if (daisangen != null) paths.Add(daisangen);

        // Suuankou: four concealed triplets. Concealed kans count; called pons don't.
        if (closed)
        {
            var triplets = Enumerable.Range(0, Tiles.KindCount).Count(k => hand[k] >= 3) + melds.Count(m => m.IsConcealedKan);
            var handPairs = Enumerable.Range(0, Tiles.KindCount).Count(k => hand[k] == 2);
            var suuankou = (triplets, handPairs) switch
            {
                (4, _) => Yakuman("Suuankou (Four Concealed Triplets)", YakuOutlook.Secured, "Four concealed triplets: waiting on your pair, any win is a yakuman."),
                (3, >= 2) => Yakuman("Suuankou (Four Concealed Triplets)", YakuOutlook.Likely, "Three concealed triplets and two pairs. Waiting on the two pairs, it only counts when you draw the tile yourself: a ron makes that triplet open, so it drops to Three Concealed Triplets and All Triplets."),
                (3, 1) => Yakuman("Suuankou (Four Concealed Triplets)", YakuOutlook.Possible, "Three concealed triplets and a pair. You'd need another pair to turn into a fourth triplet, all from your own draws."),
                (>= 2, _) when triplets + handPairs >= 5 => Yakuman("Suuankou (Four Concealed Triplets)", YakuOutlook.Possible, "Lots of concealed triplets and pairs. Don't call pon: every triplet has to come from your own draws."),
                _ => null,
            };
            if (suuankou != null) paths.Add(suuankou with { ClosedOnly = true });
        }

        // Tsuuiisou: honors only.
        var honors = Held(all, Tiles.IsHonor);
        AddAllOf(paths, "Tsuuiisou (All Honors)", honors, total, "Every tile is a wind or dragon. It has to be all triplets and a pair, since honors can't make runs.");

        // Shousuushii / Daisuushii: wind sets.
        var windSets = Count(sets, Tiles.East, Tiles.East + 3);
        var windPairs = Count(pairs, Tiles.East, Tiles.East + 3);
        var winds = (windSets, windPairs) switch
        {
            (4, _) => Yakuman("Daisuushii (Big Four Winds)", YakuOutlook.Secured, "Sets of all four winds. Any pair finishes a yakuman."),
            (3, >= 1) => Yakuman("Shousuushii (Little Four Winds)", YakuOutlook.Likely, "Three wind sets and a pair of the fourth wind is already Little Four Winds once the hand completes. A fourth set would make it Big Four Winds."),
            (3, 0) or (2, >= 2) => Yakuman("Shousuushii (Little Four Winds)", YakuOutlook.Possible, "You're collecting winds: three sets and a pair of the fourth makes Little Four Winds."),
            _ => null,
        };
        if (winds != null) paths.Add(winds);

        // Chinroutou: terminals only.
        AddAllOf(paths, "Chinroutou (All Terminals)", Held(all, Tiles.IsTerminal), total, "Only 1s and 9s. It has to be all triplets and a pair, which needs a lot of copies of very few tiles.");

        // Ryuuiisou: green tiles only.
        AddAllOf(paths, "Ryuuiisou (All Green)", Held(all, k => GreenTiles.Contains(k)), total, "Only 2, 3, 4, 6 and 8 of bamboo and the Green Dragon.");

        // Chuuren poutou: 1112345678999 in one suit plus one more. Fully closed: no kans either.
        if (melds.Count == 0)
        {
            for (var suit = 0; suit < 3; suit++)
            {
                var start = suit * 9;
                var inSuit = Held(hand, k => k >= start && k < start + 9);
                var outside = Tiles.Total(hand) - inSuit;
                if (outside > 2 || inSuit < 10) continue;
                var missing = 0;
                for (var r = 0; r < 9; r++) missing += Math.Max(0, NineGates[r] - hand[start + r]);
                var pure = Tiles.Total(hand) == 13 && Enumerable.Range(0, 9).All(r => hand[start + r] == NineGates[r]);
                var name = $"Chuuren Poutou (Nine Gates, {Tiles.SuitName(suit)})";
                var path = (missing, outside, pure) switch
                {
                    (_, _, true) => Yakuman(name, YakuOutlook.Likely,
                        "You're holding 1112345678999: True Nine Gates, waiting on any of the nine tiles in the suit. This is about the rarest hand in the game. Most players never see one."),
                    (0, 0, _) => Yakuman(name, YakuOutlook.Likely,
                        "You have the 1112345678999 shape in one suit. Nine Gates is one of the rarest yakuman, and the true nine-sided wait is rarer still."),
                    (<= 2, _, _) => Yakuman(name, YakuOutlook.Possible,
                        "Almost all one suit and close to the 1112345678999 shape. Nine Gates is one of the rarest yakuman, so keep the flush as your realistic goal."),
                    _ => null,
                };
                if (path != null) paths.Add(path with { ClosedOnly = true });
            }
        }

        // Suukantsu: four kans.
        var kans = melds.Count(m => m.Kinds.Count == 4);
        var suukantsu = kans switch
        {
            4 => Yakuman("Suukantsu (Four Quads)", YakuOutlook.Likely, "Four kans! You only need the pair. This is the rarest yakuman of all."),
            >= 2 => Yakuman("Suukantsu (Four Quads)", YakuOutlook.Possible,
                $"{kans} kans so far. Four Quads is the rarest yakuman of all: it needs all four copies of four different tiles, and the game is usually drawn if two players make four kans between them. Treat this as a curiosity, not a plan."),
            _ => null,
        };
        if (suukantsu != null) paths.Add(suukantsu);

        return paths;
    }

    /// <summary>For the "every tile is one of these" yakuman: shown when at most two tiles don't fit.</summary>
    private static void AddAllOf(List<YakuPath> paths, string name, int fitting, int total, string what)
    {
        var path = (total - fitting, fitting) switch
        {
            (0, >= 10) => Yakuman(name, YakuOutlook.Likely, $"{what} Every tile you hold fits."),
            (<= 2, >= 10) => Yakuman(name, YakuOutlook.Possible, $"{what} Only {total - fitting} of your tiles don't fit."),
            _ => null,
        };
        if (path != null) paths.Add(path);
    }

    private static YakuPath Yakuman(string name, YakuOutlook outlook, string explanation) =>
        new(name, outlook, $"{explanation} {Rare}", Yakuman: true);
}
