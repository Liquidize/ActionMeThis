using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace ActionMeThis.Windows;

public static class Widgets
{
    /// <summary>
    /// A combo with a filter box, for lists too long to scroll - a mod folder can hold
    /// hundreds of entries. Returns true on the frame something is picked.
    /// </summary>
    public static bool FilteredCombo(
        string id,
        string preview,
        IReadOnlyList<(string Key, string Label)> items,
        ref string filter,
        out string selected)
    {
        selected = string.Empty;

        using var combo = ImRaii.Combo(id, preview, ImGuiComboFlags.HeightLarge);
        if (!combo)
            return false;

        if (ImGui.IsWindowAppearing())
        {
            filter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Filter...", ref filter, 128);

        var needle = filter;
        using var child = ImRaii.Child("##items", new Vector2(0, 260 * ImGuiHelpers.GlobalScale));
        if (!child)
            return false;

        var picked = false;
        var shown = 0;

        foreach (var (key, label) in items)
        {
            if (needle.Length > 0 && !label.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            shown++;
            if (!ImGui.Selectable(label))
                continue;

            selected = key;
            picked = true;
        }

        if (shown == 0)
            ImGui.TextDisabled(items.Count == 0 ? "Nothing to choose from." : "No match.");

        return picked;
    }

    /// <summary>A plain combo over an enum, labelled by a caller-supplied function.</summary>
    public static bool EnumCombo<T>(string id, ref T value, IReadOnlyList<T> values, Func<T, string> label)
        where T : struct, Enum
    {
        var changed = false;

        using var combo = ImRaii.Combo(id, label(value));
        if (!combo)
            return false;

        foreach (var candidate in values)
        {
            if (!ImGui.Selectable(label(candidate), candidate.Equals(value)))
                continue;

            value = candidate;
            changed = true;
        }

        return changed;
    }

    /// <summary>A grey (?) that shows <paramref name="text"/> on hover.</summary>
    public static void HelpMarker(string text)
    {
        if (text.Length == 0)
            return;

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }
}
