using System;
using System.Collections.Generic;
using ActionMeThis.Rules;
using Dalamud.Configuration;

namespace ActionMeThis;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 2;

    // Bump CurrentVersion and migrate in Plugin's constructor whenever the shape changes.
    public int Version { get; set; } = CurrentVersion;

    public List<ModRule> Rules { get; set; } = [];

    /// <summary>
    /// How long a rule's verdict has to hold before it is acted on. Weapon draw in
    /// particular flickers during combat, and every reaction can cost a redraw.
    /// </summary>
    public int DebounceMilliseconds { get; set; } = 250;

    /// <summary>Master switch; when off, no rule is applied and applied rules are reverted.</summary>
    public bool RulesEnabled { get; set; } = true;

    public bool IsConfigWindowMovable { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
