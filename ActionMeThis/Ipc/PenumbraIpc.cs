using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;

namespace ActionMeThis.Ipc;

/// <summary>
/// Wrapper over Penumbra's IPC surface.
///
/// Penumbra may be absent, disabled, or a different API version than we were built
/// against, so every call has to tolerate the subscriber throwing. The
/// Initialized/Disposed events tell us when Penumbra comes and goes at runtime.
///
/// The full set of subscribers lives in reference/penumbra-api/IpcSubscribers.
/// </summary>
public sealed class PenumbraIpc : IDisposable
{
    // Penumbra's breaking API version. Bump only after checking the changed signatures.
    private const int RequiredBreakingVersion = 5;

    private readonly IPluginLog log;

    private readonly ApiVersion apiVersion;
    private readonly GetEnabledState getEnabledState;
    private readonly GetModDirectory getModDirectory;
    private readonly GetModList getModList;
    private readonly GetCollections getCollections;
    private readonly GetCollection getCollection;
    private readonly GetAvailableModSettings getAvailableModSettings;
    private readonly GetCurrentModSettings getCurrentModSettings;
    private readonly TrySetMod trySetMod;
    private readonly TrySetModPriority trySetModPriority;
    private readonly TrySetModSettings trySetModSettings;
    private readonly RedrawObject redrawObject;

    private readonly EventSubscriber initializedSubscriber;
    private readonly EventSubscriber disposedSubscriber;

    public PenumbraIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        apiVersion = new ApiVersion(pluginInterface);
        getEnabledState = new GetEnabledState(pluginInterface);
        getModDirectory = new GetModDirectory(pluginInterface);
        getModList = new GetModList(pluginInterface);
        getCollections = new GetCollections(pluginInterface);
        getCollection = new GetCollection(pluginInterface);
        getAvailableModSettings = new GetAvailableModSettings(pluginInterface);
        getCurrentModSettings = new GetCurrentModSettings(pluginInterface);
        trySetMod = new TrySetMod(pluginInterface);
        trySetModPriority = new TrySetModPriority(pluginInterface);
        trySetModSettings = new TrySetModSettings(pluginInterface);
        redrawObject = new RedrawObject(pluginInterface);

        initializedSubscriber = Initialized.Subscriber(pluginInterface, OnPenumbraInitialized);
        disposedSubscriber = Disposed.Subscriber(pluginInterface, OnPenumbraDisposed);

