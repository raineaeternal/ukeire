using System;
using System.Collections.Generic;
using System.Linq;
using Ukeire.Mahjong;

namespace Ukeire.Game;

public enum CallType
{
    Chi,
    Pon,
    OpenKan,
    ClosedKan,
    AddedKan,
}

public enum CallRisk
{
    Low,
    Medium,
    High,
}

/// <param name="Tile">The tile being called, or the kan's tile for a closed or added kan.</param>
/// <param name="HandAfter">The concealed hand once the meld is set aside, before any discard.</param>
/// <param name="Forbidden">Tiles the swap-call rule (kuikae) bans as the next discard.</param>
public sealed record CallOption(CallType Type, int Tile, OpenMeld Meld, int[] HandAfter, IReadOnlyList<int> Forbidden)
{
    public bool IsKan => Type is CallType.OpenKan or CallType.ClosedKan or CallType.AddedKan;

    /// <summary>True for calls made on another player's discard, which are only possible until the next draw.</summary>
    public bool OnDiscard => Type is CallType.Chi or CallType.Pon or CallType.OpenKan;

    public string Name => Type switch
    {
        CallType.OpenKan => "Open kan",
        CallType.ClosedKan => "Closed kan",
        CallType.AddedKan => "Added kan",
        _ => Type.ToString(),
    };
}

public sealed record CallAdvice(CallOption Option, CallRisk Risk, string Summary, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons);

/// <summary>
/// What the yaku picture looks like once a call is made.
/// <paramref name="Tenpai"/> holds the exact reading of the best tenpai the call can reach, or null when it can't reach one yet.
/// </summary>
public sealed record YakuAfterCall(bool NoYaku, YakuPath? Plan, YakuPath? NewlySecured, IReadOnlyList<WaitReading>? Tenpai)
{
    public static YakuAfterCall Of(TableSnapshot before, ReadOnlySpan<int> handAfter, IReadOnlyList<OpenMeld> meldsAfter, TableContext ctx, int[] visible, IReadOnlyList<int> forbidden)
    {
        var paths = YakuAdvisor.Suggest(handAfter, meldsAfter, ctx.RoundWind, ctx.SeatWind);
        var securedBefore = YakuAdvisor.Suggest(before.Hand, before.MyMelds, ctx.RoundWind, ctx.SeatWind)
            .Where(p => p.Outlook == YakuOutlook.Secured).Select(p => p.Name).ToHashSet();
        var noYaku = YakuAdvisor.IsOpenWithoutYaku(meldsAfter, paths);
        var tenpai = BestTenpai(handAfter.ToArray(), meldsAfter, ctx, visible, forbidden);
        // The exact reading beats the shape-based guess whenever the call can reach tenpai.
        if (tenpai != null && !meldsAfter.All(m => m.IsConcealedKan)) noYaku = !tenpai.Any(w => w.CanWin);
        return new YakuAfterCall(
            noYaku,
            paths.Where(YakuAdvisor.WorksOpen).MinBy(p => p.Outlook),
            paths.FirstOrDefault(p => p.Outlook == YakuOutlook.Secured && !securedBefore.Contains(p.Name)),
            tenpai);
    }

    /// <summary>The waits of the most valuable tenpai the hand can reach (after an allowed discard if it must discard), or null.</summary>
    private static List<WaitReading>? BestTenpai(int[] hand, IReadOnlyList<OpenMeld> melds, TableContext ctx, int[] visible, IReadOnlyList<int> forbidden)
    {
        List<WaitReading> Read(int[] h) => WinEvaluator.Waits(h, melds, ctx.RoundWind, ctx.SeatWind, visible);
        if ((Tiles.Total(hand) + (3 * melds.Count)) % 3 != 2)
            return Shanten.Calculate(hand, melds.Count).IsTenpai ? Read(hand) : null;

        List<WaitReading>? best = null;
        var bestScore = (0, 0);
        for (var d = 0; d < Tiles.KindCount; d++)
        {
            if (hand[d] == 0 || forbidden.Contains(d)) continue;
            hand[d]--;
            if (Shanten.Calculate(hand, melds.Count).IsTenpai)
            {
                var waits = Read(hand);
                var winning = waits.Where(w => w.CanWin).ToList();
                var score = (winning.Sum(w => w.Remaining), winning.Count == 0 ? 0 : winning.Max(w => Math.Max(w.Ron.Han, w.Tsumo.Han)));
                if (best == null || score.CompareTo(bestScore) > 0)
                {
                    best = waits;
                    bestScore = score;
                }
            }
            hand[d]++;
        }
        return best;
    }
}

