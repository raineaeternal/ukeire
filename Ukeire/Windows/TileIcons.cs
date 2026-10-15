using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Ukeire.Mahjong;

namespace Ukeire.Windows;

public enum TileDesign
{
    FollowGame,
    Traditional,
    Doman,
    Alternate,
}

/// <summary>
/// Maps tile kinds to the game's own tile icons in ui/icon/076000. Each design is a block of 38 icons:
/// +0 placeholder, +1..+34 the tile kinds in engine order (m, p, s, E S W N, White, Green, Red), +35..+37 red 5m, 5p, 5s.
/// </summary>
public static class TileIcons
{
    private const uint TraditionalBase = 76000;
    private const uint DomanBase = 76040;

    /// <summary>A third block with no name in the game's text. Offered as a manual choice only.</summary>
    private const uint AlternateBase = 76080;

    /// <summary>
    /// Offsets from FFXIVClientStructs' ida data (Client::UI::Misc::EmjModule). The struct isn't in the C# half of the
    /// submodule yet, so it is read by offset: UIModule vtable slot 45 is GetEmjModule, and TileSet is a byte at 72.
    /// </summary>
    private const int GetEmjModuleVtableSlot = 45;
    private const int EmjModuleTileSetOffset = 72;

    private const int EmjModuleSettingsLength = 14;

    /// <summary>The EmjModule settings block (bytes 72 to 85: TileSet through EmjCostumeId), or null if it can't be read. Framework thread only.</summary>
    public static unsafe byte[]? ReadModuleSettings()
    {
        var ui = UIModule.Instance();
        if (ui == null) return null;
        var getEmjModule = (delegate* unmanaged<UIModule*, byte*>)(*(nint**)ui)[GetEmjModuleVtableSlot];
        var module = getEmjModule(ui);
        if (module == null) return null;
        var bytes = new byte[EmjModuleSettingsLength];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = module[EmjModuleTileSetOffset + i];
        return bytes;
    }

    /// <summary>The Mahjong settings' tile set (0 Traditional, 1 Doman, 2 the unnamed third set), or null if it can't be read.</summary>
    public static byte? ReadTileSet() => ReadModuleSettings()?[0];

    /// <summary>
    /// The game copies EmjVoiceNpc and EmjCostumeId from the module into EmjGame when a table starts. If both agree
    /// with what was read from the module, the module (and so its TileSet) is the right object.
    /// </summary>
    public static unsafe string CrossCheck()
    {
        var bytes = ReadModuleSettings();
        var agent = AgentEmj.Instance();
        if (bytes == null) return "module unreadable";
        if (agent == null || agent->Game == null) return "no table to compare with";
        var (voice, costume) = (bytes[12], bytes[13]);
        var game = agent->Game;
        var match = voice == game->EmjVoiceNpc && costume == game->EmjCostumeId;
        return $"module voice {voice} costume {costume} vs table voice {game->EmjVoiceNpc} costume {game->EmjCostumeId}: {(match ? "match" : "MISMATCH")}";
    }

    private static string? lastLoggedSettings;

    /// <summary>Logs the module's settings block whenever it changes, so the byte that follows the game's tile design can be found.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void LogSettingChanges()
    {
        var bytes = ReadModuleSettings();
        var text = bytes == null ? "unreadable" : Convert.ToHexString(bytes);
        if (text == lastLoggedSettings) return;
        lastLoggedSettings = text;
        Plugin.Log.Information($"[tiles] EmjModule bytes 72-85 = {text}; Follow Game draws {Resolve(TileDesign.FollowGame)}; {CrossCheck()}");
    }

    public static TileDesign Resolve(TileDesign chosen) => chosen switch
    {
        TileDesign.FollowGame => ReadTileSet() switch
        {
            0 => TileDesign.Doman,
            1 => TileDesign.Alternate,
            _ => TileDesign.Traditional,
        },
        _ => chosen,
    };

    public static uint IconId(int kind, bool red, TileDesign design)
    {
        var baseId = design switch
        {
            TileDesign.Doman => DomanBase,
            TileDesign.Alternate => AlternateBase,
            _ => TraditionalBase,
        };
        var redFive = red && !Tiles.IsHonor(kind) && Tiles.Rank(kind) == 5;
        return redFive ? baseId + 35 + (uint)Tiles.Suit(kind) : baseId + 1 + (uint)kind;
    }
}
