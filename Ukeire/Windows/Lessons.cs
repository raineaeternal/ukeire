namespace Ukeire.Windows;

/// <summary>Short reference lessons shown on the Learn tab. Terms used in hints and reviews are explained here.</summary>
public static class Lessons
{
    public static readonly (string Title, string Body)[] All =
    [
        ("The goal: 4 groups + 1 pair",
         "A winning hand is 14 tiles: four groups (a run of three in one suit like 3-4-5, or three of a kind) plus a pair. " +
         "Seven Pairs and Thirteen Orphans are the only exceptions. Every decision is about getting to that shape quickly and safely."),
        ("Shanten",
         "Shanten counts how many more useful tiles you need before you're one tile from winning. " +
         "2-shanten means two good draws to reach tenpai. Lowering shanten is the main measure of progress."),
        ("Tenpai",
         "Tenpai means you're one tile from a complete hand. The tiles that would finish it are your waits. " +
         "With a closed hand at tenpai you can declare riichi."),
        ("Ukeire (tile acceptance)",
         "Ukeire is the number of unseen tiles that would lower your shanten. Among discards that keep the same shanten, " +
         "prefer the one with the higher ukeire: you'll improve sooner on average. Ukeire counts subtract every copy you can see " +
         "in your hand, discard piles and called melds."),
        ("Shapes",
         "Ryanmen (two-sided, e.g. 34 waiting on 2 or 5) accepts 8 tiles: the best partial shape. " +
         "Kanchan (gap, e.g. 35 waiting on 4) and penchan (edge, 12 waiting on 3) accept only 4. " +
         "A pair accepts 2 as a triplet candidate. Isolated honors and terminals connect to the fewest tiles, so they usually go first."),
        ("You need a yaku",
         "In Riichi Mahjong you can't win without at least one yaku. A closed hand always has riichi available. " +
         "Once you call chi or pon, you need an open-compatible yaku such as a dragon or value-wind triplet, All Simples or a flush. " +
         "Calling without a plan can leave you with a hand that can never win."),
        ("Riichi",
         "Declaring riichi on a closed tenpai hand adds a han and locks your hand: after that you must discard every tile you draw unless it wins. " +
         "It also tells everyone you're tenpai, so they'll defend."),
        ("Furiten",
         "If one of your waits is in your own discard pile you're furiten: you can't win on someone else's discard, only by drawing it yourself. " +
         "That's why a riichi player's own discards are 100% safe against them."),
        ("Genbutsu, suji and kabe (defense)",
         "Genbutsu: a tile the riichi player discarded themselves is completely safe against them. " +
         "Suji: if they discarded a 4, a two-sided wait on 1 or 7 is impossible (it would also win on the 4), so 1 and 7 are safer. " +
         "Middle tiles 4-6 need both sides (1 and 7 for a 4) to be suji. " +
         "Kabe (wall): if all four 7s are visible, nobody can be waiting two-sided on 8 or 9 using 7s. " +
         "Suji and kabe don't protect against gap, edge, pair or single-tile waits."),
        ("Push or fold",
         "When someone declares riichi, ask: am I tenpai or 1-shanten with a good hand? If yes, pushing can be right. " +
         "If you're 2+ shanten or the hand is cheap, fold. Discard your safest tiles and give up on winning this hand. " +
         "Not dealing in is worth more than a slow hand."),
        ("Dora",
         "The dora indicator shows the tile before the dora: an indicator of 3m makes 4m dora, 9 wraps to 1, " +
         "winds go E-S-W-N-E and dragons go White-Green-Red-White. Each dora adds a han but isn't a yaku by itself. Red fives are always dora."),
    ];
}
