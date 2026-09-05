using System;
using System.Collections.Generic;
using ActionMeThis.Ipc;
using ActionMeThis.Rules;
using ActionMeThis.Triggers;
using ActionMeThis.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ActionMeThis;

public sealed class Plugin : IDalamudPlugin
{
    // Dalamud injects these before the constructor runs. Add more from
    // reference/dalamud/Dalamud/Plugin/Services as you need them.
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/actionmethis";
    private const string ShortCommandName = "/amt";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Migrate(Configuration);

        Penumbra = new PenumbraIpc(PluginInterface, Log);
        Engine = new RuleEngine(Penumbra, Configuration, Log);
        Watcher = new PlayerStateWatcher(Framework, ObjectTable, Condition, ClientState);

        Watcher.Sampled += OnSampled;
        Penumbra.AvailabilityChanged += OnPenumbraAvailabilityChanged;

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open ActionMeThis. Use \"/actionmethis config\" for the rule editor.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /actionmethis.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"{PluginInterface.Manifest.Name} loaded with {Configuration.Rules.Count} rule(s).");
    }

    public Configuration Configuration { get; }

    public PenumbraIpc Penumbra { get; }

    public RuleEngine Engine { get; }

    public PlayerStateWatcher Watcher { get; }

    public readonly WindowSystem WindowSystem = new("ActionMeThis");

    private ConfigWindow ConfigWindow { get; }

    private MainWindow MainWindow { get; }

    /// <summary>
    /// Persist the config and reconcile rules against the current state. Call this after
    /// any edit in the UI - a rule that was just disabled or retargeted has to be
    /// reverted before the new shape takes over.
    /// </summary>
    public void SaveAndReevaluate()
    {
        Configuration.Save();

        OnFramework(() =>
        {
            if (!Configuration.RulesEnabled)
                Engine.RevertAll();
            else
                Engine.Evaluate(Watcher.Current);
        });
    }

    /// <summary>Revert a rule that is being edited or removed while it is applied.</summary>
    public void ReleaseRule(ModRule rule) => OnFramework(() => Engine.Release(rule));

    /// <summary>Put back every setting the applied rules have changed.</summary>
    public void RevertAll() => OnFramework(Engine.RevertAll);

    /// <summary>
    /// Run Penumbra work on the framework thread. The config UI draws on the render
    /// thread, and mod changes end in a character redraw, which belongs on the game's
    /// own thread. Calls already on that thread run inline, so the trigger path is
    /// unaffected. Queued work keeps its order, so a release still lands before the
    /// re-evaluation that follows it.
    /// </summary>
    private static void OnFramework(Action action)
    {
        if (Framework.IsInFrameworkUpdateThread)
            action();
        else
            Framework.RunOnFrameworkThread(action);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        Watcher.Sampled -= OnSampled;
        Penumbra.AvailabilityChanged -= OnPenumbraAvailabilityChanged;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        // Order matters: stop sampling, put the user's settings back, then drop IPC.
        Watcher.Dispose();
        Engine.Dispose();
        Penumbra.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
    }

    /// <summary>
    /// Bring an older config forward. Version 0 is the pre-rules scaffold and carries
    /// nothing; version 1 stored one trigger per rule, which becomes a single condition.
    /// </summary>
    private static void Migrate(Configuration configuration)
    {
        if (configuration.Version >= Configuration.CurrentVersion)
            return;

        foreach (var rule in configuration.Rules)
            rule.MigrateLegacyTrigger();

        var from = configuration.Version;
        configuration.Version = Configuration.CurrentVersion;
        configuration.Save();
        Log.Information($"Configuration migrated from version {from} to {Configuration.CurrentVersion}.");
    }

    private void OnSampled(PlayerSnapshot snapshot)
    {
        if (!Configuration.RulesEnabled)
            return;

        Engine.Evaluate(snapshot);
    }

    private void OnPenumbraAvailabilityChanged()
    {
        // Penumbra just came back; re-apply whatever should currently be active.
        if (Penumbra.IsAvailable && Configuration.RulesEnabled)
            Engine.Evaluate(Watcher.Current);
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            ToggleConfigUi();
        else
            MainWindow.Toggle();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    public void ToggleMainUi() => MainWindow.Toggle();
}
