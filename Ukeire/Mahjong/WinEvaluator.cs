using System;
using System.Collections.Generic;
using System.Linq;

namespace Ukeire.Mahjong;

public sealed record YakuHan(string Name, int Han);

/// <param name="Yakuman">Only Thirteen Orphans is detected; the other yakuman are left to <see cref="YakumanAdvisor"/>.</param>
public sealed record WinReading(IReadOnlyList<YakuHan> Yaku, bool Yakuman = false)
{
    public static readonly WinReading None = new([]);
    public int Han => Yaku.Sum(y => y.Han);
    public bool HasYaku => Yaku.Count > 0;
}

/// <summary>What a winning tile would be worth. Dora, ura-dora and one-off yaku (ippatsu, haitei, rinshan, chankan) aren't counted.</summary>
public sealed record WaitReading(int Kind, int Remaining, WinReading Ron, WinReading Tsumo)
{
    /// <summary>True when the wait wins by either route. A closed hand always can: tsumo itself is a yaku.</summary>
    public bool CanWin => Ron.HasYaku || Tsumo.HasYaku;
}

/// <summary>
/// Works out which yaku a complete hand has. Like Akagi's score module, it is used by probing: for a tenpai hand,
/// every tile that completes it is tried and the finished hand is read exactly, instead of guessing from the shape.
/// </summary>
public static class WinEvaluator
{
    private readonly record struct Group(int Kind, bool Sequence, bool Open, bool Kan);

