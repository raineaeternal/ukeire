using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Ukeire.Mahjong;

namespace Ukeire.Game;

// Persisted types. These are read back by later plugin versions, so:
// - only add properties (with defaults), never rename or remove them;
// - store enums as strings so a renamed or new value can't make a whole file unreadable;
// - bump SchemaVersion when the meaning of an existing property changes.

public sealed class ReviewRecord
{
    public DateTime Time { get; set; }
    public string Kind { get; set; } = nameof(ReviewKind.Discard);
    public string Hand { get; set; } = string.Empty;
    public int Tile { get; set; } = -1;
    public List<int> Meld { get; set; } = [];
    public string Verdict { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string HintUsed { get; set; } = nameof(HintTier.None);

    [JsonIgnore]
    public bool IsCall => Kind == nameof(ReviewKind.Call);
    [JsonIgnore]
    public bool IsSound => Verdict is nameof(Game.Verdict.Best) or nameof(Game.Verdict.Good) or nameof(Game.Verdict.Defensive);
    [JsonIgnore]
    public bool IsUnaided => HintUsed == nameof(HintTier.None);

    public static ReviewRecord From(ReviewEntry e) => new()
    {
        Time = e.Time,
        Kind = e.Kind.ToString(),
        Hand = e.Hand,
        Tile = e.Tile,
        Meld = e.Meld.ToList(),
        Verdict = e.Verdict.ToString(),
        Message = e.Message,
        HintUsed = e.HintUsed.ToString(),
    };
}

public sealed class HandRecord
{
    /// <summary>Tile kind of the round wind (27 = East).</summary>
    public int RoundWind { get; set; } = Tiles.East;
    public int Dealer { get; set; }
    public int Honba { get; set; }
    public bool WasDealer { get; set; }
    public int PointsBefore { get; set; }
    public int PointsAfter { get; set; }
    public bool DeclaredRiichi { get; set; }
    public int MeldsCalled { get; set; }
    public List<ReviewRecord> Reviews { get; set; } = [];

