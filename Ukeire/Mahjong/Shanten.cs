using System;
using System.Collections.Concurrent;

namespace Ukeire.Mahjong;

public enum HandForm
{
    Regular,
    SevenPairs,
    ThirteenOrphans,
}

public readonly record struct ShantenResult(int Value, HandForm Form)
{
    public bool IsTenpai => Value == 0;
    public bool IsComplete => Value < 0;
}

/// <summary>
/// Shanten = how many tile swaps away from tenpai. -1 is a complete hand, 0 is tenpai.
/// </summary>
public static class Shanten
{
    public static ShantenResult Calculate(ReadOnlySpan<int> counts, int openMelds)
    {
        var best = new ShantenResult(Regular(counts, openMelds), HandForm.Regular);

        // Seven pairs and thirteen orphans are only possible with a fully concealed hand.
        if (openMelds == 0)
        {
            var pairs = SevenPairs(counts);
            if (pairs < best.Value) best = new ShantenResult(pairs, HandForm.SevenPairs);

            var orphans = ThirteenOrphans(counts);
            if (orphans < best.Value) best = new ShantenResult(orphans, HandForm.ThirteenOrphans);
        }

        return best;
    }

    public static int SevenPairs(ReadOnlySpan<int> counts)
    {
        int pairs = 0, kinds = 0;
        foreach (var c in counts)
        {
            if (c >= 1) kinds++;
            if (c >= 2) pairs++;
        }
        return 6 - pairs + Math.Max(0, 7 - kinds);
    }

