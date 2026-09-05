using System;
using System.Collections.Generic;
using System.Linq;
using ActionMeThis.Ipc;
using ActionMeThis.Triggers;
using Dalamud.Plugin.Services;

namespace ActionMeThis.Rules;

/// <summary>
/// Turns trigger changes into Penumbra calls.
///
/// When a rule becomes active its actions are applied, and the settings they are about
/// to overwrite are captured first. When the rule goes inactive those captured values
/// are put back, so nothing is left changed behind the user's back. Only the settings
/// an action actually touches are captured, so a rule that flips one option group does
/// not clobber the rest of the mod on revert.
/// </summary>
public sealed class RuleEngine : IDisposable
{
    private readonly PenumbraIpc penumbra;
    private readonly Configuration configuration;
    private readonly IPluginLog log;

    /// <summary>Rules currently applied, with the settings needed to undo them.</summary>
    private readonly Dictionary<Guid, List<SavedSetting>> applied = [];

    /// <summary>
    /// Rules whose match state has flipped but has not held long enough to act on, with
    /// the time the flip was first seen.
    /// </summary>
    private readonly Dictionary<Guid, long> pendingSince = [];

    /// <summary>
    /// Guards <see cref="applied"/> and <see cref="pendingSince"/>. Sampling arrives on the
    /// framework thread while the config UI reads and edits from the render thread.
    /// </summary>
    private readonly object sync = new();

    public RuleEngine(PenumbraIpc penumbra, Configuration configuration, IPluginLog log)
    {
        this.penumbra = penumbra;
        this.configuration = configuration;
        this.log = log;

        this.penumbra.AvailabilityChanged += OnPenumbraAvailabilityChanged;
    }

    /// <summary>Raised after any rule is applied or reverted, so the UI can refresh.</summary>
    public event Action? StateChanged;

    public bool IsApplied(Guid ruleId)
    {
        lock (sync)
            return applied.ContainsKey(ruleId);
    }

    public IReadOnlyCollection<Guid> AppliedRules
    {
        get
        {
            lock (sync)
                return applied.Keys.ToArray();
        }
    }

    /// <summary>Whether a rule's match state has flipped but is still settling.</summary>
    public bool IsPending(Guid ruleId)
    {
        lock (sync)
            return pendingSince.ContainsKey(ruleId);
    }

    /// <summary>
    /// Reconcile every rule against a state reading. Called every frame, so the common
    /// case - nothing to do - has to stay cheap.
    /// </summary>
    public void Evaluate(PlayerSnapshot snapshot)
    {
        lock (sync)
            EvaluateLocked(snapshot);
    }