    /// <summary>The waits of a tenpai (3n+1) hand, each read as a win. Empty when the hand isn't tenpai.</summary>
    public static List<WaitReading> Waits(int[] hand, IReadOnlyList<OpenMeld> melds, int roundWind, int seatWind, int[] visible)
    {
        var readings = new List<WaitReading>();
        var meldTiles = new int[Tiles.KindCount];
        foreach (var m in melds)
            foreach (var k in m.Kinds) meldTiles[k]++;
        var work = (int[])hand.Clone();
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            if (work[k] + meldTiles[k] >= 4) continue;
            work[k]++;
            var complete = Shanten.Calculate(work, melds.Count).IsComplete;
            work[k]--;
            if (!complete) continue;
            var remaining = Math.Max(0, 4 - hand[k] - visible[k]);
            readings.Add(new WaitReading(k, remaining,
                Evaluate(hand, melds, k, false, roundWind, seatWind) ?? WinReading.None,
                Evaluate(hand, melds, k, true, roundWind, seatWind) ?? WinReading.None));
        }
        return readings;
    }

    /// <summary>The best reading of the hand completed by <paramref name="winTile"/>, or null when it isn't a winning shape.</summary>
    public static WinReading? Evaluate(int[] hand13, IReadOnlyList<OpenMeld> melds, int winTile, bool tsumo, int roundWind, int seatWind, bool riichi = false)
    {
        var closedHand = (int[])hand13.Clone();
        closedHand[winTile]++;
        var closed = melds.All(m => m.IsConcealedKan);
        var meldGroups = melds.Select(m => new Group(m.IsSequence ? m.Kinds.Min() : m.Kind, m.IsSequence, !m.IsConcealedKan, m.Kinds.Count == 4)).ToList();

        var candidates = new List<WinReading>();
        if (melds.Count == 0)
        {
            if (Shanten.ThirteenOrphans(closedHand) < 0)
                candidates.Add(new WinReading([new YakuHan("Thirteen Orphans", 13)], true));
            if (IsSevenPairs(closedHand))
                candidates.Add(SevenPairs(closedHand, tsumo, riichi));
        }

        for (var pair = 0; pair < Tiles.KindCount; pair++)
        {
            if (closedHand[pair] < 2) continue;
            closedHand[pair] -= 2;
            var splits = new List<List<Group>>();
            Split(closedHand, 0, [], splits);
            closedHand[pair] += 2;
            foreach (var split in splits)
                foreach (var winGroup in WinPlacements(split, pair, winTile))
                    candidates.Add(Score(split, meldGroups, pair, winGroup, winTile, tsumo, closed, riichi, roundWind, seatWind));
        }

        if (candidates.Count == 0) return null;
        return candidates.MaxBy(c => (c.Yakuman, c.Han))!;
    }

    private static void Split(int[] c, int i, List<Group> sofar, List<List<Group>> results)
    {
        while (i < Tiles.KindCount && c[i] == 0) i++;
        if (i == Tiles.KindCount)
        {
            results.Add([.. sofar]);
            return;
        }
        if (c[i] >= 3)
        {
            c[i] -= 3;
            sofar.Add(new Group(i, false, false, false));
            Split(c, i, sofar, results);
            sofar.RemoveAt(sofar.Count - 1);
            c[i] += 3;
        }
        if (i < 27 && i % 9 <= 6 && c[i + 1] > 0 && c[i + 2] > 0)
        {
            c[i]--; c[i + 1]--; c[i + 2]--;
            sofar.Add(new Group(i, true, false, false));
            Split(c, i, sofar, results);
            sofar.RemoveAt(sofar.Count - 1);
            c[i]++; c[i + 1]++; c[i + 2]++;
        }
    }

    /// <summary>Which part of the split the winning tile can have completed: -1 for the pair, otherwise a group index.</summary>
    private static IEnumerable<int> WinPlacements(List<Group> split, int pair, int winTile)
    {
        if (pair == winTile) yield return -1;
        var tried = new HashSet<(int, bool)>();
        for (var i = 0; i < split.Count; i++)
        {
            var g = split[i];
            var holds = g.Sequence ? winTile >= g.Kind && winTile <= g.Kind + 2 : winTile == g.Kind;
            if (holds && tried.Add((g.Kind, g.Sequence))) yield return i;
        }
    }

    private static bool IsSevenPairs(int[] c) => c.Count(n => n == 2) == 7;

    private static WinReading SevenPairs(int[] c, bool tsumo, bool riichi)
    {
        var yaku = new List<YakuHan> { new("Chiitoitsu (Seven Pairs)", 2) };
        if (riichi) yaku.Add(new YakuHan("Riichi", 1));
        if (tsumo) yaku.Add(new YakuHan("Menzen Tsumo", 1));
        var kinds = Enumerable.Range(0, Tiles.KindCount).Where(k => c[k] > 0).ToList();
        if (kinds.All(Tiles.IsSimple)) yaku.Add(new YakuHan("Tanyao (All Simples)", 1));
        if (kinds.All(Tiles.IsTerminalOrHonor)) yaku.Add(new YakuHan("Honroutou (All Terminals and Honors)", 2));
        AddFlush(yaku, kinds, closed: true);
        return new WinReading(yaku);
    }

    private static void AddFlush(List<YakuHan> yaku, IEnumerable<int> kinds, bool closed)
    {
        var list = kinds.ToList();
        var suits = list.Where(k => !Tiles.IsHonor(k)).Select(Tiles.Suit).Distinct().Count();
        if (suits != 1) return;
        yaku.Add(list.Any(Tiles.IsHonor)
            ? new YakuHan("Honitsu (Half Flush)", closed ? 3 : 2)
            : new YakuHan("Chinitsu (Full Flush)", closed ? 6 : 5));
    }

    private static WinReading Score(List<Group> split, List<Group> melds, int pair, int winGroup, int winTile, bool tsumo, bool closed, bool riichi, int roundWind, int seatWind)
    {
        var groups = split.Concat(melds).ToList();
        var yaku = new List<YakuHan>();
        var han = closed ? 1 : 0; // the extra han a closed hand earns on sanshoku, ittsu and chanta

        if (riichi && closed) yaku.Add(new YakuHan("Riichi", 1));
        if (tsumo && closed) yaku.Add(new YakuHan("Menzen Tsumo", 1));

        // Wait shape: the winning tile's role decides pinfu, and a ron on a triplet makes it open for sanankou.
        var waitIsRyanmen = false;
        var ronOnTriplet = false;
        if (winGroup >= 0)
        {
            var g = split[winGroup];
            if (g.Sequence)
            {
                var low = winTile == g.Kind;
                var high = winTile == g.Kind + 2;
                var penchan = (low && Tiles.Rank(g.Kind) == 7) || (high && Tiles.Rank(g.Kind) == 1);
                waitIsRyanmen = (low || high) && !penchan;
            }
            else ronOnTriplet = !tsumo;
        }

        var yakuhaiPair = Tiles.IsDragon(pair) || pair == roundWind || pair == seatWind;
        if (closed && groups.All(g => g.Sequence) && !yakuhaiPair && waitIsRyanmen)
            yaku.Add(new YakuHan("Pinfu", 1));

        var allKinds = TilesOf(groups, pair).ToList();
        if (allKinds.All(Tiles.IsSimple)) yaku.Add(new YakuHan("Tanyao (All Simples)", 1));

        foreach (var g in groups.Where(g => !g.Sequence))
        {
            if (Tiles.IsDragon(g.Kind)) yaku.Add(new YakuHan($"Yakuhai ({Tiles.Short(g.Kind)})", 1));
            if (g.Kind == roundWind) yaku.Add(new YakuHan($"Round wind ({Tiles.Short(g.Kind)})", 1));
            if (g.Kind == seatWind) yaku.Add(new YakuHan($"Seat wind ({Tiles.Short(g.Kind)})", 1));
        }

        var sequences = groups.Where(g => g.Sequence).Select(g => g.Kind).ToList();
        var triplets = groups.Where(g => !g.Sequence).ToList();
        if (closed)
        {
            var identical = sequences.GroupBy(k => k).Sum(g => g.Count() / 2);
            if (identical >= 2) yaku.Add(new YakuHan("Ryanpeikou", 3));
            else if (identical == 1) yaku.Add(new YakuHan("Iipeikou", 1));
        }

        for (var r = 0; r < 9; r++)
        {
            if (r <= 6 && sequences.Contains(r) && sequences.Contains(9 + r) && sequences.Contains(18 + r))
                yaku.Add(new YakuHan("Sanshoku Doujun", 1 + han));
            if (triplets.Any(t => t.Kind == r) && triplets.Any(t => t.Kind == 9 + r) && triplets.Any(t => t.Kind == 18 + r))
                yaku.Add(new YakuHan("Sanshoku Doukou", 2));
        }
        for (var s = 0; s < 3; s++)
            if (sequences.Contains(s * 9) && sequences.Contains((s * 9) + 3) && sequences.Contains((s * 9) + 6))
                yaku.Add(new YakuHan("Ittsu", 1 + han));

        var touches = groups.Select(g => g.Sequence ? g.Kind % 9 is 0 or 6 : Tiles.IsTerminalOrHonor(g.Kind)).All(b => b) && Tiles.IsTerminalOrHonor(pair);
        if (touches)
        {
            if (sequences.Count == 0) yaku.Add(new YakuHan("Honroutou (All Terminals and Honors)", 2));
            else yaku.Add(allKinds.Any(Tiles.IsHonor) ? new YakuHan("Chanta", 1 + han) : new YakuHan("Junchan", 2 + han));
        }

        if (triplets.Count == 4) yaku.Add(new YakuHan("Toitoi (All Triplets)", 2));
        var concealed = triplets.Count(t => !t.Open) - (ronOnTriplet ? 1 : 0);
        if (concealed >= 3) yaku.Add(new YakuHan("Sanankou (Three Concealed Triplets)", 2));
        if (groups.Count(g => g.Kan) >= 3) yaku.Add(new YakuHan("Sankantsu (Three Quads)", 2));
        if (triplets.Count(t => Tiles.IsDragon(t.Kind)) == 2 && Tiles.IsDragon(pair))
            yaku.Add(new YakuHan("Shousangen (Little Three Dragons)", 2));

        AddFlush(yaku, allKinds, closed);
        return new WinReading(yaku);
    }

    private static IEnumerable<int> TilesOf(List<Group> groups, int pair)
    {
        yield return pair;
        foreach (var g in groups)
        {
            yield return g.Kind;
            if (g.Sequence)
            {
                yield return g.Kind + 1;
                yield return g.Kind + 2;
            }
        }
    }
}
