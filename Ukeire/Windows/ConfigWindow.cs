using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Ukeire.Windows;

public class ConfigWindow : Window, IDisposable
{
    private static readonly string[] TierNames = ["Off (reviews only)", "Nudges", "Nudges + pointers", "Everything (full numbers)"];

    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Ukeire Settings###UkeireConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(420, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var tier = configuration.MaxHintTier;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderInt("Hints allowed", ref tier, 0, 3, TierNames[Math.Clamp(tier, 0, 3)]))
        {
            configuration.MaxHintTier = tier;
            configuration.Save();
        }
        TileWidgets.HelpMarker("Limit how much the tutor can tell you during a hand. Lower tiers make you do more of the thinking.");

        Toggle("Review my discards and calls", configuration.AutoReview, v => configuration.AutoReview = v,
               "After each discard, compare it against the most efficient options. After each chi, pon or kan, rate whether the call helped.");
        Toggle("Show shanten and waits", configuration.ShowShanten, v => configuration.ShowShanten = v,
               "Turn off to practise counting shanten yourself.");
        Toggle("Show defense ratings", configuration.ShowDefense, v => configuration.ShowDefense = v,
               "Turn off to practise reading suji and genbutsu yourself.");
        Toggle("Explain call chances", configuration.ExplainCalls, v => configuration.ExplainCalls = v,
               "When you could chi, pon or kan, show what the call would do to your hand and how risky it is.");
        Toggle("Open when a game starts", configuration.AutoOpen, v => configuration.AutoOpen = v, null);
    }

    private void Toggle(string label, bool value, Action<bool> set, string? help)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            configuration.Save();
        }
        if (help != null) TileWidgets.HelpMarker(help);
    }
}
