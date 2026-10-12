using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Ukeire.Mahjong;

namespace Ukeire.Windows;

public static class TileWidgets
{
    public static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    public static readonly Vector4 Warn = new(0.95f, 0.75f, 0.30f, 1f);
    public static readonly Vector4 Bad = new(0.95f, 0.40f, 0.40f, 1f);
    public static readonly Vector4 Muted = new(0.65f, 0.65f, 0.65f, 1f);
    public static readonly Vector4 Yakuman = new(1.00f, 0.82f, 0.30f, 1f);

    /// <summary>The tile icon set to draw with, resolved once per frame by the window that draws tiles.</summary>
    public static TileDesign Design { get; set; } = TileDesign.Traditional;

    public static Vector4 SuitColor(int kind)
    {
        if (Tiles.IsDragon(kind)) return new Vector4(0.55f, 0.45f, 0.20f, 1f);
        if (Tiles.IsHonor(kind)) return new Vector4(0.35f, 0.35f, 0.40f, 1f);
        return Tiles.Suit(kind) switch
        {
            0 => new Vector4(0.60f, 0.22f, 0.22f, 1f),
            1 => new Vector4(0.22f, 0.35f, 0.62f, 1f),
            _ => new Vector4(0.20f, 0.50f, 0.28f, 1f),
        };
    }

    public static Vector4 SafetyColor(SafetyLevel level) => level switch
    {
        SafetyLevel.Safe or SafetyLevel.VerySafe => Good,
        SafetyLevel.FairlySafe => new Vector4(0.75f, 0.85f, 0.40f, 1f),
        SafetyLevel.Risky => Warn,
        _ => Bad,
    };

    /// <summary>
    /// Draws one tile using the game's own tile icon for the current <see cref="Design"/>. Returns true when clicked.
    /// Falls back to a coloured chip with the tile's short name while the icon loads.
    /// </summary>
    public static bool Tile(int kind, string id, bool red = false, Vector4? outline = null)
    {
        // The game's tile icons are 40x52.
        var size = new Vector2(34, 44) * ImGuiHelpers.GlobalScale;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = 3f * ImGuiHelpers.GlobalScale;

        var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(TileIcons.IconId(kind, red, Design))).GetWrapOrDefault();
        if (icon != null)
        {
            drawList.AddImageRounded(icon.Handle, pos, pos + size, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(SuitColor(kind)), rounding);
            var label = red ? $"{Tiles.Short(kind)}*" : Tiles.Short(kind);
            drawList.AddText(pos + ((size - ImGui.CalcTextSize(label)) / 2), 0xFFFFFFFF, label);
        }
        if (hovered) drawList.AddRectFilled(pos, pos + size, 0x30FFFFFF, rounding);
        if (outline.HasValue) drawList.AddRect(pos, pos + size, ImGui.GetColorU32(outline.Value), rounding, ImDrawFlags.None, 2f * ImGuiHelpers.GlobalScale);

        if (hovered)
            ImGui.SetTooltip(red ? $"{Tiles.Name(kind)} (red five, dora)" : Tiles.Name(kind));
        return clicked;
    }

    /// <summary>Draws a row of tiles from a count array. <paramref name="outline"/> can colour individual kinds.</summary>
    /// <summary>Draws the hand sorted by kind. A <paramref name="drawn"/> tile (already counted in <paramref name="counts"/>) is pulled out and drawn apart on the right.</summary>
    public static void Hand(ReadOnlySpan<int> counts, ReadOnlySpan<int> reds, string id, Func<int, Vector4?>? outline = null, int drawn = -1, bool drawnRed = false)
    {
        var first = true;
        for (var k = 0; k < Tiles.KindCount; k++)
        {
            var count = counts[k] - (k == drawn ? 1 : 0);
            var redCount = reds[k] - (k == drawn && drawnRed ? 1 : 0);
            for (var n = 0; n < count; n++)
            {
                if (!first) ImGui.SameLine(0, 2 * ImGuiHelpers.GlobalScale);
                first = false;
                Tile(k, $"{id}{k}_{n}", n < redCount, outline?.Invoke(k));
            }
        }
        if (Tiles.IsValid(drawn))
        {
            if (!first) ImGui.SameLine(0, 12 * ImGuiHelpers.GlobalScale);
            first = false;
            Tile(drawn, $"{id}drawn", drawnRed, outline?.Invoke(drawn));
        }
        if (first) ImGui.TextColored(Muted, "(empty)");
    }

    public static void Row(IEnumerable<int> kinds, string id)
    {
        var first = true;
        var i = 0;
        foreach (var k in kinds)
        {
            if (!first) ImGui.SameLine(0, 2 * ImGuiHelpers.GlobalScale);
            first = false;
            Tile(k, $"{id}{i++}");
        }
        if (first) ImGui.TextColored(Muted, "(none)");
    }

    /// <summary>A combo listing every tile kind. Returns the picked kind, or -1.</summary>
    public static int Picker(string label, string preview)
    {
        var picked = -1;
        using var combo = ImRaii.Combo(label, preview);
        if (!combo.Success) return picked;
        for (var k = 0; k < Tiles.KindCount; k++)
            if (ImGui.Selectable($"{Tiles.Short(k)}  {Tiles.Name(k)}"))
                picked = k;
        return picked;
    }

    public static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextColored(Muted, "(?)");
        if (ImGui.IsItemHovered())
        {
            using var tip = ImRaii.Tooltip();
            using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 30);
            ImGui.TextUnformatted(text);
        }
    }
}