        Refresh();
    }

    /// <summary>Whether Penumbra is loaded and speaks an API version we understand.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>The (breaking, feature) API version last read from Penumbra.</summary>
    public (int Breaking, int Features) Version { get; private set; }

    /// <summary>Raised whenever <see cref="IsAvailable"/> changes.</summary>
    public event Action? AvailabilityChanged;

    public void Refresh()
    {
        var wasAvailable = IsAvailable;

        try
        {
            Version = apiVersion.Invoke();
            IsAvailable = Version.Breaking == RequiredBreakingVersion;

            if (!IsAvailable)
            {
                log.Warning(
                    $"Penumbra API version {Version.Breaking}.{Version.Features} is not the expected breaking version {RequiredBreakingVersion}.");
            }
        }
        catch (Exception)
        {
            // Penumbra is not installed, not loaded yet, or currently reloading.
            Version = (0, 0);
            IsAvailable = false;
        }

        if (wasAvailable != IsAvailable)
            AvailabilityChanged?.Invoke();
    }

    /// <summary>Whether the user has Penumbra's mod loading switched on.</summary>
    public bool IsEnabled => Invoke(() => getEnabledState.Invoke(), false);

    /// <summary>The root folder Penumbra keeps mods in, or an empty string.</summary>
    public string ModDirectory => Invoke(() => getModDirectory.Invoke(), string.Empty);

    /// <summary>Mod directory name -> display name for every installed mod.</summary>
    public IReadOnlyDictionary<string, string> GetMods()
        => Invoke(() => (IReadOnlyDictionary<string, string>)getModList.Invoke(),
            new Dictionary<string, string>());

    /// <summary>Collection id -> name for every collection.</summary>
    public IReadOnlyDictionary<Guid, string> GetCollections()
        => Invoke(() => (IReadOnlyDictionary<Guid, string>)getCollections.Invoke(),
            new Dictionary<Guid, string>());

    /// <summary>The collection currently selected in Penumbra, if any.</summary>
    public (Guid Id, string Name)? GetCurrentCollection()
        => Invoke(() => getCollection.Invoke(ApiCollectionType.Current), null);

    /// <summary>
    /// The option groups of a mod: group name -> (option names, group kind).
    /// Null when the mod is unknown to Penumbra.
    /// </summary>
    public IReadOnlyDictionary<string, (string[] Options, GroupType Type)>? GetAvailableModSettings(
        string modDirectory, string modName)
        => Invoke(() => getAvailableModSettings.Invoke(modDirectory, modName), null);

    /// <summary>The mod's current enabled state, priority and selected options in a collection.</summary>
    public ModSettingsSnapshot? GetCurrentSettings(Guid collectionId, string modDirectory, string modName)
        => Invoke(() =>
        {
            var (ec, settings) = getCurrentModSettings.Invoke(collectionId, modDirectory, modName, false);
            if (ec != PenumbraApiEc.Success || settings is not { } value)
                return (ModSettingsSnapshot?)null;

            var (enabled, priority, options, inherited) = value;
            return new ModSettingsSnapshot(enabled, priority, options, inherited);
        }, null);

    public bool SetModEnabled(Guid collectionId, string modDirectory, string modName, bool enabled)
        => Check(Invoke(() => trySetMod.Invoke(collectionId, modDirectory, enabled, modName),
            PenumbraApiEc.UnknownError), nameof(SetModEnabled), modDirectory);

    public bool SetModPriority(Guid collectionId, string modDirectory, string modName, int priority)
        => Check(Invoke(() => trySetModPriority.Invoke(collectionId, modDirectory, priority, modName),
            PenumbraApiEc.UnknownError), nameof(SetModPriority), modDirectory);

    public bool SetModOptions(
        Guid collectionId, string modDirectory, string modName, string optionGroup, IReadOnlyList<string> options)
        => Check(Invoke(() => trySetModSettings.Invoke(collectionId, modDirectory, optionGroup, options, modName),
            PenumbraApiEc.UnknownError), nameof(SetModOptions), modDirectory + "/" + optionGroup);

    /// <summary>Redraw a single game object. Index 0 is the local player.</summary>
    public void Redraw(int gameObjectIndex = 0)
        => Invoke<object?>(() =>
        {
            redrawObject.Invoke(gameObjectIndex, RedrawType.Redraw);
            return null;
        }, null);

    private bool Check(PenumbraApiEc code, string what, string subject)
    {
        // NothingChanged means the setting already held the value we wanted.
        if (code is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged)
            return true;

        log.Warning($"Penumbra {what} failed for {subject}: {code}.");
        return false;
    }

    private T Invoke<T>(Func<T> action, T fallback)
    {
        if (!IsAvailable)
            return fallback;

        try
        {
            return action();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Penumbra IPC call failed.");
            return fallback;
        }
    }

    private void OnPenumbraInitialized()
    {
        log.Information("Penumbra initialized.");
        Refresh();
    }

    private void OnPenumbraDisposed()
    {
        log.Information("Penumbra disposed.");
        IsAvailable = false;
        Version = (0, 0);
        AvailabilityChanged?.Invoke();
    }

    public void Dispose()
    {
        initializedSubscriber.Dispose();
        disposedSubscriber.Dispose();
    }
}

/// <summary>A mod's settings in one collection at one point in time.</summary>
public readonly record struct ModSettingsSnapshot(
    bool Enabled,
    int Priority,
    IReadOnlyDictionary<string, List<string>> Options,
    bool Inherited);
