using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Emj;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Ukeire.Mahjong;

namespace Ukeire.Game;

/// <summary>
/// Reads the Doman Mahjong table through FFXIVClientStructs:
/// AgentEmj -> EmjGame -> EmjEngine -> EmjEngineCore -> ClientTable.
///
/// Fair-play rule: only the local player's <see cref="Player"/> is read for hand contents.
/// Other players' tile counts and draws, the AI's chosen action, the engine's danger evaluation,
/// its unseen-tile counts and the ura dora are deliberately ignored.
/// </summary>
public static unsafe class EmjReader
{
    private const uint EmptyDiscard = 127;
    private const uint UnusedMeldTile = 255;
    private const uint RedFiveFlag = 0x100;

    public static TableSnapshot Read()
    {
        var agent = AgentEmj.Instance();
        if (agent == null || agent->Game == null) return TableSnapshot.Inactive;
        var engine = agent->Game->Engine;
        if (engine == null || engine->Core == null) return TableSnapshot.Inactive;
        var table = engine->Core->Table;
        if (table == null) return Waiting("Waiting for the table to be set up.");

        var localSeat = (int)table->LocalSeat;
        if (localSeat is < 0 or > 3) return Waiting("Waiting for seats to be assigned.");

        // LocalSeat indexes Players; the board, discard piles and DrawnTile are numbered by Player.Seat.
        Player me = table->Players[localSeat];
        var mySeat = (int)me.Seat;
        if (mySeat is < 0 or > 3) return Waiting("Waiting for seats to be assigned.");

        var hand = new int[Tiles.KindCount];
        var red = new int[Tiles.KindCount];
        var handTiles = me.HandTiles;
        var handTileCount = Math.Clamp(me.HandTileCount, 0, handTiles.Length);
        for (var i = 0; i < handTileCount; i++)
        {
            var t = handTiles[i];
            var kind = (int)(t & 0xFF);
            if (!Tiles.IsValid(kind) || hand[kind] >= 4) continue;
            hand[kind]++;
            if ((t & RedFiveFlag) != 0) red[kind]++;
        }

        // Table.DrawnTile belongs to whichever seat's turn it is; only use it when that's us.
        var drawnTile = -1;
        var drawnRed = false;
        ref var drawnInfo = ref table->DrawnTile;
        if (drawnInfo.Seat == (uint)mySeat)
        {
            var kind = (int)(drawnInfo.Tile & 0xFF);
            if (Tiles.IsValid(kind) && hand[kind] < 4)
            {
                drawnTile = kind;
                drawnRed = (drawnInfo.Tile & RedFiveFlag) != 0;
            }
        }
        if (drawnTile >= 0)
        {
            hand[drawnTile]++;
            if (drawnRed) red[drawnTile]++;
        }

        var rawHandTiles = new uint[handTiles.Length];
        for (var i = 0; i < handTiles.Length; i++) rawHandTiles[i] = handTiles[i];

        LogDrawChanges(mySeat, me.HandTileCount, rawHandTiles, drawnInfo.Seat, drawnInfo.Tile, drawnInfo.Flags);

        var melds = new List<OpenMeld>[4];
        var discards = new List<DiscardEntry>[4];
        var board = table->Board;
        for (var seat = 0; seat < 4; seat++)
        {
            melds[seat] = [];
            discards[seat] = [];
            if (board == null) continue;

            ref var meldList = ref board->MeldLists[seat];
            var meldCount = Math.Clamp(meldList.Count, 0, meldList.Melds.Length);
            for (var m = 0; m < meldCount; m++)
            {
                ref var meld = ref meldList.Melds[m];
                var kinds = new List<int>(4);
                foreach (var t in meld.Tiles)
                {
                    if (t == UnusedMeldTile) continue;
                    var kind = (int)(t & 0xFF);
                    if (Tiles.IsValid(kind)) kinds.Add(kind);
                }
                if (kinds.Count == 0) continue;
                kinds.Sort();
                melds[seat].Add(new OpenMeld(kinds, meld.Type.HasFlag(MeldType.ConcealedQuad)));
            }

            ref var pile = ref board->DiscardPiles[seat];
            var pileCount = Math.Clamp(pile.Count, 0, pile.Tiles.Length);
            for (var d = 0; d < pileCount; d++)
            {
                var t = pile.Tiles[d];
                if (t == EmptyDiscard) continue;
                var kind = (int)(t & 0xFF);
                if (Tiles.IsValid(kind)) discards[seat].Add(new DiscardEntry(kind, pile.Flags[d]));
            }
        }

        // Round state: everything here is public at the table. Ura dora are hidden until a win, so they're never read.
        var roundWind = Tiles.East;
        var seatWind = Tiles.East + mySeat;
        int dealer = 0, honba = 0, riichiSticks = 0, wallRemaining = 0;
        var doraIndicators = new List<int>(5);
        var riichi = new bool[4];
        var points = new int[4];
        if (board != null)
        {
            ref var round = ref board->RoundState;
            roundWind = Tiles.East + Math.Clamp(round.RoundWind, 0, 3);
            dealer = round.Dealer;
            honba = round.Honba;
            riichiSticks = round.RiichiSticks;
            wallRemaining = round.WallTilesRemaining;
            var doraCount = Math.Clamp(round.DoraIndicatorCount, 0, round.DoraIndicators.Length);
            for (var i = 0; i < doraCount; i++)
            {
                var kind = (int)(round.DoraIndicators[i] & 0xFF);
                if (Tiles.IsValid(kind)) doraIndicators.Add(kind);
            }
            for (var seat = 0; seat < 4; seat++)
            {
                riichi[seat] = round.IsRiichi[seat] != 0;
                // Points are stored by fixed table position rather than by seat.
                points[seat] = round.Points[(seat + round.Dealer + round.StartSeatOffset) & 3] * 100;
            }
        }

        return new TableSnapshot
        {
            Status = "At the table.",
            LocalSeat = mySeat,
            Hand = hand,
            RedFives = red,
            HandTileCount = handTileCount + (drawnTile >= 0 ? 1 : 0),
            DrawnTile = drawnTile,
            DrawnTileIsRed = drawnRed,
            RawHandTiles = rawHandTiles,
            RawHandTileCount = me.HandTileCount,
            RawDrawnTile = (drawnInfo.Seat, drawnInfo.Tile, drawnInfo.Flags),
            GameSaysCanRiichi = me.CanRiichi,
            RoundWind = roundWind,
            SeatWind = seatWind,
            Dealer = dealer,
            Honba = honba,
            RiichiSticks = riichiSticks,
            WallTilesRemaining = wallRemaining,
            DoraIndicators = doraIndicators,
            Riichi = riichi,
            Points = points,
            Melds = melds,
            Discards = discards,
        };
    }

    private static string? lastDrawState;
    private static readonly Queue<string> drawHistory = new();

    /// <summary>Recent changes to the draw-related fields, newest last, for the debug tab.</summary>
    public static string[] DrawHistory
    {
        get { lock (drawHistory) return drawHistory.ToArray(); }
    }

    /// <summary>Temporary: logs every change to the draw-related fields so short-lived values can be read back from /xllog.</summary>
    private static void LogDrawChanges(int mySeat, int handTileCount, uint[] handTiles, uint drawnSeat, uint drawnTile, uint drawnFlags)
    {
        var state = $"MySeat={mySeat} HandTileCount={handTileCount} HandTiles=[{string.Join(" ", Array.ConvertAll(handTiles, t => t.ToString("X")))}] DrawnTile Seat={drawnSeat} Tile={drawnTile:X} Flags={drawnFlags:X}";
        if (state == lastDrawState) return;
        lastDrawState = state;
        lock (drawHistory)
        {
            drawHistory.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {state}");
            while (drawHistory.Count > 40) drawHistory.Dequeue();
        }
        Plugin.Log.Information($"[draw] {state}");
    }

    private static TableSnapshot Waiting(string status) => new() { IsActive = false, Status = status };
}
