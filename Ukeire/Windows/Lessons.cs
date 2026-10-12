using System;
using System.Collections.Generic;
using System.Linq;
using Ukeire.Game;
using Ukeire.Mahjong;

namespace Ukeire.Windows;

public sealed record Lesson(string Id, string Title, string Body);

/// <summary>A lesson that applies to the current table, with a line tying it to the player's hand.</summary>
public sealed record Spotlight(Lesson Lesson, string Why);

/// <summary>Short reference lessons shown on the Learn tab. Terms used in hints and reviews are explained here.</summary>
public static class Lessons
{
    public static readonly Lesson[] All =
    [
        new("goal", "The goal: 4 groups + 1 pair",
         "A winning hand is 14 tiles: four groups (a run of three in one suit like 3-4-5, or three of a kind) plus a pair. " +
         "Seven Pairs and Thirteen Orphans are the only exceptions. Every decision is about getting to that shape quickly and safely."),
        new("shanten", "Shanten",
         "Shanten counts how many more useful tiles you need before you're one tile from winning. " +
         "2-shanten means two good draws to reach tenpai. Lowering shanten is the main measure of progress."),
        new("tenpai", "Tenpai",
         "Tenpai means you're one tile from a complete hand. The tiles that would finish it are your waits. " +
         "With a closed hand at tenpai you can declare riichi."),
        new("ukeire", "Ukeire (tile acceptance)",
         "Ukeire is the number of unseen tiles that would lower your shanten. Among discards that keep the same shanten, " +
         "prefer the one with the higher ukeire: you'll improve sooner on average. Ukeire counts subtract every copy you can see " +
         "in your hand, discard piles and called melds."),
        new("shapes", "Shapes",
         "Ryanmen (two-sided, e.g. 34 waiting on 2 or 5) accepts 8 tiles: the best partial shape. " +
         "Kanchan (gap, e.g. 35 waiting on 4) and penchan (edge, 12 waiting on 3) accept only 4. " +
         "A pair accepts 2 as a triplet candidate. Isolated honors and terminals connect to the fewest tiles, so they usually go first."),
        new("yaku", "You need a yaku",
         "In Riichi Mahjong you can't win without at least one yaku. A closed hand always has riichi available. " +
         "Once you call chi or pon, you need an open-compatible yaku such as a dragon or value-wind triplet, All Simples or a flush. " +
         "Calling without a plan can leave you with a hand that can never win."),
        new("calling", "Calling chi, pon and kan",
         "Calling a discard speeds your hand up but opens it. You lose riichi, and an open hand needs a yaku that still works open: " +
         "a dragon or value-wind triplet, All Simples, a flush or All Triplets. Flushes are worth one han less when open. " +
         "A good call brings you closer to tenpai and keeps a yaku you already have, like pon on a dragon pair. " +
         "A weak call doesn't change your shanten, leaves you hoping for a yaku you don't have yet, or comes while someone is in riichi " +
         "and you're far from tenpai: every call leaves fewer tiles in your hand to defend with. " +
         "A kan doesn't change your shape. It gives you a replacement draw and reveals a new dora indicator for everyone. A closed kan keeps your hand closed."),
        new("yakuman", "Yakuman",
         "A yakuman is a limit hand worth 32,000 points (48,000 as dealer), whatever else is in it. " +
         "The ones to know: Big Three Dragons (sets of all three dragons), Four Concealed Triplets, Thirteen Orphans (one of each terminal and honor plus a pair), " +
         "All Honors, Little and Big Four Winds, All Terminals, All Green, Nine Gates (1112345678999 in one suit plus one more) and Four Quads. " +
         "They're rare: most need a lot of specific tiles, and opponents get wary as soon as one looks possible. " +
         "Four Quads and True Nine Gates (1112345678999 waiting on any of the nine tiles) are the rarest of all, and many players never see either. " +
         "Go for a yakuman when your hand is already most of the way there, not by breaking up a good hand to chase one."),
        new("riichi", "Riichi",
         "Declaring riichi on a closed tenpai hand adds a han and locks your hand: after that you must discard every tile you draw unless it wins. " +
         "It also tells everyone you're tenpai, so they'll defend."),
        new("furiten", "Furiten",
         "If one of your waits is in your own discard pile you're furiten: you can't win on someone else's discard, only by drawing it yourself. " +
         "That's why a riichi player's own discards are 100% safe against them."),
        new("defense", "Genbutsu, suji and kabe (defense)",
         "Genbutsu: a tile the riichi player discarded themselves is completely safe against them. " +
         "Suji: if they discarded a 4, a two-sided wait on 1 or 7 is impossible (it would also win on the 4), so 1 and 7 are safer. " +
         "Middle tiles 4-6 need both sides (1 and 7 for a 4) to be suji. " +
         "Kabe (wall): if all four 7s are visible, nobody can be waiting two-sided on 8 or 9 using 7s. " +
         "Suji and kabe don't protect against gap, edge, pair or single-tile waits."),
        new("pushfold", "Push or fold",
         "When someone declares riichi, ask: am I tenpai or 1-shanten with a good hand? If yes, pushing can be right. " +
         "If you're 2+ shanten or the hand is cheap, fold. Discard your safest tiles and give up on winning this hand. " +
         "Not dealing in is worth more than a slow hand."),
        new("dora", "Dora, aka dora and ura dora",
         "There are three kinds of dora, and every copy is worth 1 han. " +
         "Dora: the indicator shows the tile before the dora. An indicator of 3m makes 4m dora, 9 wraps to 1, " +
         "winds go E-S-W-N-E and dragons go White-Green-Red-White. A kan reveals another indicator. " +
         "Aka dora: the red fives are always dora. A red five that is also the dora counts twice. " +
         "Ura dora: when a riichi player wins, the tile under each indicator is flipped and gives more dora the same way. " +
         "Nobody sees them before the win, which is another reason to declare riichi. " +
         "Dora add han but aren't yaku: a hand full of dora still needs a yaku to win."),
    ];