/// <summary>
/// Finds the chi, pon and kan the player could make right now and weighs each one. Only public information is used:
/// the player's own hand and melds, the last discard and the visible tiles.
/// </summary>
public static class CallAdvisor
{
    public static List<CallOption> Options(TableSnapshot snap)
    {
        var options = new List<CallOption>();
        if (snap.Riichi[snap.LocalSeat] || snap.WallTilesRemaining <= 0) return options;
        var hand = snap.Hand;

        if (snap.IsDiscardTurn)
        {
            for (var k = 0; k < Tiles.KindCount; k++)
                if (hand[k] == 4)
                    options.Add(Make(CallType.ClosedKan, k, [k, k, k, k], hand, concealed: true, removed: [k, k, k, k]));
            foreach (var pon in snap.MyMelds.Where(m => m.Kinds.Count == 3 && !m.IsSequence && hand[m.Kind] > 0))
                options.Add(Make(CallType.AddedKan, pon.Kind, [pon.Kind, pon.Kind, pon.Kind, pon.Kind], hand, concealed: false, removed: [pon.Kind]));
            return options;
        }

        var tile = snap.LastDiscardKind;
        var from = snap.LastDiscardSeat;
        if (tile < 0 || from < 0 || from == snap.LocalSeat) return options;

        if (hand[tile] >= 2)
            options.Add(Make(CallType.Pon, tile, [tile, tile, tile], hand, concealed: false, removed: [tile, tile], forbidden: [tile]));
        if (hand[tile] >= 3)
            options.Add(Make(CallType.OpenKan, tile, [tile, tile, tile, tile], hand, concealed: false, removed: [tile, tile, tile]));

        // Chi only works on the discard of the player to your left, who plays just before you.
        if (from == ((snap.LocalSeat + 3) & 3) && !Tiles.IsHonor(tile))
        {
            var rank = Tiles.Rank(tile);
            // (lowest tile of the run relative to the called tile, tile the swap-call rule also bans)
            (int Low, int SujiBan)[] runs = [(-2, rank >= 4 ? tile - 3 : -1), (-1, -1), (0, rank <= 6 ? tile + 3 : -1)];
            foreach (var (offset, suji) in runs)
            {
                if (rank + offset < 1 || rank + offset + 2 > 9) continue;
                var low = tile + offset;
                int[] run = [low, low + 1, low + 2];
                var held = run.Where(k => k != tile).ToArray();
                if (held.Any(k => hand[k] == 0)) continue;
                options.Add(Make(CallType.Chi, tile, run, hand, concealed: false, removed: held, forbidden: suji >= 0 ? [tile, suji] : [tile]));
            }
        }
        return options;
    }

    private static CallOption Make(CallType type, int tile, int[] meld, int[] hand, bool concealed, int[] removed, int[]? forbidden = null)
    {
        var after = (int[])hand.Clone();
        foreach (var k in removed) after[k]--;
        return new CallOption(type, tile, new OpenMeld(meld, concealed), after, forbidden ?? []);
    }

    /// <summary>Shanten and tile acceptance of a hand. A hand that must discard is measured after its best allowed discard.</summary>
    private static (int Shanten, int Ukeire) Speed(int[] hand, int melds, int[] visible, IReadOnlyList<int> forbidden)
    {
        if ((Tiles.Total(hand) + (3 * melds)) % 3 == 2)
        {
            var best = Efficiency.RankDiscards(hand, melds, visible).FirstOrDefault(o => !forbidden.Contains(o.Discard));
            return best == null ? (Shanten.Calculate(hand, melds).Value, 0) : (best.ShantenAfter.Value, best.UkeireTotal);
        }
        var accepts = Efficiency.Acceptances(hand, melds, visible, out var current);
        return (current.Value, accepts.Sum(a => a.Remaining));
    }