    [JsonIgnore]
    public int PointsChange => PointsAfter - PointsBefore;
    [JsonIgnore]
    public string Label => $"{Tiles.Name(RoundWind)} {Dealer + 1}{(Honba > 0 ? $", {Honba} honba" : string.Empty)}";
}

public sealed class GameRecord
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Id { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public DateTime Started { get; set; }

    /// <summary>Null while the game is in progress, or if the game was never finished (plugin unloaded, crash).</summary>
    public DateTime? Ended { get; set; }
    public uint MatchType { get; set; }

    /// <summary>Points at the end, indexed by seat relative to the player (0 = you, 1 = right, 2 = across, 3 = left).</summary>
    public int[] FinalPoints { get; set; } = new int[4];

    /// <summary>1 to 4, or 0 if unknown.</summary>
    public int Placement { get; set; }
    public List<HandRecord> Hands { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<ReviewRecord> Reviews => Hands.SelectMany(h => h.Reviews);
}

/// <summary>
/// Records each game at the table and stores it as its own JSON file under the plugin's config directory,
/// which Dalamud keeps across plugin updates. Files that can't be read are skipped and left alone.
/// </summary>
public sealed class GameHistory
{
    private const int MaxGames = 1000;

    /// <summary>The table can briefly read as inactive between hands; only end a game after this long away.</summary>
    private static readonly TimeSpan LeaveGrace = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly DirectoryInfo directory;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly List<GameRecord> games = [];

    private GameRecord? current;
    private HandRecord? hand;
    private (int RoundWind, int Dealer, int Honba)? handKey;
    private int lastWall;
    private TableSnapshot? lastActive;
    private DateTime? inactiveSince;

    /// <summary>File writes run one after another so two saves of the same game can't interleave.</summary>
    private Task writes = Task.CompletedTask;

    public GameHistory(DirectoryInfo configDirectory, IPluginLog log)
    {
        directory = new DirectoryInfo(Path.Combine(configDirectory.FullName, "history"));
        this.log = log;
        Task.Run(Load);
    }

    /// <summary>Finished and unfinished games, newest first. The game in progress is included.</summary>
    public IReadOnlyList<GameRecord> Games
    {
        get { lock (gate) return games.ToList(); }
    }

    public GameRecord? Current
    {
        get { lock (gate) return current; }
    }

    public string FolderPath => directory.FullName;

    private void Load()
    {
        try
        {
            directory.Create();
            var loaded = new List<GameRecord>();
            foreach (var file in directory.EnumerateFiles("*.json"))
            {
                try
                {
                    var game = JsonSerializer.Deserialize<GameRecord>(File.ReadAllText(file.FullName), Json);
                    if (game == null) continue;
                    if (string.IsNullOrEmpty(game.Id)) game.Id = Path.GetFileNameWithoutExtension(file.Name);
                    loaded.Add(game);
                }
                catch (Exception ex)
                {
                    log.Warning(ex, $"Skipping unreadable game history file {file.Name}");
                }
            }
            lock (gate)
            {
                games.AddRange(loaded.Where(g => games.All(x => x.Id != g.Id)));
                games.Sort((a, b) => b.Started.CompareTo(a.Started));
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to load game history");
        }
    }

    /// <summary>Called every frame with the latest snapshot, on the framework thread.</summary>
    public void Observe(TableSnapshot snap)
    {
        lock (gate)
        {
            if (!snap.IsActive)
            {
                if (current == null) return;
                inactiveSince ??= DateTime.Now;
                if (DateTime.Now - inactiveSince > LeaveGrace) EndGame();
                return;
            }
            inactiveSince = null;

            if (current == null) StartGame(snap);
            var key = (snap.RoundWind, snap.Dealer, snap.Honba);
            // A new hand: the round/dealer/honba changed, or the wall was rebuilt.
            if (hand == null || handKey != key || snap.WallTilesRemaining > lastWall) StartHand(snap, key);

            var me = snap.LocalSeat;
            hand!.PointsAfter = snap.Points[me];
            hand.DeclaredRiichi |= snap.Riichi[me];
            hand.MeldsCalled = Math.Max(hand.MeldsCalled, snap.MyMelds.Count(m => !m.IsConcealedKan));
            lastWall = snap.WallTilesRemaining;
            lastActive = snap;
        }
    }

    /// <summary>Adds a review to the hand in progress. Safe to call from any thread.</summary>
    public void Add(ReviewEntry entry)
    {
        lock (gate) hand?.Reviews.Add(ReviewRecord.From(entry));
    }

    public void Delete(GameRecord game)
    {
        lock (gate)
        {
            if (game == current) return;
            games.Remove(game);
        }
        Task.Run(() =>
        {
            try
            {
                File.Delete(PathFor(game));
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Failed to delete game {game.Id}");
            }
        });
    }

    /// <summary>Saves the game in progress without ending it, e.g. when the plugin unloads.</summary>
    public void Flush()
    {
        Task pending;
        lock (gate)
        {
            if (current != null) Save(current);
            pending = writes;
        }
        pending.Wait(TimeSpan.FromSeconds(2));
    }

    private void StartGame(TableSnapshot snap)
    {
        var started = DateTime.Now;
        current = new GameRecord
        {
            Id = started.ToString("yyyyMMdd-HHmmss"),
            PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Started = started,
            MatchType = EmjReader.MatchType,
        };
        games.Insert(0, current);
        hand = null;
        handKey = null;
        if (games.Count > MaxGames) Prune();
    }

    private void StartHand(TableSnapshot snap, (int, int, int) key)
    {
        // Save at every hand boundary so a crash loses at most the current hand.
        if (hand != null && current != null) Save(current);
        hand = new HandRecord
        {
            RoundWind = snap.RoundWind,
            Dealer = snap.Dealer,
            Honba = snap.Honba,
            WasDealer = snap.SeatWind == Tiles.East,
            PointsBefore = snap.Points[snap.LocalSeat],
            PointsAfter = snap.Points[snap.LocalSeat],
        };
        handKey = key;
        current!.Hands.Add(hand);
    }

    private void EndGame()
    {
        var game = current!;
        game.Ended = DateTime.Now;
        if (lastActive is { } last)
        {
            for (var i = 0; i < 4; i++) game.FinalPoints[i] = last.Points[(last.LocalSeat + i) & 3];
            // Tied players share the better placement.
            game.Placement = 1 + game.FinalPoints.Skip(1).Count(p => p > game.FinalPoints[0]);
        }
        // A game with no reviews and a single empty hand is just someone sitting down and leaving.
        if (game.Hands.Count <= 1 && !game.Reviews.Any()) games.Remove(game);
        else Save(game);
        current = null;
        hand = null;
        handKey = null;
        lastActive = null;
        inactiveSince = null;
    }

    private void Prune()
    {
        foreach (var old in games.Skip(MaxGames).ToList())
        {
            games.Remove(old);
            var path = PathFor(old);
            Task.Run(() =>
            {
                try { File.Delete(path); }
                catch (Exception ex) { log.Warning(ex, $"Failed to prune {path}"); }
            });
        }
    }

    private string PathFor(GameRecord game) => Path.Combine(directory.FullName, $"{game.Id}.json");

    /// <summary>Serializes under the lock, then writes on a worker thread via a temp file so a crash never leaves half a file.</summary>
    private void Save(GameRecord game)
    {
        var text = JsonSerializer.Serialize(game, Json);
        var path = PathFor(game);
        writes = writes.ContinueWith(_ =>
        {
            try
            {
                directory.Create();
                var temp = path + ".tmp";
                File.WriteAllText(temp, text);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Failed to save game {game.Id}");
            }
        }, TaskScheduler.Default);
    }
}
