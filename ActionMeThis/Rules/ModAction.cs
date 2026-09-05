using System;
using System.Collections.Generic;

namespace ActionMeThis.Rules;

public enum ModActionKind
{
    /// <summary>Turn the mod on in the collection.</summary>
    Enable,

    /// <summary>Turn the mod off in the collection.</summary>
    Disable,

    /// <summary>Set the mod's priority.</summary>
    SetPriority,

    /// <summary>Select specific options inside one of the mod's option groups.</summary>
    SetOptions,
}

/// <summary>One change to apply to one Penumbra mod while a rule is active.</summary>
[Serializable]
public class ModAction
{
    /// <summary>Target collection. <see cref="Guid.Empty"/> means "whatever collection is current".</summary>
    public Guid CollectionId { get; set; } = Guid.Empty;

    /// <summary>Cached collection name, so the UI can still label a collection that has gone missing.</summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>Penumbra identifies mods by their folder name; this is the stable key.</summary>
    public string ModDirectory { get; set; } = string.Empty;

    /// <summary>Display name, passed to Penumbra as a fallback and shown in the UI.</summary>
    public string ModName { get; set; } = string.Empty;

    public ModActionKind Kind { get; set; } = ModActionKind.Enable;

    public int Priority { get; set; }

    public string OptionGroup { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public bool IsConfigured => ModDirectory.Length > 0
                             && (Kind != ModActionKind.SetOptions || OptionGroup.Length > 0);

    public string Describe() => Kind switch
    {
        ModActionKind.Enable      => $"Enable {DisplayMod}",
        ModActionKind.Disable     => $"Disable {DisplayMod}",
        ModActionKind.SetPriority => $"{DisplayMod} priority -> {Priority}",
        ModActionKind.SetOptions  => $"{DisplayMod} / {OptionGroup} -> {(Options.Count == 0 ? "(none)" : string.Join(", ", Options))}",
        _                         => DisplayMod,
    };

    private string DisplayMod => ModName.Length > 0 ? ModName : ModDirectory.Length > 0 ? ModDirectory : "(no mod)";

    public ModAction Clone() => new()
    {
        CollectionId = CollectionId,
        CollectionName = CollectionName,
        ModDirectory = ModDirectory,
        ModName = ModName,
        Kind = Kind,
        Priority = Priority,
        OptionGroup = OptionGroup,
        Options = [.. Options],
    };
}