    public static CallAdvice Assess(TableSnapshot snap, CallOption option, TableContext ctx, int[] visible, IReadOnlyList<int> threatSeats)
    {
        var melds = snap.OpenMeldCount;
        var meldsAfter = option.Type == CallType.AddedKan
            ? snap.MyMelds.Select(m => m.Kinds.Count == 3 && !m.IsSequence && m.Kind == option.Tile ? option.Meld : m).ToList()
            : snap.MyMelds.Append(option.Meld).ToList();

        var (shantenBefore, ukeireBefore) = Speed(snap.Hand, melds, visible, []);
        var (shantenAfter, ukeireAfter) = Speed(option.HandAfter, meldsAfter.Count, visible, option.Forbidden);
        var gained = shantenBefore - shantenAfter;
        var yaku = YakuAfterCall.Of(snap, option.HandAfter, meldsAfter, ctx, visible, option.Forbidden);
        var opensHand = !option.Meld.IsConcealedKan && snap.MyMelds.All(m => m.IsConcealedKan);
        var threatened = threatSeats.Count > 0;
        var who = string.Join(" and ", threatSeats.Select(s => RelativeSeat(snap, s)));
        // Tiles left to defend with once the post-call discard is made.
        var defenders = Tiles.Total(option.HandAfter) - (option.OnDiscard && !option.IsKan ? 1 : 0);
        var dora = ctx.DoraIndicators.Where(Tiles.IsValid).Select(Tiles.DoraFromIndicator).ToHashSet();

        var pros = new List<string>();
        var cons = new List<string>();

        if (gained > 0) pros.Add($"Takes you from {ShantenText(shantenBefore)} to {ShantenText(shantenAfter)}.");
        if (gained == 0 && ukeireAfter > ukeireBefore) pros.Add($"Afterwards {ukeireAfter} unseen tiles would improve your hand (now {ukeireBefore}).");
        if (yaku.NewlySecured != null) pros.Add($"Locks in {yaku.NewlySecured.Name} as your yaku.");
        else if (opensHand && yaku.Plan is { Outlook: YakuOutlook.Secured or YakuOutlook.Likely } plan) pros.Add($"{plan.Name} still works with an open hand.");
        if (option.OnDiscard && dora.Contains(option.Tile)) pros.Add($"{Tiles.Short(option.Tile)} is a dora, so taking it adds a han.");
        if (option.IsKan) pros.Add("You get a replacement draw, and the new dora indicator might land on your tiles.");
        if (option.Type == CallType.ClosedKan) pros.Add("Your hand stays closed, so riichi is still available.");

        if (yaku.Tenpai is { } reach && reach.Where(w => w.CanWin).MaxBy(w => Math.Max(w.Ron.Han, w.Tsumo.Han)) is { } top)
        {
            var reading = top.Ron.HasYaku ? top.Ron : top.Tsumo;
            pros.Add($"It can reach tenpai: a win on {Tiles.Short(top.Kind)} would score {string.Join(", ", reading.Yaku.Select(y => $"{y.Name} {y.Han}"))}.");
        }

        if (opensHand)
        {
            var flush = yaku.Plan is { } p && (p.Name.StartsWith("Honitsu") || p.Name.StartsWith("Chinitsu"));
            cons.Add($"Opens your hand: no riichi, menzen tsumo or pinfu{(flush ? $", and {yaku.Plan!.Name} is worth a han less" : "")}.");
        }
        if (yaku.NoYaku) cons.Add("No yaku in reach afterwards, so the hand couldn't win.");
        else if (!option.Meld.IsConcealedKan && yaku.Plan is { Outlook: YakuOutlook.Possible } shaky) cons.Add($"Your only open yaku, {shaky.Name}, isn't secured yet.");
        if (gained < 0) cons.Add($"Breaks up your shape: {ShantenText(shantenBefore)} now, {ShantenText(shantenAfter)} after.");
        else if (gained == 0 && !option.IsKan) cons.Add($"Doesn't bring you closer to tenpai (still {ShantenText(shantenAfter)}).");
        if (threatened && !option.IsKan) cons.Add($"{who} is in riichi, and you'd only have {defenders} tiles left to defend with.");
        if (option.IsKan) cons.Add(threatened ? $"The new dora indicator helps everyone, including {who} in riichi." : "The new dora indicator helps everyone, not just you.");
        var banned = option.Forbidden.Where(k => option.HandAfter[k] > 0).ToList();
        if (banned.Count > 0) cons.Add($"Swap-call rule: you couldn't discard {Tiles.Join(banned)} straight after.");

        var (risk, summary) = (yaku.NoYaku && !option.Meld.IsConcealedKan, gained) switch
        {
            (true, _) => (CallRisk.High, "This call would leave you with no yaku. The hand couldn't win."),
            (_, < 0) => (CallRisk.High, "This call would set your hand back."),
            _ when option.IsKan && threatened => (CallRisk.Medium, "A kan doesn't hurt your shape, but the extra dora also helps the riichi player."),
            _ when option.IsKan => (CallRisk.Low, "A kan doesn't change your shape and gives you an extra draw."),
            (_, 0) when yaku.NewlySecured != null => (CallRisk.Medium, "No faster, but it secures a yaku. Fine if you weren't planning to riichi."),
            (_, 0) when ukeireAfter > ukeireBefore => (CallRisk.Medium, "Same shanten, but more tiles would help you afterwards."),
            (_, 0) => (CallRisk.High, "This call doesn't speed your hand up."),
            _ when threatened && shantenAfter >= 2 => (CallRisk.High, $"Faster, but you'd still be {ShantenText(shantenAfter)} with {who} in riichi."),
            _ when threatened => (CallRisk.Medium, $"Faster, but {who} is in riichi and you'd have fewer tiles to defend with."),
            _ when yaku.Plan is { Outlook: YakuOutlook.Possible } => (CallRisk.Medium, "Faster, but your yaku isn't secured yet."),
            _ => (CallRisk.Low, "Faster, with a yaku in reach."),
        };

        return new CallAdvice(option, risk, summary, pros, cons);
    }

    public static string ShantenText(int shanten) => shanten switch
    {
        < 0 => "a complete hand",
        0 => "tenpai",
        _ => $"{shanten}-shanten",
    };

    private static string RelativeSeat(TableSnapshot snap, int seat) =>
        ((seat - snap.LocalSeat + 4) & 3) switch { 1 => "Right", 2 => "Across", 3 => "Left", _ => "You" };
}