    public static Lesson Get(string id) => All.First(l => l.Id == id);

    /// <summary>How long after a discard its review still counts as "just happened".</summary>
    private static readonly TimeSpan ReviewWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Picks the lessons that apply to the table right now, most urgent first. The reasons only restate what the
    /// Table and Review tabs already show, so this never names the best discard before a hint is opened.
    /// </summary>
    /// <param name="a">The analysis, only if it's current; null otherwise.</param>
    public static List<Spotlight> Relevant(TableSnapshot snap, HandAnalysis? a, ReviewEntry? last)
    {
        var list = new List<Spotlight>();
        if (!snap.IsActive) return list;
        void Add(string id, string why) => list.Add(new Spotlight(Get(id), why));

        var current = a;
        var inRiichi = snap.Riichi[snap.LocalSeat];
        var closed = snap.MyMelds.All(m => m.IsConcealedKan);

        if (current is { FuritenWaits.Count: > 0 } f)
            Add("furiten", $"You're furiten: {Tiles.Join(f.FuritenWaits)} is in your own discards, so you can only win on those by drawing them yourself.");

        if (current?.YakuPaths.Where(y => y.Yakuman).MinBy(y => y.Outlook) is { } yakuman)
            Add("yakuman", $"Your hand could be heading for {yakuman.Name} ({yakuman.Outlook}). It's a long shot: keep an eye on it, but don't force it.");

        if (current is { OpenWithoutYaku: true })
            Add("yaku", "Your hand is open and no yaku is in reach. As it stands it can't win.");
        else if (!closed && current?.YakuPaths.FirstOrDefault(y => y.Outlook == YakuOutlook.Secured) is { } secured)
            Add("yaku", $"You've called a meld, so you need an open yaku. {secured.Name} covers it.");

        if (current is { Threats.Count: > 0 } t)
        {
            var who = string.Join(" and ", t.Threats.Select(x => RelativeSeat(snap, x.Seat)));
            var where = t.Shanten.IsTenpai ? "you're tenpai" : $"you're {t.Shanten.Value}-shanten";
            Add("pushfold", $"{who} declared riichi and {where}. Decide whether this hand is worth the risk.");
            Add("defense", $"{who}'s own discards are 100% safe against them. Their pile is on the Defense tab.");
        }

        switch (inRiichi, snap.IsDiscardTurn)
        {
            case (true, _):
                Add("riichi", "You're in riichi: every tile you draw that doesn't win must be discarded.");
                break;
            case (false, true) when snap.GameSaysCanRiichi:
                Add("riichi", "The game is offering riichi this turn. Think about whether locking your hand is worth the extra han.");
                break;
            case (false, false) when closed && current is { Shanten.IsTenpai: true }:
                Add("riichi", "Your hand is closed and tenpai, so riichi is an option from your next discard.");
                break;
        }

        switch (current?.Shanten, snap.IsDiscardTurn, current?.Acceptances)
        {
            case ({ IsComplete: true }, _, _):
                Add("goal", "Your hand is complete: four groups and a pair (or a special hand).");
                break;
            case ({ IsTenpai: true }, false, { } waits):
                Add("tenpai", $"You're tenpai, waiting on {Tiles.Join(waits.Select(w => w.Kind))} ({waits.Sum(w => w.Remaining)} unseen).");
                break;
            case ({ Value: >= 1 and var n }, false, _):
                Add("shanten", $"You're {n}-shanten: {n} more useful draw{(n == 1 ? "" : "s")} before tenpai.");
                break;
        }

        if (last != null && DateTime.Now - last.Time < ReviewWindow)
        {
            var tile = Tiles.Short(last.Tile);
            switch (last.Kind, last.Verdict)
            {
                case (ReviewKind.Call, Verdict.NoYaku):
                    Add("calling", $"Your last call (on {tile}) left your open hand with no yaku.");
                    break;
                case (ReviewKind.Call, Verdict.Pointless or Verdict.Risky or Verdict.Backward):
                    Add("calling", $"Your last call (on {tile}) was rated {last.Verdict}. See what makes a call worth it.");
                    break;
                case (ReviewKind.Call, _):
                    break;
                case (_, Verdict.Backward):
                    Add("shapes", $"Your last discard ({tile}) set you back a step. It was probably part of a group or a useful shape.");
                    break;
                case (_, Verdict.Slow or Verdict.Good):
                    Add("ukeire", $"Your last discard ({tile}) kept your shanten but accepted fewer tiles than the best choice.");
                    break;
                case (_, Verdict.Defensive):
                    if (list.All(x => x.Lesson.Id != "pushfold"))
                        Add("pushfold", $"Your last discard ({tile}) traded speed for safety.");
                    break;
            }
        }

        var dora = snap.DoraIndicators.Where(Tiles.IsValid).Select(Tiles.DoraFromIndicator).ToList();
        if (dora.Count > 0)
        {
            var regular = dora.Sum(k => snap.Hand[k] + snap.MyMelds.Sum(m => m.Kinds.Count(x => x == k)));
            var aka = snap.RedFives.Sum();
            var held = aka > 0 ? $"{regular} dora and {aka} aka dora" : $"{regular} dora";
            Add("dora", $"Dora this hand: {Tiles.Join(dora.Distinct())}. You hold {held}, worth {regular + aka} han if you win with a yaku.");
        }

        return list;
    }

    private static string RelativeSeat(TableSnapshot snap, int seat) =>
        ((seat - snap.LocalSeat + 4) & 3) switch { 1 => "Right", 2 => "Across", 3 => "Left", _ => "You" };
}
