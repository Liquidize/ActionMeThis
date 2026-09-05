# ActionMeThis

A Dalamud plugin for FFXIV that changes **Penumbra** mod settings in response to what
your character is doing — draw your weapon and a mod switches on, sit down and an option
group changes, and everything goes back when you stop.

Built against **Dalamud API level 15** (`Dalamud.NET.Sdk/15.0.0`, `net10.0-windows`, x64).

## How it works

A **rule** is a list of **conditions** plus a list of **actions**:

- **Conditions** — every one has to be true for the rule to fire, so "weapon drawn AND
  near a friend" is two conditions. Each has a `Not` box, so "weapon sheathed" is the
  `Weapon drawn` condition inverted.
- **Actions** — what to do to Penumbra while the conditions all hold. Each one targets a
  collection and a mod, and can enable it, disable it, set its priority, or select
  options inside one of its option groups. A rule can carry as many as you like.

While every condition holds, the actions are applied. When one stops holding, the
settings they overwrote are put back. A rule with no conditions never fires.

### Condition types

**Player state** — weapon drawn, offhand drawn, sitting (either kind, or specifically on
the ground / on furniture), mounted, riding pillion, flying, swimming, diving, in combat,
casting, crafting, gathering, fishing, performing, carrying, emoting, jumping, stealthed,
dead, in a duty, in a cutscene, in GPose, in PvP, transformed, using a fashion accessory,
role playing.

**Proximity** — who is standing near you:

| Condition | Matches |
| --- | --- |
| Near another player | any player character but you |
| Near a friend | a player on your friend list |
| Near a party member | a player in your party |
| Near a specific player | someone you list by name |

Each takes a **radius** in yalms and a **how many** count, so "3+ party members within
15y" is one condition. Named entries are a bare character name, or `Name@World` when the
name alone is ambiguous; **Add target** fills one in from whoever you have targeted.

Nearby players are rescanned five times a second rather than every frame, and world names
are cached, so a crowded zone does not cost much.

### Reverting

Before an action changes anything, the value it is about to overwrite is captured, and
that is what gets restored. Only the setting the action actually touches is captured, so
a rule that flips one option group does not overwrite the rest of that mod on the way
out. Turn off **Revert when the rule stops matching** to make its changes stick.

Everything currently applied is also reverted when you disable a rule, delete it, switch
rules off entirely, press **Revert everything**, or unload the plugin.

### Debounce

Weapon draw flickers in combat, walking past someone puts them in and out of range, and
every reaction can cost a character redraw. So a rule's overall verdict — not each
condition separately — has to hold for a moment before it is acted on. Debouncing the
whole rule is what keeps a half-matched multi-condition rule from flickering while its
conditions settle at different times. The delay is configurable (default 250 ms) at the
top of the rule editor.

Loading screens do not count as a state change: the last known state is held while there
is no local player, so moving between zones does not revert and re-apply everything.

## Installing

Add this repository in game under `/xlsettings` -> Experimental -> **Custom Plugin
Repositories**:

```
https://raw.githubusercontent.com/Liquidize/ActionMeThis/main/repo.json
```

Save, then find **ActionMeThis** in `/xlplugins`. Penumbra has to be installed too, for
reasons that should be obvious.

## Using it

- `/actionmethis` (or `/amt`) — status window: Penumbra's state, which rules are applied
  or still settling, a live readout of what the game thinks you are doing, and a
  **Nearby** tab listing tracked players with their distance. Useful when a rule is not
  firing and you want to see why.
- `/actionmethis config` — the rule editor. Also reachable from the plugin installer.

Deleting a rule needs Ctrl held, to avoid losing one to a stray click.

## Layout

| Path | What it is |
| --- | --- |
| `ActionMeThis/Plugin.cs` | Entry point. Service injection, commands, framework-thread marshalling. |
| `ActionMeThis/Configuration.cs` | Persisted settings and the rule list. |
| `ActionMeThis/Triggers/` | The trigger enum, the state snapshot, and the watcher that samples it. |
| `ActionMeThis/Rules/` | Rules, conditions and actions, and the engine that applies and reverts them. |
| `ActionMeThis/Ipc/PenumbraIpc.cs` | Penumbra IPC, tolerant of Penumbra being absent or reloading. |
| `ActionMeThis/Windows/` | ImGui windows plus the cache that keeps IPC off the draw path. |
| `reference/` | Vendored upstream sources, for reading only. See `reference/README.md`. |

### Threading

Sampling runs on the framework thread; the UI draws on the render thread. The watcher
publishes immutable snapshots rather than live collections, the engine's applied-rule and
pending maps are behind a lock, and every Penumbra mutation the UI starts is marshalled
onto the framework thread before it runs.

Rules are evaluated every frame, so that path avoids LINQ and its per-call closures.

## Building

```
dotnet build ActionMeThis/ActionMeThis.csproj -c Debug
```

The SDK resolves Dalamud's assemblies from `%AppData%\XIVLauncher\addon\Hooks\dev`.
Set `DALAMUD_HOME` to override that. Output lands in `ActionMeThis/bin/Debug/` alongside
a generated `ActionMeThis.json` manifest. A `Release` build additionally produces
`ActionMeThis/bin/Release/ActionMeThis/latest.zip` via DalamudPackager.

## Running it in game

1. Build.
2. In game, `/xlsettings` -> Experimental -> **Dev Plugin Locations** -> add the full path
   to `ActionMeThis\bin\Debug` inside your clone.
3. `/xlplugins` -> Dev Tools -> Installed Dev Plugins -> load **ActionMeThis**.
4. `/actionmethis` opens the status window; `/xllog` shows the log output.

Reloading after a rebuild is done from the same Dev Tools menu — no game restart needed.

## Manifest fields

The `.json` manifest is generated from MSBuild properties in `ActionMeThis.csproj`
(`Name`, `Author`, `Punchline`, `Description`, `Tags`, `RepoUrl`, `Version`). Edit them
there, not in `bin/` and not in `repo.json` - both are generated.

## Cutting a release

`repo.json` is the custom repository file Dalamud reads. It is the packaged manifest plus
three download links, all pointing at `releases/latest/download/latest.zip`, so the links
never change - bumping `AssemblyVersion` is what tells Dalamud an update exists.

1. Bump `<Version>` in `ActionMeThis/ActionMeThis.csproj`.
2. Regenerate the repository file:
   ```
   ./scripts/Generate-RepoJson.ps1
   ```
3. Commit both, then tag and push:
   ```
   git tag v1.0.0.0 && git push origin main --tags
   ```

The `Release` workflow builds on any `v*` tag, refuses to continue if the tag and the
manifest version disagree, and attaches `latest.zip` to the GitHub release. To publish by
hand instead, build Release and upload `ActionMeThis/bin/Release/ActionMeThis/latest.zip`
as a release asset - the filename has to stay `latest.zip` for the download links to
resolve.

## Penumbra

`PenumbraIpc` covers API version, enabled state, mod directory, mod and collection lists,
per-mod option groups, reading and writing mod settings, and redraw. Every other endpoint
follows the same shape — construct the subscriber from `Penumbra.Api.IpcSubscribers` and
call `Invoke()`. Browse `reference/penumbra-api/IpcSubscribers/` for the full set.

Note the version split: the `Penumbra.Api` NuGet package is at **5.15.1**, while
`reference/penumbra-api` tracks upstream `main` (**5.17.0**), and their subscriber
signatures differ in places. Read the reference for concepts, but check the package before
relying on a newly added endpoint.