    public static int ThirteenOrphans(ReadOnlySpan<int> counts)
    {
        int kinds = 0;
        var hasPair = false;
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            if (!Tiles.IsTerminalOrHonor(k) || counts[k] == 0) continue;
            kinds++;
            if (counts[k] >= 2) hasPair = true;
        }
        return 13 - kinds - (hasPair ? 1 : 0);
    }

    // ---------------------------------------------------------------------------------------
    // Regular hands (four melds + a pair)
    //
    // The three suits and the honors never form blocks with each other, so each of those four
    // "groups" is analysed on its own. For one group we compute a small table:
    //
    //     table[pairs, melds] = the most partial blocks (two-tile runs, or pairs not used as the
    //                           hand's pair) that can accompany exactly `melds` complete melds,
    //                           with `pairs` (0 or 1) of the hand's pair taken from this group.
    //
    // Tables are cached per tile pattern, and the four group tables are then merged with a
    // tiny DP. This replaces one search whose cost multiplies across suits with four searches
    // whose costs add (and, once cached, are just lookups).
    // ---------------------------------------------------------------------------------------

    private const int SuitCount = 3;
    private const int SuitSize = 9;
    private const int HonorStart = SuitCount * SuitSize;
    private const int HonorCount = 7;

    private const int MaxMelds = 4;
    private const int WorstShanten = 8;

    /// <summary>Marks a (pairs, melds) combination that no decomposition of the group can reach.</summary>
    private const sbyte Impossible = -1;

    /// <summary>Two rows (no pair / pair) of MaxMelds + 1 columns (0..MaxMelds melds).</summary>
    private const int TableSize = 2 * (MaxMelds + 1);

    /// <summary>
    /// Each cache is emptied once it holds this many patterns, so a long session can't grow it without limit.
    /// A table is about 100 bytes with its entry, and a game only revisits a few thousand patterns.
    /// </summary>
    private const int MaxCachedPatterns = 100_000;

    private static readonly ConcurrentDictionary<int, sbyte[]> SuitTables = new();
    private static readonly ConcurrentDictionary<int, sbyte[]> HonorTables = new();

    public static int Regular(ReadOnlySpan<int> counts, int openMelds)
    {
        // Start with "no tiles examined yet": zero melds, zero partials, no pair.
        Span<sbyte> total = stackalloc sbyte[TableSize];
        Span<sbyte> merged = stackalloc sbyte[TableSize];
        total.Fill(Impossible);
        total[TableIndex(pairs: 0, melds: 0)] = 0;

        for (var group = 0; group < SuitCount + 1; group++)
        {
            Merge(total, TableFor(counts, group), merged);
            merged.CopyTo(total);
        }

        return BestShanten(total, openMelds);
    }

    private static int TableIndex(int pairs, int melds) => pairs * (MaxMelds + 1) + melds;

    /// <summary>Looks up (or computes and caches) the table for group 0-2 (suits) or 3 (honors).</summary>
    private static ReadOnlySpan<sbyte> TableFor(ReadOnlySpan<int> counts, int group)
    {
        var isSuit = group < SuitCount;
        var tiles = isSuit
            ? counts.Slice(group * SuitSize, SuitSize)
            : counts.Slice(HonorStart, HonorCount);
        var cache = isSuit ? SuitTables : HonorTables;

        var key = EncodePattern(tiles);
        if (cache.TryGetValue(key, out var cached)) return cached;

        var table = new GroupSearch(tiles, isSuit).BuildTable();
        // Only checked on a miss: counting a concurrent dictionary is too costly for the lookup path.
        if (cache.Count > MaxCachedPatterns) cache.Clear();
        return cache.GetOrAdd(key, table);
    }

    /// <summary>Packs tile counts (each 0-4) into a base-5 number, so every pattern has a unique key.</summary>
    private static int EncodePattern(ReadOnlySpan<int> tiles)
    {
        var key = 0;
        for (var i = tiles.Length - 1; i >= 0; i--) key = key * 5 + tiles[i];
        return key;
    }

    /// <summary>
    /// Combines the best-partials tables of two sets of groups into one for their union.
    /// Melds add up (capped at MaxMelds); the hand has at most one pair.
    /// </summary>
    private static void Merge(ReadOnlySpan<sbyte> left, ReadOnlySpan<sbyte> right, Span<sbyte> result)
    {
        result.Fill(Impossible);

        for (var leftPairs = 0; leftPairs <= 1; leftPairs++)
        for (var leftMelds = 0; leftMelds <= MaxMelds; leftMelds++)
        {
            var leftPartials = left[TableIndex(leftPairs, leftMelds)];
            if (leftPartials == Impossible) continue;

            for (var rightPairs = 0; leftPairs + rightPairs <= 1; rightPairs++)
            for (var rightMelds = 0; leftMelds + rightMelds <= MaxMelds; rightMelds++)
            {
                var rightPartials = right[TableIndex(rightPairs, rightMelds)];
                if (rightPartials == Impossible) continue;

                var slot = TableIndex(leftPairs + rightPairs, leftMelds + rightMelds);
                var partials = (sbyte)(leftPartials + rightPartials);
                if (partials > result[slot]) result[slot] = partials;
            }
        }
    }

    /// <summary>
    /// Turns the merged table into a shanten number using the standard formula:
    ///   8 - 2 * melds - partials - pair,
    /// where melds + partials is capped at 4 (extra partial blocks can't help).
    /// </summary>
    private static int BestShanten(ReadOnlySpan<sbyte> table, int openMelds)
    {
        var best = WorstShanten;

        for (var pairs = 0; pairs <= 1; pairs++)
        for (var concealedMelds = 0; concealedMelds + openMelds <= MaxMelds; concealedMelds++)
        {
            var partials = table[TableIndex(pairs, concealedMelds)];
            if (partials == Impossible) continue;

            var melds = concealedMelds + openMelds;
            var usablePartials = Math.Min(partials, MaxMelds - melds);
            var shanten = WorstShanten - 2 * melds - usablePartials - pairs;
            if (shanten < best) best = shanten;
        }

        return best;
    }

    /// <summary>
    /// Exhaustively decomposes one group (a suit, or the honors) into melds, partial blocks and
    /// at most one pair, recording the best partial count for each (pairs, melds) outcome.
    /// Only runs on a cache miss; the group is tiny, so this is cheap.
    /// </summary>
    private sealed class GroupSearch
    {
        // Each block is described by the tile offsets (from the current tile) that it consumes.
        private static readonly int[] Triplet = { 0, 0, 0 };
        private static readonly int[] Run = { 0, 1, 2 };
        private static readonly int[] Pair = { 0, 0 };
        private static readonly int[] AdjacentTiles = { 0, 1 };   // e.g. 4-5 (or 1-2 / 8-9)
        private static readonly int[] GappedTiles = { 0, 2 };     // e.g. 4-6

        private readonly int[] _tiles;
        private readonly bool _isSuit;
        private readonly sbyte[] _table = new sbyte[TableSize];

        public GroupSearch(ReadOnlySpan<int> tiles, bool isSuit)
        {
            _tiles = tiles.ToArray();
            _isSuit = isSuit;
            Array.Fill(_table, Impossible);
        }

        public sbyte[] BuildTable()
        {
            Search(start: 0, melds: 0, partials: 0, hasPair: false);
            return _table;
        }

        private void Search(int start, int melds, int partials, bool hasPair)
        {
            var i = start;
            while (i < _tiles.Length && _tiles[i] == 0) i++;

            if (i == _tiles.Length)
            {
                Record(melds, partials, hasPair);
                return;
            }

            var canAddMeld = melds < MaxMelds;
            var hasNext = i + 1 < _tiles.Length && _tiles[i + 1] > 0;
            var hasAfterNext = i + 2 < _tiles.Length && _tiles[i + 2] > 0;

            if (_tiles[i] >= 3 && canAddMeld)
                Take(Triplet, i, melds + 1, partials, hasPair);

            if (_isSuit && hasNext && hasAfterNext && canAddMeld)
                Take(Run, i, melds + 1, partials, hasPair);

            if (_tiles[i] >= 2)
            {
                if (!hasPair) Take(Pair, i, melds, partials, hasPair: true);      // as the hand's pair
                Take(Pair, i, melds, partials + 1, hasPair);                      // as a partial block
            }

            if (_isSuit && hasNext)
                Take(AdjacentTiles, i, melds, partials + 1, hasPair);

            if (_isSuit && hasAfterNext)
                Take(GappedTiles, i, melds, partials + 1, hasPair);

            // Or leave every remaining copy of this tile unused and move on.
            var leftover = _tiles[i];
            _tiles[i] = 0;
            Search(i + 1, melds, partials, hasPair);
            _tiles[i] = leftover;
        }

        /// <summary>Removes a block's tiles, searches the rest of the group, then puts them back.</summary>
        private void Take(int[] block, int i, int melds, int partials, bool hasPair)
        {
            foreach (var offset in block) _tiles[i + offset]--;
            Search(i, melds, partials, hasPair);
            foreach (var offset in block) _tiles[i + offset]++;
        }

        private void Record(int melds, int partials, bool hasPair)
        {
            var slot = TableIndex(hasPair ? 1 : 0, melds);
            var capped = (sbyte)Math.Min(partials, MaxMelds);   // more than 4 can never be used
            if (capped > _table[slot]) _table[slot] = capped;
        }
    }
}
