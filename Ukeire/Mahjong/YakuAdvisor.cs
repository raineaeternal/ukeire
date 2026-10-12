using System;
using System.Collections.Generic;
using System.Linq;

namespace Ukeire.Mahjong;

public enum YakuOutlook
{
    Secured,
    Likely,
    Possible,
}

public sealed record YakuPath(string Name, YakuOutlook Outlook, string Explanation);

public sealed record OpenMeld(IReadOnlyList<int> Kinds, bool IsConcealedKan)
{
    public bool IsSequence => Kinds.Count == 3 && Kinds[0] != Kinds[1];
    public int Kind => Kinds[0];
}

/// <summary>
/// Suggests which yaku the hand can aim for. A hand with open melds and no yaku cannot win, a common beginner trap.
/// </summary>
public static class YakuAdvisor
{
    public static List<YakuPath> Suggest(ReadOnlySpan<int> hand, IReadOnlyList<OpenMeld> melds, int roundWind, int seatWind)
    {
        var paths = new List<YakuPath>();
        var closed = melds.All(m => m.IsConcealedKan);
        Span<int> all = stackalloc int[Tiles.KindCount];
        hand.CopyTo(all);
        foreach (var m in melds)
            foreach (var k in m.Kinds)
                all[k]++;
        var total = Tiles.Total(all);

        if (closed)
            paths.Add(new YakuPath("Riichi", YakuOutlook.Possible,
                "Your hand is closed, so declaring riichi at tenpai always gives you a yaku. Closed hands are the safest default while you're learning."));

        // Yakuhai: dragons, round wind, seat wind.
        for (var k = 27; k < Tiles.KindCount; k++)
        {
            var valuable = Tiles.IsDragon(k) || k == roundWind || k == seatWind;
            if (!valuable) continue;
            var why = Tiles.IsDragon(k) ? "dragon" : (k == roundWind && k == seatWind) ? "round and seat wind (double value)" : k == roundWind ? "round wind" : "seat wind";
            var name = $"Yakuhai ({Tiles.Short(k)})";
            var yakuhai = (called: melds.Any(m => !m.IsSequence && m.Kind == k), held: hand[k]) switch
            {
                (true, _) => new YakuPath(name, YakuOutlook.Secured, $"You have a set of {Tiles.Name(k)}, a {why}. That's one han and a yaku."),
                (_, >= 3) => new YakuPath(name, YakuOutlook.Secured, $"A concealed triplet of {Tiles.Name(k)} ({why}) is already a yaku."),
                (_, 2) => new YakuPath(name, YakuOutlook.Likely, $"A pair of {Tiles.Name(k)} ({why}). Calling pon on the third copy gives you a yaku and lets you open your hand safely."),
                _ => null,
            };
            if (yakuhai != null) paths.Add(yakuhai);
        }

        // Tanyao: all simples.
        var nonSimples = 0;
        for (var k = 0; k < Tiles.KindCount; k++)
            if (!Tiles.IsSimple(k)) nonSimples += all[k];
        var meldsBlockTanyao = melds.Any(m => m.Kinds.Any(k => !Tiles.IsSimple(k)));
        if (!meldsBlockTanyao)
        {
            if (nonSimples == 0)
                paths.Add(new YakuPath("Tanyao (All Simples)", YakuOutlook.Likely, "Every tile is 2-8. Keep it that way and you have a yaku, open or closed."));
            else if (nonSimples <= 2)
                paths.Add(new YakuPath("Tanyao (All Simples)", YakuOutlook.Possible, $"Only {nonSimples} terminal/honor tile(s). Letting them go would set up All Simples, which works even with an open hand."));
        }

        // Flushes.
        Span<int> suitCount = stackalloc int[3];
        var honors = 0;
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            if (Tiles.IsHonor(k)) honors += all[k];
            else suitCount[Tiles.Suit(k)] += all[k];
        }
        var mainSuit = 0;
        for (var s = 1; s < 3; s++)
            if (suitCount[s] > suitCount[mainSuit]) mainSuit = s;
        var offSuit = total - suitCount[mainSuit] - honors;
        var flush = (offSuit, honors) switch
        {
            (0, 0) => new YakuPath("Chinitsu (Full Flush)", YakuOutlook.Likely, $"Everything is {Tiles.SuitName(mainSuit)}. That's six han closed, five open."),
            (0, _) => new YakuPath("Honitsu (Half Flush)", YakuOutlook.Likely, $"Only {Tiles.SuitName(mainSuit)} and honors. That's three han closed, two open."),
            (<= 3, _) when suitCount[mainSuit] + honors >= 9 =>
                new YakuPath("Honitsu (Half Flush)", YakuOutlook.Possible, $"{offSuit} tile(s) outside {Tiles.SuitName(mainSuit)} and honors. Dropping them would aim you at a half flush."),
            _ => null,
        };
        if (flush != null) paths.Add(flush);

        // Pair-based hands.
        var pairs = 0;
        var triplets = melds.Count(m => !m.IsSequence);
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            if (hand[k] >= 3) triplets++;
            else if (hand[k] == 2) pairs++;
        }
        if (closed && pairs + triplets >= 4 && melds.Count == 0)
            paths.Add(new YakuPath("Chiitoitsu (Seven Pairs)", pairs >= 5 ? YakuOutlook.Likely : YakuOutlook.Possible,
                $"You have {pairs + triplets} pairs. Seven different pairs is a closed-only yaku worth two han."));
        if (triplets + pairs >= 4 && melds.All(m => !m.IsSequence))
            paths.Add(new YakuPath("Toitoi (All Triplets)", triplets >= 2 ? YakuOutlook.Likely : YakuOutlook.Possible,
                "Lots of pairs and triplets. Calling pon to build four triplets is worth two han."));

        return paths;
    }

    /// <summary>True when the hand is open and none of the open-compatible yaku are reachable: it can never win.</summary>
    public static bool IsOpenWithoutYaku(IReadOnlyList<OpenMeld> melds, List<YakuPath> paths)
    {
        if (melds.All(m => m.IsConcealedKan)) return false;
        return !paths.Any(p => p.Name.StartsWith("Yakuhai") || p.Name.StartsWith("Tanyao") || p.Name.StartsWith("Honitsu") ||
                               p.Name.StartsWith("Chinitsu") || p.Name.StartsWith("Toitoi"));
    }
}
