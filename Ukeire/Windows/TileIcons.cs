using Dalamud.Game.Config;
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
    /// Bit of UiConfig EmjSetting that is assumed to select the Doman tile design. Unconfirmed: the raw value is on
    /// the Debug tab so it can be checked by switching the design in the game's Mahjong settings.
    /// </summary>
    private const uint DomanDesignBit = 1u << 0;

    private const string EmjSettingName = "EmjSetting";

    /// <summary>The raw EmjSetting value and the config section it was found in, or null if it couldn't be read.</summary>
    public static (uint Value, string Section)? ReadGameSetting()
    {
        // Dalamud has no enum member for it, so look it up by name in each section.
        (string Name, GameConfigSection Section)[] sections =
            [("UiConfig", Plugin.GameConfig.UiConfig), ("UiControl", Plugin.GameConfig.UiControl), ("System", Plugin.GameConfig.System)];
        foreach (var (name, section) in sections)
            if (section.TryGetUInt(EmjSettingName, out var value)) return (value, name);
        return null;
    }

    /// <summary>The raw EmjSetting value, or null if it couldn't be read.</summary>
    public static uint? GameSetting => ReadGameSetting()?.Value;

    public static TileDesign Resolve(TileDesign chosen) => chosen switch
    {
        TileDesign.FollowGame => (GameSetting & DomanDesignBit) is > 0 ? TileDesign.Doman : TileDesign.Traditional,
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
