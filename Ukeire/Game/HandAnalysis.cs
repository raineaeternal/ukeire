using System.Collections.Generic;
using System.Linq;
using Ukeire.Mahjong;

namespace Ukeire.Game;

public sealed record ThreatReading(int Seat, IReadOnlyList<SafetyReading> Tiles);

/// <summary>Everything computed for one snapshot. Built on a worker thread, read by the UI.</summary>
public sealed class HandAnalysis
{
    public required TableSnapshot Snapshot { get; init; }
    public required TableContext Context { get; init; }
    public required int[] Visible { get; init; }
    public required ShantenResult Shanten { get; init; }

    /// <summary>Ranked best-first. Only set on a discard turn.</summary>
    public List<DiscardOption>? Discards { get; init; }

    /// <summary>Tiles that improve a waiting (3n+1) hand, or its waits when tenpai.</summary>
    public IReadOnlyList<Acceptance>? Acceptances { get; init; }

    public required List<YakuPath> YakuPaths { get; init; }
    public required bool OpenWithoutYaku { get; init; }
    public required List<ThreatReading> Threats { get; init; }

    /// <summary>Waits that are already in the player's own discard pile. Empty unless tenpai off-turn.</summary>
    public IReadOnlyList<int> FuritenWaits { get; init; } = [];

    public static HandAnalysis Build(TableSnapshot snap, TableContext ctx)
    {
        var visible = snap.VisibleOutsideHand(ctx.DoraIndicators);
        var melds = snap.OpenMeldCount;
        List<DiscardOption>? discards = null;
        IReadOnlyList<Acceptance>? accepts = null;
        ShantenResult shanten;

        if (snap.IsDiscardTurn)
        {
            shanten = Mahjong.Shanten.Calculate(snap.Hand, melds);
            discards = Efficiency.RankDiscards(snap.Hand, melds, visible);
        }
        else
        {
            accepts = Efficiency.Acceptances(snap.Hand, melds, visible, out shanten);
        }

        var yaku = YakuAdvisor.Suggest(snap.Hand, snap.MyMelds, ctx.RoundWind, ctx.SeatWind);

        var seen = new int[Tiles.KindCount];
        for (var k = 0; k < Tiles.KindCount; k++) seen[k] = visible[k] + snap.Hand[k];
        var threats = new List<ThreatReading>();
        for (var seat = 0; seat < 4; seat++)
        {
            if (seat == snap.LocalSeat || !ctx.IsThreat(seat)) continue;
            threats.Add(new ThreatReading(seat, Safety.ReadHand(snap.Hand, snap.DiscardCounts(seat), seen)));
        }

        var furiten = new List<int>();
        if (shanten.IsTenpai && accepts != null)
        {
            var own = snap.DiscardCounts(snap.LocalSeat);
            furiten.AddRange(accepts.Where(w => own[w.Kind] > 0).Select(w => w.Kind));
        }

        return new HandAnalysis
        {
            Snapshot = snap,
            Context = ctx,
            Visible = visible,
            Shanten = shanten,
            Discards = discards,
            Acceptances = accepts,
            YakuPaths = yaku,
            OpenWithoutYaku = YakuAdvisor.IsOpenWithoutYaku(snap.MyMelds, yaku),
            Threats = threats,
            FuritenWaits = furiten,
        };
    }

    /// <summary>The worst safety level of a tile across all threatening opponents.</summary>
    public SafetyLevel? DangerOf(int kind)
    {
        if (Threats.Count == 0) return null;
        return Threats.Max(t => t.Tiles.FirstOrDefault(r => r.Kind == kind)?.Level ?? SafetyLevel.Dangerous);
    }
}

/// <summary>Round facts read from Board.RoundState: winds, dora indicators and who is in riichi.</summary>
public sealed record TableContext(int RoundWind, int SeatWind, IReadOnlyList<int> DoraIndicators, IReadOnlyList<bool> RiichiSeats)
{
    public bool IsThreat(int seat) => seat < RiichiSeats.Count && RiichiSeats[seat];
}
