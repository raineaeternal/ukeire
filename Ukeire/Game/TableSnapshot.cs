using System;
using System.Collections.Generic;
using System.Linq;
using Ukeire.Mahjong;

namespace Ukeire.Game;

public sealed record DiscardEntry(int Kind, uint Flags);

/// <summary>
/// What the local player can legitimately see: their own hand, every discard pile and every called meld.
/// Opponents' concealed hands are never read.
/// </summary>
public sealed record TableSnapshot
{
    public static readonly TableSnapshot Inactive = new() { IsActive = false, Status = "Not at a Doman Mahjong table." };

    public bool IsActive { get; init; } = true;
    public string Status { get; init; } = string.Empty;
    public int LocalSeat { get; init; }
    public int[] Hand { get; init; } = new int[Tiles.KindCount];
    public int[] RedFives { get; init; } = new int[Tiles.KindCount];
    public int HandTileCount { get; init; }

    /// <summary>Tile kind just drawn, or -1. Already included in <see cref="Hand"/>.</summary>
    public int DrawnTile { get; init; } = -1;
    public bool DrawnTileIsRed { get; init; }

    /// <summary>Player.HandTiles and Player.HandTileCount as read, for the debug tab.</summary>
    public uint[] RawHandTiles { get; init; } = [];
    public int RawHandTileCount { get; init; }

    /// <summary>Table.DrawnTile as read, for the debug tab.</summary>
    public (uint Seat, uint Tile, uint Flags) RawDrawnTile { get; init; }
    public bool GameSaysCanRiichi { get; init; }

    /// <summary>Seat and tile kind of the most recent discard (Table.LastDiscard), or -1.</summary>
    public int LastDiscardSeat { get; init; } = -1;
    public int LastDiscardKind { get; init; } = -1;

    /// <summary>Table.LastDiscard as read, for the debug tab.</summary>
    public (uint Seat, uint Tile, uint Flags) RawLastDiscard { get; init; }

    public int RoundWind { get; init; } = Tiles.East;
    public int SeatWind { get; init; } = Tiles.East;
    public int Dealer { get; init; }
    public int Honba { get; init; }
    public int RiichiSticks { get; init; }
    public int WallTilesRemaining { get; init; }
    public IReadOnlyList<int> DoraIndicators { get; init; } = [];

    /// <summary>Indexed by seat.</summary>
    public bool[] Riichi { get; init; } = new bool[4];

    /// <summary>Indexed by seat.</summary>
    public int[] Points { get; init; } = new int[4];

    /// <summary>Indexed by seat.</summary>
    public List<OpenMeld>[] Melds { get; init; } = [[], [], [], []];

    /// <summary>Indexed by seat, in discard order.</summary>
    public List<DiscardEntry>[] Discards { get; init; } = [[], [], [], []];

    public IReadOnlyList<OpenMeld> MyMelds => Melds[LocalSeat];
    public int OpenMeldCount => MyMelds.Count;

    /// <summary>True when the player holds 3n+2 tiles and must discard.</summary>
    public bool IsDiscardTurn => (HandTileCount + (3 * OpenMeldCount)) % 3 == 2;

    public int TotalDiscards
    {
        get
        {
            var n = 0;
            foreach (var d in Discards) n += d.Count;
            return n;
        }
    }

    /// <summary>Counts of every tile kind visible outside the player's hand (discards and melds), capped so totals never exceed 4.</summary>
    public int[] VisibleOutsideHand(IEnumerable<int> extraIndicators)
    {
        var visible = new int[Tiles.KindCount];
        foreach (var pile in Discards)
            foreach (var d in pile)
                visible[d.Kind]++;
        foreach (var seatMelds in Melds)
            foreach (var m in seatMelds)
                foreach (var k in m.Kinds)
                    visible[k]++;
        foreach (var k in extraIndicators)
            if (Tiles.IsValid(k)) visible[k]++;
        // A called discard can appear both in the pile and in the meld; never claim more than exist.
        for (var k = 0; k < Tiles.KindCount; k++)
            visible[k] = Math.Clamp(visible[k], 0, 4 - Hand[k]);
        return visible;
    }

    /// <summary>The player's current placement, 1 to 4. Tied players share the better placement.</summary>
    public int Placement => 1 + Enumerable.Range(0, 4).Count(seat => seat != LocalSeat && Points[seat] > Points[LocalSeat]);

    public int AllMeldTiles
    {
        get
        {
            var n = 0;
            foreach (var seatMelds in Melds)
                foreach (var m in seatMelds) n += m.Kinds.Count;
            return n;
        }
    }

    public int[] DiscardCounts(int seat)
    {
        var counts = new int[Tiles.KindCount];
        foreach (var d in Discards[seat]) counts[d.Kind]++;
        return counts;
    }

    /// <summary>Changes whenever anything the analysis depends on changes.</summary>
    public long Fingerprint()
    {
        var hash = new HashCode();
        hash.Add(IsActive);
        hash.Add(LocalSeat);
        foreach (var c in Hand) hash.Add(c);
        hash.Add(DrawnTile);
        hash.Add(LastDiscardSeat);
        hash.Add(LastDiscardKind);
        foreach (var pile in Discards)
        {
            hash.Add(pile.Count);
            foreach (var d in pile) { hash.Add(d.Kind); hash.Add(d.Flags); }
        }
        foreach (var seatMelds in Melds)
        {
            hash.Add(seatMelds.Count);
            foreach (var m in seatMelds)
                foreach (var k in m.Kinds) hash.Add(k);
        }
        return hash.ToHashCode();
    }
}
