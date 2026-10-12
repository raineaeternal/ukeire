using Dalamud.Configuration;
using System;
using Ukeire.Windows;

namespace Ukeire;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Highest hint tier the player allows themselves: 0 off, 1 nudge, 2 pointer, 3 full answer.</summary>
    public int MaxHintTier { get; set; } = 3;

    /// <summary>Review each discard after it is made.</summary>
    public bool AutoReview { get; set; } = true;

    /// <summary>Show the shanten count and tile acceptance of the current hand without asking.</summary>
    public bool ShowShanten { get; set; } = true;

    /// <summary>Show the danger rating of hand tiles when an opponent is in riichi, without asking.</summary>
    public bool ShowDefense { get; set; } = true;

    /// <summary>When a chi, pon or kan is possible, explain its pros, cons and risk.</summary>
    public bool ExplainCalls { get; set; } = true;

    /// <summary>Open the tutor window automatically when a Mahjong game starts.</summary>
    public bool AutoOpen { get; set; } = true;

    /// <summary>Which of the game's tile icon sets to draw. FollowGame matches the design picked in the game's Mahjong settings.</summary>
    public TileDesign TileDesign { get; set; } = TileDesign.FollowGame;

    public int ReviewHistoryLimit { get; set; } = 200;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
