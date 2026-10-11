using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ukeire.Mahjong;

/// <summary>
/// Tile kinds use the engine's 0-33 numbering: 0-8 characters (m), 9-17 dots (p), 18-26 bamboo (s),
/// 27-30 East/South/West/North, 31-33 White/Green/Red.
/// </summary>
public static class Tiles
{
    public const int KindCount = 34;
    public const int East = 27;
    public const int White = 31;

    private static readonly string[] SuitLetters = ["m", "p", "s"];
    private static readonly string[] HonorShort = ["E", "S", "W", "N", "Wh", "Gr", "Rd"];
    private static readonly string[] HonorNames = ["East", "South", "West", "North", "White Dragon", "Green Dragon", "Red Dragon"];
    private static readonly string[] SuitNames = ["Characters", "Dots", "Bamboo"];

    public static bool IsValid(int kind) => kind is >= 0 and < KindCount;
    public static bool IsHonor(int kind) => kind >= 27;
    public static bool IsDragon(int kind) => kind >= 31;
    public static bool IsWind(int kind) => kind is >= 27 and <= 30;
    public static int Suit(int kind) => kind / 9;
    public static int Rank(int kind) => (kind % 9) + 1;
    public static bool IsTerminal(int kind) => !IsHonor(kind) && Rank(kind) is 1 or 9;
    public static bool IsTerminalOrHonor(int kind) => IsHonor(kind) || IsTerminal(kind);
    public static bool IsSimple(int kind) => !IsTerminalOrHonor(kind);

    public static string Short(int kind)
    {
        if (!IsValid(kind)) return "?";
        return IsHonor(kind) ? HonorShort[kind - 27] : $"{Rank(kind)}{SuitLetters[Suit(kind)]}";
    }

    public static string Name(int kind)
    {
        if (!IsValid(kind)) return "Unknown";
        return IsHonor(kind) ? HonorNames[kind - 27] : $"{Rank(kind)} of {SuitNames[Suit(kind)]}";
    }

    public static string SuitName(int suit) => suit < 3 ? SuitNames[suit] : "Honors";

    /// <summary>The tile a dora indicator points at.</summary>
    public static int DoraFromIndicator(int indicator)
    {
        if (indicator < 27) return (Suit(indicator) * 9) + ((indicator % 9) + 1) % 9;
        if (indicator <= 30) return 27 + ((indicator - 27 + 1) % 4);
        return 31 + ((indicator - 31 + 1) % 3);
    }

    /// <summary>Writes a count array in compact notation, e.g. "123m 456p 77s EE".</summary>
    public static string Notation(ReadOnlySpan<int> counts)
    {
        var sb = new StringBuilder();
        for (var suit = 0; suit < 3; suit++)
        {
            var any = false;
            for (var r = 0; r < 9; r++)
            {
                for (var n = 0; n < counts[(suit * 9) + r]; n++)
                {
                    sb.Append(r + 1);
                    any = true;
                }
            }
            if (any) sb.Append(SuitLetters[suit]).Append(' ');
        }
        for (var k = 27; k < KindCount; k++)
            for (var n = 0; n < counts[k]; n++)
                sb.Append(HonorShort[k - 27]).Append(' ');
        return sb.ToString().TrimEnd();
    }

    public static string Join(IEnumerable<int> kinds) => string.Join(" ", kinds.Select(Short));

    public static int Total(ReadOnlySpan<int> counts)
    {
        var total = 0;
        foreach (var c in counts) total += c;
        return total;
    }
}
