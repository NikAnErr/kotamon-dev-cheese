# Kotamon Dev Cheese

![Version](https://img.shields.io/badge/version-0.3.11-18a6b8)
![Unity](https://img.shields.io/badge/Unity-6000.4.1f1-black)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078d4)
![Runtime](https://img.shields.io/badge/runtime-BepInEx%206%20IL2CPP-8a5cf5)

An educational reverse-engineering and defensive security project for **KOTAMON**. It demonstrates how exposed client-side state can be inspected and modified—and documents what a production game should validate or keep authoritative.

> The repository does not contain the game, its resources, or instructions for bypassing third-party anti-cheat systems.

## Features

- **Noclip** with adjustable movement speed.
- **WorldSpeed** with an adjustable `Time.timeScale` multiplier.
- **Card, fragment, and figurine ESP** with classified colors, boxes, labels, and thin tracer lines.
- **Fragment HUD** showing `DirtyPartsCount`, the current target, and the native spawn timer.
- **Auto Cleanup** that collects dirty cards, card boxes, and all spawned fragments before removing only the engine-tracked normal junk. Figurines and all other rare objects are preserved.
- **Money editor** through the game's `ParametersController`.
- **Always Full Bag** toggle.
- **Max Card Collection** command, which unlocks every card at `Foil` quality.
- **All Cassettes** command, which unlocks every tape registered by the in-game tape player.
- Rebindable hotkeys, draggable UI windows, and proper cursor/camera capture.
- Self-contained Windows launcher with install, update, launch, backup, and uninstall flows.

## Launcher

The release launcher embeds the tested BepInEx 6 IL2CPP runtime, Unity 6000.4.1 base libraries, compatible generated interop assemblies, and the plugin DLL. A separate BepInEx installation is not required.

### Install

1. Download `KotamonDevCheat.exe` from the [latest release](https://github.com/NikAnErr/kotamon-dev-cheat/releases/latest).
2. Close KOTAMON completely.
3. Run the launcher and select the folder containing the game's executable.
4. Click **УСТАНОВИТЬ / ОБНОВИТЬ**, then **ЗАПУСТИТЬ ИГРУ**.

The launcher validates the selected game folder, backs up replaced loader files, and records whether it owns the installed BepInEx runtime.

### Uninstall

Click **ДЕИНСТАЛЛЯЦИЯ** and confirm the operation. If the launcher installed BepInEx, it removes that runtime as well. If BepInEx existed beforehand, the launcher preserves it and restores backed-up loader files.

## Default controls

| Key | Function |
|---|---|
| `Insert` | Open/close menu |
| `F1` | Noclip |
| `F2` | WorldSpeed |
| `F3` | ESP |
| `F4` | Auto Cleanup |
| `F5` | Always Full Bag |
| `F6` | Max Card Collection |
| `F7` | All Cassettes |

Every key can be rebound from the in-game menu.

## Technical notes

The plugin is loaded internally by BepInEx and uses generated IL2CPP interop assemblies to call the game's normal controllers. No process injector is used.

Recent KOTAMON builds expose card fragments as world pickups. Auto Cleanup uses the native `JunkZoneController._partPickups` registry and explicit item markers to protect and collect recognised fragments through the normal `PlayerPickupController.Pick(..., true)` route. Its destructive pass is restricted to confirmed `EJunkType.Common` objects using the engine's `ui_empty` data, so unidentified objects are retained for manual collection.

The Unity 6 build also required two compatibility adjustments:

- load generated interop assemblies before Unity logging initialization;
- rename duplicate compiler-generated `<>O` cache types in generated `UnityEngine.CoreModule.dll`.

The corresponding transformations are documented in the included PowerShell scripts.

The build scripts stage compiler inputs in a temporary path, so the full game directory name—including its commas—works without confusing Roslyn's reference parser.

## Build from source

Requirements:

- Windows x64;
- Visual Studio 2022 with the Roslyn C# compiler;
- PowerShell 5.1 or newer;
- a local KOTAMON development build with BepInEx IL2CPP and generated interop assemblies.

Build and install the plugin into a local game copy:

```powershell
.\build.ps1 -GameRoot 'C:\path\to\KOTAMON'
```

Compile without installing:

```powershell
.\build.ps1 -GameRoot 'C:\path\to\KOTAMON' -SkipInstall
```

Build the self-contained launcher:

```powershell
.\build-exe.ps1 -GameRoot 'C:\path\to\KOTAMON'
```

The launcher is written to `release/KotamonDevCheat.exe`. Build outputs are intentionally excluded from Git history; release binaries are published as GitHub Release assets.

## Repository layout

| Path | Purpose |
|---|---|
| `KotamonDevCheat.cs` | BepInEx IL2CPP plugin |
| `Launcher/Program.cs` | Windows Forms installer/launcher |
| `build.ps1` | Plugin build script |
| `build-exe.ps1` | Self-contained launcher packager |
| `BepInEx.Kotamon.cfg` | Tested Unity 6/BepInEx configuration |
| `patch-*.ps1` | Reproducible Unity 6 compatibility transformations |
| `SECURITY_NOTES.md` | Defensive findings and mitigations |
| `THIRD_PARTY_NOTICES.txt` | BepInEx attribution and modification notice |

## Defensive perspective

The companion [security notes](SECURITY_NOTES.md) discuss what the experiment exposes and how to reduce the attack surface: exclude development controllers from production, validate movement and economy server-side, minimize replication of hidden objects, and treat client integrity checks as only one layer of defense.

## Third-party software

The launcher bundles a modified BepInEx 6 IL2CPP runtime. BepInEx is distributed under LGPL-2.1; attribution, source links, and the compatibility transformation scripts are included in this repository and in the installed payload. See [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
