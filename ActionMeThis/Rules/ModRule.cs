using System;
using System.Collections.Generic;
using System.Linq;
using ActionMeThis.Triggers;

namespace ActionMeThis.Rules;

/// <summary>Conditions that must all hold, plus the mod changes to make while they do.</summary>
[Serializable]
public class ModRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New rule";

    public bool Enabled { get; set; } = true;

    /// <summary>Every one of these has to match. An empty list never matches.</summary>
    public List<RuleCondition> Conditions { get; set; } = [];

    /// <summary>Put the previous settings back when the rule stops matching.</summary>
    public bool RevertOnDeactivate { get; set; } = true;

    /// <summary>Redraw your character after applying or reverting, so the change shows immediately.</summary>
    public bool RedrawOnChange { get; set; } = true;

    public List<ModAction> Actions { get; set; } = [];

    /// <summary>
    /// Config version 1 stored a single trigger on the rule itself. These two are kept so
    /// that shape still deserialises; Migrate folds them into <see cref="Conditions"/>.
    /// </summary>
    public PlayerTrigger? Trigger { get; set; }

    public bool? Invert { get; set; }

    /// <summary>
    /// True when every condition holds. No conditions means the rule is inert.
    /// Called for every rule on every frame, so it avoids LINQ and its closures.
    /// </summary>
    public bool Matches(PlayerSnapshot snapshot)
    {
        if (Conditions.Count == 0)
            return false;

        foreach (var condition in Conditions)
        {
            if (!condition.Matches(snapshot))
                return false;
        }

        return true;
    }

    /// <summary>A one-line summary of the condition list, for the rule list and tooltips.</summary>
    public string DescribeConditions()
        => Conditions.Count == 0
            ? "no conditions - never fires"
            : string.Join(" and ", Conditions.Select(c => c.Describe()));

    /// <summary>Fold a version 1 rule's single trigger into the condition list.</summary>
    public void MigrateLegacyTrigger()
    {
        if (Trigger is { } trigger && Conditions.Count == 0)
            Conditions.Add(new RuleCondition { Trigger = trigger, Invert = Invert ?? false });

        Trigger = null;
        Invert = null;
    }

    public ModRule Clone()
    {
        var clone = new ModRule
        {
            Id = Guid.NewGuid(),
            Name = $"{Name} (copy)",
            Enabled = Enabled,
            RevertOnDeactivate = RevertOnDeactivate,
            RedrawOnChange = RedrawOnChange,
        };

        foreach (var condition in Conditions)
            clone.Conditions.Add(condition.Clone());

        foreach (var action in Actions)
            clone.Actions.Add(action.Clone());

        return clone;
    }
}
