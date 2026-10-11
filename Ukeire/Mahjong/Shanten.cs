using System;

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

    public static int Regular(ReadOnlySpan<int> counts, int openMelds)
    {
        Span<int> work = stackalloc int[Tiles.KindCount];
        counts.CopyTo(work);
        var best = 8;
        Search(work, 0, 0, 0, false, openMelds, ref best);
        return best;
    }

    private static void Search(Span<int> c, int i, int sets, int partials, bool pair, int openMelds, ref int best)
    {
        while (i < Tiles.KindCount && c[i] == 0) i++;
        if (i == Tiles.KindCount)
        {
            var melds = sets + openMelds;
            if (melds + partials > 4) partials = 4 - melds;
            var value = 8 - (2 * melds) - partials - (pair ? 1 : 0);
            if (value < best) best = value;
            return;
        }

        var suited = i < 27;
        var rank = i % 9;

        if (c[i] >= 3)
        {
            c[i] -= 3;
            Search(c, i, sets + 1, partials, pair, openMelds, ref best);
            c[i] += 3;
        }
        if (suited && rank <= 6 && c[i + 1] > 0 && c[i + 2] > 0)
        {
            c[i]--; c[i + 1]--; c[i + 2]--;
            Search(c, i, sets + 1, partials, pair, openMelds, ref best);
            c[i]++; c[i + 1]++; c[i + 2]++;
        }
        if (c[i] >= 2)
        {
            c[i] -= 2;
            if (!pair) Search(c, i, sets, partials, true, openMelds, ref best);
            Search(c, i, sets, partials + 1, pair, openMelds, ref best);
            c[i] += 2;
        }
        if (suited && rank <= 7 && c[i + 1] > 0)
        {
            c[i]--; c[i + 1]--;
            Search(c, i, sets, partials + 1, pair, openMelds, ref best);
            c[i]++; c[i + 1]++;
        }
        if (suited && rank <= 6 && c[i + 2] > 0)
        {
            c[i]--; c[i + 2]--;
            Search(c, i, sets, partials + 1, pair, openMelds, ref best);
            c[i]++; c[i + 2]++;
        }

        // Leave the remaining copies of this tile floating.
        var saved = c[i];
        c[i] = 0;
        Search(c, i + 1, sets, partials, pair, openMelds, ref best);
        c[i] = saved;
    }
}
