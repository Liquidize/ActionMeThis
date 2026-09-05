using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ActionMeThis.Rules;
using ActionMeThis.Triggers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Penumbra.Api.Enums;

namespace ActionMeThis.Windows;

/// <summary>The rule editor: pick conditions, pick what they do to which mods.</summary>
public class ConfigWindow : Window, IDisposable
{
    private static readonly IReadOnlyList<ModActionKind> ActionKinds =
        (ModActionKind[])Enum.GetValues(typeof(ModActionKind));

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly PenumbraCache cache;

    /// <summary>Scratch text per widget, keyed by owning object. Combo filters and name entry.</summary>
    private readonly Dictionary<string, string> scratch = [];

    private Guid selectedRuleId = Guid.Empty;

    public ConfigWindow(Plugin plugin)
        : base("ActionMeThis Rules###ActionMeThisConfigWindow")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        cache = new PenumbraCache(plugin.Penumbra);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 440),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(920, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    { }

    public override void OnOpen() => cache.Invalidate();

    public override void PreDraw()
    {
        // Flags have to be set before Draw() or they do not apply this frame.
        if (configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        DrawToolbar();
        ImGui.Separator();

        if (!plugin.Penumbra.IsAvailable)
        {
            ImGui.TextColored(Colors.Warning, "Penumbra is not available - rules will not run.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Retry"))
            {
                plugin.Penumbra.Refresh();
                cache.Invalidate();
            }

            ImGui.Separator();
        }

        var listWidth = 220 * ImGuiHelpers.GlobalScale;
        using (var list = ImRaii.Child("##ruleList", new Vector2(listWidth, 0), true))
        {
            if (list)
                DrawRuleList();
        }

        ImGui.SameLine();

        using var editor = ImRaii.Child("##ruleEditor", Vector2.Zero, true);
        if (!editor)
            return;

        var rule = configuration.Rules.FirstOrDefault(r => r.Id == selectedRuleId);
        if (rule == null)
        {
            ImGui.TextDisabled("Select a rule on the left, or add one.");
            return;
        }

        DrawRuleEditor(rule);
    }

    private void DrawToolbar()
    {
        var enabled = configuration.RulesEnabled;
        if (ImGui.Checkbox("Rules active", ref enabled))
        {
            configuration.RulesEnabled = enabled;
            plugin.SaveAndReevaluate();
        }

        Widgets.HelpMarker("Master switch. Turning this off reverts everything currently applied.");

        ImGui.SameLine(0, 20 * ImGuiHelpers.GlobalScale);
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);

        var debounce = configuration.DebounceMilliseconds;
        if (ImGui.DragInt("Debounce (ms)", ref debounce, 5f, 0, 3000))
        {
            configuration.DebounceMilliseconds = Math.Clamp(debounce, 0, 3000);
            plugin.SaveAndReevaluate();
        }

        Widgets.HelpMarker(
            "How long a rule's verdict must hold before it is acted on. Raise this if "
            + "drawing and sheathing quickly, or walking past someone, causes repeated redraws.");

        ImGui.SameLine(0, 20 * ImGuiHelpers.GlobalScale);
        if (ImGui.Button("Refresh from Penumbra"))
        {
            plugin.Penumbra.Refresh();
            cache.Invalidate();
        }

        Widgets.HelpMarker("Reload the mod and collection lists after installing or renaming mods.");
    }

    private void DrawRuleList()
    {
        if (ImGui.Button("Add rule", new Vector2(-1, 0)))
        {
            var rule = new ModRule { Name = $"Rule {configuration.Rules.Count + 1}" };
            rule.Conditions.Add(new RuleCondition());
            configuration.Rules.Add(rule);
            selectedRuleId = rule.Id;
            plugin.SaveAndReevaluate();
        }

        ImGui.Separator();

        for (var i = 0; i < configuration.Rules.Count; i++)
        {
            var rule = configuration.Rules[i];
            using var id = ImRaii.PushId(i);

            var applied = plugin.Engine.IsApplied(rule.Id);
            using (ImRaii.PushColor(ImGuiCol.Text, Colors.Active, applied))
            {
                if (ImGui.Selectable($"{(rule.Enabled ? string.Empty : "(off) ")}{rule.Name}",
                        rule.Id == selectedRuleId))
                    selectedRuleId = rule.Id;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"{rule.DescribeConditions()}\n"
                    + $"{rule.Actions.Count} action(s)\n"
                    + (applied ? "Currently applied." : "Not applied."));
            }
        }
    }

    private void DrawRuleEditor(ModRule rule)
    {
        var name = rule.Name;
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("Name", ref name, 64))
        {
            rule.Name = name;
            configuration.Save();
        }

        ImGui.SameLine(0, 20 * ImGuiHelpers.GlobalScale);

        var ruleEnabled = rule.Enabled;
        if (ImGui.Checkbox("Enabled", ref ruleEnabled))
        {
            rule.Enabled = ruleEnabled;
            // A rule that just went off has to give its settings back before it stops
            // being considered.
            if (!ruleEnabled)
                plugin.ReleaseRule(rule);

            plugin.SaveAndReevaluate();
        }

        ImGui.SameLine(0, 20 * ImGuiHelpers.GlobalScale);

        var move = 0;
        var index = configuration.Rules.IndexOf(rule);
        using (ImRaii.Disabled(index <= 0))
        {
            if (ImGui.SmallButton("Move up"))
                move = -1;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(index >= configuration.Rules.Count - 1))
        {
            if (ImGui.SmallButton("Move down"))
                move = 1;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Duplicate"))
        {
            var copy = rule.Clone();
            configuration.Rules.Insert(index + 1, copy);
            selectedRuleId = copy.Id;
            plugin.SaveAndReevaluate();
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, Colors.Danger))
        {
            if (ImGui.SmallButton("Delete") && ImGui.GetIO().KeyCtrl)
            {
                plugin.ReleaseRule(rule);
                configuration.Rules.Remove(rule);
                selectedRuleId = Guid.Empty;
                plugin.SaveAndReevaluate();
                return;
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hold Ctrl and click to delete this rule.");

        if (move != 0)
        {
            configuration.Rules.RemoveAt(index);
            configuration.Rules.Insert(Math.Clamp(index + move, 0, configuration.Rules.Count), rule);
            configuration.Save();
        }

        DrawRuleBehaviour(rule);

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        DrawConditionsSection(rule);

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        DrawActionsSection(rule);
    }

    private void DrawRuleBehaviour(ModRule rule)
    {
        var revert = rule.RevertOnDeactivate;
        if (ImGui.Checkbox("Revert when the rule stops matching", ref revert))
        {
            rule.RevertOnDeactivate = revert;
            configuration.Save();
        }

        Widgets.HelpMarker(
            "On by default. The settings each action overwrites are captured when the rule "
            + "fires and put back when it stops. Turn this off to make the change stick.");

        ImGui.SameLine(0, 20 * ImGuiHelpers.GlobalScale);

        var redraw = rule.RedrawOnChange;
        if (ImGui.Checkbox("Redraw character", ref redraw))
        {
            rule.RedrawOnChange = redraw;
            configuration.Save();
        }

        Widgets.HelpMarker("Redraw yourself after applying, so the change is visible immediately.");

        var status = plugin.Engine.IsApplied(rule.Id)
            ? "applied now"
            : plugin.Engine.IsPending(rule.Id)
                ? "settling"
                : "not applied";

        ImGui.TextDisabled($"{rule.DescribeConditions()} - {status}");
    }

    private void DrawConditionsSection(ModRule rule)
    {
        ImGui.TextUnformatted($"Conditions ({rule.Conditions.Count})");
        ImGui.SameLine();
        ImGui.TextDisabled("- all of these must be true");
        ImGui.SameLine();
        if (ImGui.SmallButton("Add condition"))
        {
            rule.Conditions.Add(new RuleCondition());
            plugin.SaveAndReevaluate();
        }

        if (rule.Conditions.Count == 0)
        {
            ImGui.TextColored(Colors.Warning, "No conditions - this rule will never fire.");
            return;
        }

        var snapshot = plugin.Watcher.Current;
        RuleCondition? remove = null;

        for (var i = 0; i < rule.Conditions.Count; i++)
        {
            var condition = rule.Conditions[i];
            using var id = ImRaii.PushId($"condition{i}");

            if (i > 0)
            {
                ImGui.TextDisabled("and");
                ImGui.SameLine();
            }

            ImGui.SetNextItemWidth(240 * ImGuiHelpers.GlobalScale);
            var trigger = condition.Trigger;
            if (Widgets.EnumCombo("##trigger", ref trigger, PlayerTriggers.All, t => t.Label()))
            {
                // The old condition's changes have to come off before the new one takes over.
                plugin.ReleaseRule(rule);
                condition.Trigger = trigger;
                plugin.SaveAndReevaluate();
            }

            ImGui.SameLine();
            var invert = condition.Invert;
            if (ImGui.Checkbox("Not", ref invert))
            {
                plugin.ReleaseRule(rule);
                condition.Invert = invert;
                plugin.SaveAndReevaluate();
            }

            Widgets.HelpMarker("Require this to be false instead of true.");

            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Button, Colors.Danger))
            {
                if (ImGui.SmallButton("Remove"))
                    remove = condition;
            }

            ImGui.SameLine();
            var holds = condition.Matches(snapshot);
            ImGui.TextColored(holds ? Colors.Active : Colors.Inactive, holds ? "true" : "false");

            var description = condition.Trigger.Description();
            if (description.Length > 0)
            {
                using var indent = ImRaii.PushIndent();
                ImGui.TextDisabled(description);
            }

            if (condition.Trigger.IsProximity())
            {
                using var indent = ImRaii.PushIndent();
                DrawProximityOptions(rule, condition, snapshot);
            }

            ImGuiHelpers.ScaledDummy(2f);
        }

        if (remove == null)
            return;

        plugin.ReleaseRule(rule);
        rule.Conditions.Remove(remove);
        plugin.SaveAndReevaluate();
    }

    private void DrawProximityOptions(ModRule rule, RuleCondition condition, PlayerSnapshot snapshot)
    {
        var width = 160 * ImGuiHelpers.GlobalScale;

        ImGui.SetNextItemWidth(width);
        var radius = condition.Radius;
        if (ImGui.DragFloat("Radius (yalms)", ref radius, 0.5f, 1f, 100f, "%.1f"))
        {
            condition.Radius = Math.Clamp(radius, 1f, 100f);
            configuration.Save();
        }

        Widgets.HelpMarker("Straight-line distance. A party member standing next to you is about 1-2 yalms away.");

        ImGui.SameLine(0, 20 * ImGuiHelpers.GlobalScale);
        ImGui.SetNextItemWidth(width);

        var minimum = condition.MinimumCount;
        if (ImGui.DragInt("How many", ref minimum, 0.2f, 1, 20))
        {
            condition.MinimumCount = Math.Clamp(minimum, 1, 20);
            configuration.Save();
        }

        Widgets.HelpMarker("How many matching players have to be in range. 1 means any one of them.");

        ImGui.SameLine();
        ImGui.TextDisabled($"({condition.CountNearby(snapshot)} in range now)");

        if (condition.Trigger == PlayerTrigger.NearNamedPlayer)
            DrawNameList(rule, condition);
    }

    private void DrawNameList(ModRule rule, RuleCondition condition)
    {
        var key = $"name:{condition.GetHashCode()}";
        var entry = scratch.TryGetValue(key, out var value) ? value : string.Empty;

        ImGui.SetNextItemWidth(240 * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##name", "Character name or Name@World", ref entry, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        scratch[key] = entry;

        ImGui.SameLine();
        if ((ImGui.SmallButton("Add") || submitted) && entry.Trim().Length > 0)
        {
            AddName(rule, condition, entry.Trim());
            scratch[key] = string.Empty;
        }

        // Typing a name exactly is error-prone, so offer the obvious shortcut.
        var target = Plugin.TargetManager.Target;
        var targetName = target is { ObjectKind: ObjectKind.Pc } ? target.Name.TextValue : string.Empty;

        ImGui.SameLine();
        using (ImRaii.Disabled(targetName.Length == 0))
        {
            if (ImGui.SmallButton("Add target"))
            {
                var world = target is IPlayerCharacter { HomeWorld.IsValid: true } pc
                    ? $"@{pc.HomeWorld.Value.Name}"
                    : string.Empty;

                AddName(rule, condition, targetName + world);
            }
        }

        if (targetName.Length > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip($"Add {targetName}.");

        if (condition.Names.Count == 0)
        {
            ImGui.TextColored(Colors.Warning, "Nobody listed - no one will ever match this.");
            return;
        }

        string? removeName = null;
        for (var i = 0; i < condition.Names.Count; i++)
        {
            using var id = ImRaii.PushId(i);

            if (ImGui.SmallButton("x"))
                removeName = condition.Names[i];

            ImGui.SameLine();
            ImGui.TextUnformatted(condition.Names[i]);
        }

        if (removeName == null)
            return;

        plugin.ReleaseRule(rule);
        condition.Names.Remove(removeName);
        plugin.SaveAndReevaluate();
    }

    private void AddName(ModRule rule, RuleCondition condition, string name)
    {
        if (condition.Names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            return;

        plugin.ReleaseRule(rule);
        condition.Names.Add(name);
        plugin.SaveAndReevaluate();
    }

    private void DrawActionsSection(ModRule rule)
    {
        ImGui.TextUnformatted($"Actions ({rule.Actions.Count})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Add action"))
        {
            rule.Actions.Add(new ModAction());
            configuration.Save();
        }

        if (rule.Actions.Count == 0)
        {
            ImGui.TextDisabled("No actions yet. Add one to pick a mod and what to do with it.");
            return;
        }

        using var child = ImRaii.Child("##actions", Vector2.Zero, false);
        if (!child)
            return;

        ModAction? remove = null;

        for (var i = 0; i < rule.Actions.Count; i++)
        {
            var action = rule.Actions[i];
            using var id = ImRaii.PushId(i);

            var header = $"{i + 1}. {action.Describe()}###action{i}";
            if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            using var indent = ImRaii.PushIndent();

            DrawAction(rule, action);

            using (ImRaii.PushColor(ImGuiCol.Button, Colors.Danger))
            {
                if (ImGui.SmallButton("Remove action"))
                    remove = action;
            }

            ImGuiHelpers.ScaledDummy(4f);
        }

        if (remove == null)
            return;

        plugin.ReleaseRule(rule);
        rule.Actions.Remove(remove);
        plugin.SaveAndReevaluate();
    }

    private void DrawAction(ModRule rule, ModAction action)
    {
        var width = 300 * ImGuiHelpers.GlobalScale;

        // Collection.
        ImGui.SetNextItemWidth(width);
        var collectionItems = new List<(string, string)> { (Guid.Empty.ToString(), "Current collection") };
        collectionItems.AddRange(cache.Collections.Select(c => (c.Id.ToString(), c.Name)));

        var collectionFilter = GetScratch("collection", action);
        if (Widgets.FilteredCombo("##collection", cache.CollectionLabel(action.CollectionId, action.CollectionName),
                collectionItems, ref collectionFilter, out var pickedCollection))
        {
            plugin.ReleaseRule(rule);
            action.CollectionId = Guid.Parse(pickedCollection);
            action.CollectionName = action.CollectionId == Guid.Empty
                ? string.Empty
                : cache.Collections.FirstOrDefault(c => c.Id == action.CollectionId).Name ?? string.Empty;
            plugin.SaveAndReevaluate();
        }

        SetScratch("collection", action, collectionFilter);
        ImGui.SameLine();
        ImGui.TextUnformatted("Collection");
        Widgets.HelpMarker(
            "\"Current collection\" follows whatever Penumbra has selected at the moment the rule fires.");

        // Mod.
        ImGui.SetNextItemWidth(width);
        var modItems = cache.Mods.Select(m => (m.Directory, m.Name)).ToList();
        var modFilter = GetScratch("mod", action);
        var modPreview = action.ModDirectory.Length == 0
            ? "Pick a mod..."
            : cache.ModLabel(action.ModDirectory, action.ModName);

        if (Widgets.FilteredCombo("##mod", modPreview, modItems, ref modFilter, out var pickedMod))
        {
            plugin.ReleaseRule(rule);
            action.ModDirectory = pickedMod;
            action.ModName = cache.Mods.FirstOrDefault(m => m.Directory == pickedMod).Name ?? string.Empty;
            action.OptionGroup = string.Empty;
            action.Options.Clear();
            plugin.SaveAndReevaluate();
        }

        SetScratch("mod", action, modFilter);
        ImGui.SameLine();
        ImGui.TextUnformatted("Mod");

        // What to do with it.
        ImGui.SetNextItemWidth(width);
        var kind = action.Kind;
        if (Widgets.EnumCombo("##kind", ref kind, ActionKinds, KindLabel))
        {
            plugin.ReleaseRule(rule);
            action.Kind = kind;
            plugin.SaveAndReevaluate();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("Action");

        switch (action.Kind)
        {
            case ModActionKind.SetPriority:
                ImGui.SetNextItemWidth(width);
                var priority = action.Priority;
                if (ImGui.InputInt("Priority", ref priority))
                {
                    action.Priority = priority;
                    configuration.Save();
                }

                break;

            case ModActionKind.SetOptions:
                DrawOptionPicker(rule, action, width);
                break;
        }
    }

    private void DrawOptionPicker(ModRule rule, ModAction action, float width)
    {
        var groups = cache.OptionGroupsFor(action.ModDirectory, action.ModName);
        if (groups == null)
        {
            ImGui.TextColored(Colors.Warning,
                action.ModDirectory.Length == 0
                    ? "Pick a mod first."
                    : "Penumbra does not know this mod - it may have been removed or renamed.");
            return;
        }

        if (groups.Count == 0)
        {
            ImGui.TextDisabled("This mod has no option groups.");
            return;
        }

        ImGui.SetNextItemWidth(width);
        var groupItems = groups.Keys.Select(k => (k, k)).ToList();
        var groupFilter = GetScratch("group", action);
        var groupPreview = action.OptionGroup.Length == 0 ? "Pick an option group..." : action.OptionGroup;

        if (Widgets.FilteredCombo("##group", groupPreview, groupItems, ref groupFilter, out var pickedGroup))
        {
            plugin.ReleaseRule(rule);
            action.OptionGroup = pickedGroup;
            action.Options.Clear();
            plugin.SaveAndReevaluate();
        }

        SetScratch("group", action, groupFilter);
        ImGui.SameLine();
        ImGui.TextUnformatted("Option group");

        if (action.OptionGroup.Length == 0 || !groups.TryGetValue(action.OptionGroup, out var group))
            return;

        // Single-selection groups hold exactly one option; multi-selection groups any number.
        var single = group.Type == GroupType.Single;
        ImGui.TextDisabled(single ? "Choose one:" : "Choose any:");

        for (var i = 0; i < group.Options.Length; i++)
        {
            var option = group.Options[i];

            // Option names are mod-authored and need not be unique, so give each row its
            // own ImGui id rather than letting the label be the id.
            using var optionId = ImRaii.PushId(i);

            var selected = action.Options.Contains(option);
            if (!ImGui.Checkbox(option, ref selected))
                continue;

            plugin.ReleaseRule(rule);

            if (single)
            {
                action.Options.Clear();
                if (selected)
                    action.Options.Add(option);
            }
            else if (selected)
            {
                action.Options.Add(option);
            }
            else
            {
                action.Options.Remove(option);
            }

            plugin.SaveAndReevaluate();
        }
    }

    private static string KindLabel(ModActionKind kind) => kind switch
    {
        ModActionKind.Enable      => "Enable the mod",
        ModActionKind.Disable     => "Disable the mod",
        ModActionKind.SetPriority => "Set priority",
        ModActionKind.SetOptions  => "Select options",
        _                         => kind.ToString(),
    };

    private string GetScratch(string kind, object owner)
        => scratch.TryGetValue(ScratchKey(kind, owner), out var value) ? value : string.Empty;

    private void SetScratch(string kind, object owner, string value)
        => scratch[ScratchKey(kind, owner)] = value;

    private static string ScratchKey(string kind, object owner)
        => $"{kind}:{owner.GetHashCode()}";

    private static class Colors
    {
        public static readonly Vector4 Warning = new(0.95f, 0.75f, 0.25f, 1f);
        public static readonly Vector4 Danger = new(0.55f, 0.16f, 0.16f, 1f);
        public static readonly Vector4 Active = new(0.4f, 0.85f, 0.45f, 1f);
        public static readonly Vector4 Inactive = new(0.55f, 0.55f, 0.55f, 1f);
    }
}