    private void EvaluateLocked(PlayerSnapshot snapshot)
    {
        if (!penumbra.IsAvailable)
            return;

        var now = Environment.TickCount64;
        var debounce = Math.Max(0, configuration.DebounceMilliseconds);

        var needsRedraw = false;
        var changed = false;

        foreach (var rule in configuration.Rules)
        {
            var wanted = rule.Enabled && rule.Matches(snapshot);
            var isApplied = applied.ContainsKey(rule.Id);

            if (wanted == isApplied)
            {
                // Settled back to where it already is; cancel any pending flip.
                pendingSince.Remove(rule.Id);
                continue;
            }

            // A rule can combine several conditions, and each may settle at a different
            // moment. Debouncing the rule's overall verdict rather than each condition
            // keeps a half-matched rule from flickering on the way to a stable state.
            if (!pendingSince.TryGetValue(rule.Id, out var since))
            {
                pendingSince[rule.Id] = now;
                continue;
            }

            if (now - since < debounce)
                continue;

            pendingSince.Remove(rule.Id);

            if (wanted)
                Apply(rule, ref needsRedraw);
            else
                Revert(rule, ref needsRedraw);

            changed = true;
        }

        if (needsRedraw)
            penumbra.Redraw();

        if (changed)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// Drop a rule that is no longer valid to keep applied - it was disabled, edited, or
    /// deleted while active. Reverts it first so the user's settings come back.
    /// </summary>
    public void Release(ModRule rule)
    {
        lock (sync)
            ReleaseLocked(rule);
    }

    private void ReleaseLocked(ModRule rule)
    {
        pendingSince.Remove(rule.Id);

        if (!applied.ContainsKey(rule.Id))
            return;

        var needsRedraw = false;
        Revert(rule, ref needsRedraw);

        if (needsRedraw)
            penumbra.Redraw();

        StateChanged?.Invoke();
    }

    /// <summary>Revert every applied rule. Used on unload and when the user asks.</summary>
    public void RevertAll()
    {
        lock (sync)
            RevertAllLocked();
    }

    private void RevertAllLocked()
    {
        pendingSince.Clear();

        if (applied.Count == 0)
            return;

        var needsRedraw = false;

        foreach (var ruleId in applied.Keys.ToList())
        {
            var rule = configuration.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
                Revert(rule, ref needsRedraw);
            else
                applied.Remove(ruleId);
        }

        if (needsRedraw)
            penumbra.Redraw();

        StateChanged?.Invoke();
    }

    private void Apply(ModRule rule, ref bool needsRedraw)
    {
        var saved = new List<SavedSetting>();

        foreach (var action in rule.Actions)
        {
            if (!action.IsConfigured)
                continue;

            if (!TryResolveCollection(action, out var collectionId))
            {
                log.Warning($"Rule '{rule.Name}': no collection to apply '{action.Describe()}' to.");
                continue;
            }

            var before = penumbra.GetCurrentSettings(collectionId, action.ModDirectory, action.ModName);
            if (before == null)
            {
                log.Warning($"Rule '{rule.Name}': Penumbra does not know mod '{action.ModDirectory}'.");
                continue;
            }

            if (ApplyAction(action, collectionId))
                saved.Add(SavedSetting.Capture(action, collectionId, before.Value));
        }

        applied[rule.Id] = saved;

        if (saved.Count > 0 && rule.RedrawOnChange)
            needsRedraw = true;

        log.Debug($"Rule '{rule.Name}' applied ({saved.Count} change(s)).");
    }

    private void Revert(ModRule rule, ref bool needsRedraw)
    {
        if (!applied.Remove(rule.Id, out var saved))
            return;

        if (!rule.RevertOnDeactivate || !penumbra.IsAvailable)
        {
            log.Debug($"Rule '{rule.Name}' released without reverting.");
            return;
        }

        // Undo in reverse so overlapping actions on one mod unwind cleanly.
        for (var i = saved.Count - 1; i >= 0; i--)
            RestoreSetting(saved[i]);

        if (saved.Count > 0 && rule.RedrawOnChange)
            needsRedraw = true;

        log.Debug($"Rule '{rule.Name}' reverted ({saved.Count} change(s)).");
    }

    private bool ApplyAction(ModAction action, Guid collectionId) => action.Kind switch
    {
        ModActionKind.Enable => penumbra.SetModEnabled(
            collectionId, action.ModDirectory, action.ModName, true),
        ModActionKind.Disable => penumbra.SetModEnabled(
            collectionId, action.ModDirectory, action.ModName, false),
        ModActionKind.SetPriority => penumbra.SetModPriority(
            collectionId, action.ModDirectory, action.ModName, action.Priority),
        ModActionKind.SetOptions => penumbra.SetModOptions(
            collectionId, action.ModDirectory, action.ModName, action.OptionGroup, action.Options),
        _ => false,
    };

    private void RestoreSetting(SavedSetting saved)
    {
        switch (saved.Kind)
        {
            case ModActionKind.Enable:
            case ModActionKind.Disable:
                penumbra.SetModEnabled(saved.CollectionId, saved.ModDirectory, saved.ModName, saved.Enabled);
                break;
            case ModActionKind.SetPriority:
                penumbra.SetModPriority(saved.CollectionId, saved.ModDirectory, saved.ModName, saved.Priority);
                break;
            case ModActionKind.SetOptions:
                penumbra.SetModOptions(
                    saved.CollectionId, saved.ModDirectory, saved.ModName, saved.OptionGroup, saved.Options);
                break;
        }
    }

    /// <summary>
    /// An action with an empty collection id follows whatever collection is current,
    /// which is resolved at apply time rather than stored.
    /// </summary>
    private bool TryResolveCollection(ModAction action, out Guid collectionId)
    {
        if (action.CollectionId != Guid.Empty)
        {
            collectionId = action.CollectionId;
            return true;
        }

        var current = penumbra.GetCurrentCollection();
        collectionId = current?.Id ?? Guid.Empty;
        return collectionId != Guid.Empty;
    }

    private void OnPenumbraAvailabilityChanged()
    {
        if (penumbra.IsAvailable)
            return;

        // Penumbra went away. Our saved settings are meaningless now and trying to
        // restore them would only log failures, so forget them.
        var dropped = 0;
        lock (sync)
        {
            dropped = applied.Count;
            applied.Clear();
            pendingSince.Clear();
        }

        if (dropped > 0)
        {
            log.Information($"Penumbra became unavailable; dropping {dropped} applied rule(s).");
            StateChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        penumbra.AvailabilityChanged -= OnPenumbraAvailabilityChanged;
        RevertAll();
    }

    /// <summary>The single setting one action overwrote, and what it held before.</summary>
    private readonly record struct SavedSetting(
        Guid CollectionId,
        string ModDirectory,
        string ModName,
        ModActionKind Kind,
        bool Enabled,
        int Priority,
        string OptionGroup,
        IReadOnlyList<string> Options)
    {
        public static SavedSetting Capture(ModAction action, Guid collectionId, ModSettingsSnapshot before)
        {
            var options = action.Kind == ModActionKind.SetOptions
                          && before.Options.TryGetValue(action.OptionGroup, out var selected)
                ? (IReadOnlyList<string>)selected.ToArray()
                : [];

            return new SavedSetting(
                collectionId,
                action.ModDirectory,
                action.ModName,
                action.Kind,
                before.Enabled,
                before.Priority,
                action.OptionGroup,
                options);
        }
    }
}
