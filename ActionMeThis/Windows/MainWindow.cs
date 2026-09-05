using System;
using System.Linq;
using System.Numerics;
using ActionMeThis.Triggers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ActionMeThis.Windows;

/// <summary>
/// Status readout: what the game thinks you are doing, who is standing near you, and
/// which rules are acting on it. Mostly here so a rule that is not firing can be
/// diagnosed without guessing.
/// </summary>
public class MainWindow : Window, IDisposable
{
    private static readonly Vector4 On = new(0.4f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Off = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 Warning = new(0.95f, 0.75f, 0.25f, 1f);

    private readonly Plugin plugin;

    private bool showInactiveTriggers;

    public MainWindow(Plugin plugin)
        : base("ActionMeThis##ActionMeThisMainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 340),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(440, 540);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose()
    { }

    public override void Draw()
    {
        if (ImGui.Button("Configure rules"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        using (ImRaii.Disabled(plugin.Engine.AppliedRules.Count == 0))
        {
            if (ImGui.Button("Revert everything"))
                plugin.RevertAll();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Put back every setting the active rules have changed.");

        ImGuiHelpers.ScaledDummy(4f);
        DrawPenumbraStatus();

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Separator();

        DrawRuleStatus();

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Separator();

        using var tabs = ImRaii.TabBar("##status");
        if (!tabs)
            return;

        using (var state = ImRaii.TabItem("Player state"))
        {
            if (state)
                DrawTriggerStatus();
        }

        using var nearby = ImRaii.TabItem("Nearby");
        if (nearby)
            DrawNearbyStatus();
    }

    private void DrawPenumbraStatus()
    {
        var penumbra = plugin.Penumbra;

        if (!penumbra.IsAvailable)
        {
            ImGui.TextColored(Warning, "Penumbra is not available.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Retry"))
                penumbra.Refresh();

            return;
        }

        ImGui.TextColored(On, $"Penumbra {penumbra.Version.Breaking}.{penumbra.Version.Features}");
        ImGui.SameLine();
        ImGui.TextDisabled(penumbra.IsEnabled ? "(mods on)" : "(mods off)");

        if (!plugin.Configuration.RulesEnabled)
            ImGui.TextColored(Warning, "Rules are switched off in the settings.");
    }

    private void DrawRuleStatus()
    {
        var rules = plugin.Configuration.Rules;
        ImGui.TextUnformatted($"Rules ({rules.Count(r => r.Enabled)} enabled of {rules.Count})");

        if (rules.Count == 0)
        {
            ImGui.TextDisabled("No rules yet. Open the settings to add one.");
            return;
        }

        using var table = ImRaii.Table("##rules", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Conditions", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 70 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        foreach (var rule in rules)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(rule.Name);

            ImGui.TableNextColumn();
            var conditions = rule.DescribeConditions();
            ImGui.TextDisabled(conditions);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(conditions);

            ImGui.TableNextColumn();
            if (!rule.Enabled)
                ImGui.TextColored(Off, "off");
            else if (plugin.Engine.IsApplied(rule.Id))
                ImGui.TextColored(On, "applied");
            else if (plugin.Engine.IsPending(rule.Id))
                ImGui.TextColored(Warning, "settling");
            else
                ImGui.TextDisabled("waiting");
        }
    }

    private void DrawTriggerStatus()
    {
        ImGui.Checkbox("Show inactive", ref showInactiveTriggers);

        var flags = plugin.Watcher.Current.Flags;

        using var child = ImRaii.Child("##triggers", Vector2.Zero, true);
        if (!child)
            return;

        if (flags.Count == 0 && !showInactiveTriggers)
        {
            ImGui.TextDisabled("Nothing active right now.");
            return;
        }

        foreach (var trigger in PlayerTriggers.All)
        {
            // Proximity states depend on a radius and a name list, so they only mean
            // something in the context of a condition. The Nearby tab covers them.
            if (trigger.IsProximity())
                continue;

            var isActive = flags.Contains(trigger);
            if (!isActive && !showInactiveTriggers)
                continue;

            ImGui.TextColored(isActive ? On : Off, isActive ? "*" : "-");
            ImGui.SameLine();
            ImGui.TextUnformatted(trigger.Label());
        }
    }

    private void DrawNearbyStatus()
    {
        var snapshot = plugin.Watcher.Current;

        ImGui.TextDisabled($"{snapshot.Nearby.Count} player(s) tracked, nearest first.");

        using var child = ImRaii.Child("##nearby", Vector2.Zero, true);
        if (!child)
            return;

        if (snapshot.Nearby.Count == 0)
        {
            ImGui.TextDisabled(snapshot.HasPlayer ? "Nobody around." : "Not logged in.");
            return;
        }

        foreach (var player in snapshot.Nearby)
        {
            var highlight = player.IsFriend || player.IsPartyMember;
            ImGui.TextColored(highlight ? On : Off, highlight ? "*" : "-");
            ImGui.SameLine();
            ImGui.TextUnformatted(player.Describe());
        }
    }
}
