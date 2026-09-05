using System;
using System.Collections.Generic;
using System.Linq;
using ActionMeThis.Ipc;
using Penumbra.Api.Enums;

namespace ActionMeThis.Windows;

/// <summary>
/// Caches the lists the config UI needs from Penumbra.
///
/// The UI draws every frame; Penumbra's IPC is not something to call at that rate, so
/// results are held until something invalidates them - the window opening, the user
/// pressing refresh, or Penumbra being reloaded.
/// </summary>
public sealed class PenumbraCache
{
    private readonly PenumbraIpc penumbra;

    private readonly Dictionary<string, IReadOnlyDictionary<string, (string[] Options, GroupType Type)>?> optionGroups =
        [];

    private List<ModEntry> mods = [];
    private List<CollectionEntry> collections = [];
    private bool stale = true;

    public PenumbraCache(PenumbraIpc penumbra)
    {
        this.penumbra = penumbra;
        this.penumbra.AvailabilityChanged += Invalidate;
    }

    public IReadOnlyList<ModEntry> Mods
    {
        get
        {
            Refresh();
            return mods;
        }
    }

    public IReadOnlyList<CollectionEntry> Collections
    {
        get
        {
            Refresh();
            return collections;
        }
    }

    public void Invalidate()
    {
        stale = true;
        optionGroups.Clear();
    }

    /// <summary>Option groups for a mod, or null if Penumbra does not know it.</summary>
    public IReadOnlyDictionary<string, (string[] Options, GroupType Type)>? OptionGroupsFor(
        string modDirectory, string modName)
    {
        if (modDirectory.Length == 0)
            return null;

        if (optionGroups.TryGetValue(modDirectory, out var cached))
            return cached;

        var groups = penumbra.GetAvailableModSettings(modDirectory, modName);
        optionGroups[modDirectory] = groups;
        return groups;
    }

    /// <summary>The display name for a mod directory, falling back to the stored name.</summary>
    public string ModLabel(string modDirectory, string fallback)
    {
        var match = Mods.FirstOrDefault(m => m.Directory == modDirectory);
        return match.Directory != null ? match.Name : fallback.Length > 0 ? fallback : modDirectory;
    }

    /// <summary>The display name for a collection id, falling back to the stored name.</summary>
    public string CollectionLabel(Guid id, string fallback)
    {
        if (id == Guid.Empty)
            return "Current collection";

        var match = Collections.FirstOrDefault(c => c.Id == id);
        return match.Name ?? (fallback.Length > 0 ? $"{fallback} (missing)" : id.ToString());
    }

    private void Refresh()
    {
        if (!stale)
            return;

        stale = false;

        mods = penumbra.GetMods()
                       .Select(kv => new ModEntry(kv.Key, kv.Value))
                       .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                       .ToList();

        collections = penumbra.GetCollections()
                              .Select(kv => new CollectionEntry(kv.Key, kv.Value))
                              .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                              .ToList();
    }

    public readonly record struct ModEntry(string Directory, string Name);

    public readonly record struct CollectionEntry(Guid Id, string Name);
}
