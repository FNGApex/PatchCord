<p align="center"><img src="docs/logo.png" width="104" alt="PatchCord"></p>
<h1 align="center">PatchCord</h1>

Keeps your Discord client mod installed. Discord wipes client mods every time it
auto-updates; PatchCord sits in the tray, notices when an install is running
unpatched, re-applies your mod, and restarts Discord.

Works with Vencord, Equicord, and BetterDiscord (pick one in Options). OpenAsar can
be kept installed alongside any of them.

## Screenshots

| Status | Options |
|--------|---------|
| ![Status](docs/status.png) | ![Options](docs/options.png) |

## How it works

It reproduces what each mod's installer does, then re-does it after an update wipes it:

- Vencord / Equicord: rename `resources\app.asar` to `_app.asar` and write a small
  stub `app.asar` that requires the mod's `dist\patcher.js`.
- BetterDiscord: overwrite `modules\discord_desktop_core\index.js` so it requires
  `%APPDATA%\BetterDiscord\data\betterdiscord.asar`.

It reuses the files each mod already put on disk, so there's nothing to download for
the mods themselves. If the mod you picked isn't installed, PatchCord leaves Discord
alone and shows a button to that mod's installer.

OpenAsar is optional and off by default. When on, it's downloaded from OpenAsar's
GitHub releases (cached locally) and re-applied under whichever mod you use.

## Install

Download `PatchCord.exe` from [Releases](https://github.com/tomgks/PatchCord/releases/latest)
and run it. It lives in the tray and keeps your mod patched. To have it
open automatically when you sign in to Windows, turn on **Run at startup** in the
Options tab.

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File publish.ps1
```

This writes a self-contained `publish\PatchCord.exe` (~66 MB) that runs without a
separate .NET install. For development: `dotnet build src`, or run
`PatchCord.exe --selftest` to build the UI and exit.

### Building on macOS (Windows cross-compile check)

`dotnet build src/PatchCord.csproj` works on macOS for editing and CI — the
`EnableWindowsTargeting` property in the csproj allows the Windows reference packs to
resolve on non-Windows hosts. **The resulting binary does not run on macOS**: the TFM
is `net10.0-windows` and the app depends on WPF and WinForms, which are Windows-only.
Runnable artifacts come only from a Windows `publish.ps1` run. A `dotnet publish` on
macOS is a compile-check only — it will not produce a usable application.

## macOS (Apple Silicon)

PatchCord runs natively on macOS (osx-arm64) and keeps your Mac's Discord patched
with Vencord, Equicord, or BetterDiscord — re-patching automatically after Discord
auto-updates.

### Install (macOS)

Build from source using the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
Xcode Command Line Tools:

```bash
chmod +x publish-mac.sh
./publish-mac.sh
```

This writes a self-contained `publish/PatchCord.app` (~107 MB) that requires no
separate .NET install.

**First launch (Gatekeeper):** the app is ad-hoc signed but not notarized for
distribution. On first run, right-click `PatchCord.app` → **Open** to bypass the
Gatekeeper "unidentified developer" dialog. Subsequent launches open normally.

### Full Disk Access (required for Vencord / Equicord / OpenAsar)

Vencord and Equicord patch inside the signed Discord bundle
(`Discord.app/Contents/Resources/app.asar`). macOS App Management / TCC blocks writes
to signed app bundles from unprivileged processes — PatchCord will be prompted for
permission the first time it attempts to patch.

Grant the permission when the "App Management" dialog appears, or add PatchCord
manually:

> **System Settings → Privacy & Security → Full Disk Access → add PatchCord.app**

This is a one-time grant. BetterDiscord patches files outside the bundle
(`~/Library/Application Support/…`) and does not require Full Disk Access.

### Re-patch after Discord updates

Discord auto-updates restore the vanilla `app.asar`, removing your mod. PatchCord's
monitor loop detects this and re-applies the patch and restarts Discord automatically —
this is the core reason to use PatchCord over running the mod installer once.

### Run at login

Toggle **Run at startup** in the Options tab. PatchCord writes a LaunchAgent plist to
`~/Library/LaunchAgents/com.tomgks.patchcord.plist` so it starts in the menu bar on
every login.

### Notes (macOS)

- osx-arm64 only (Apple Silicon). Intel Macs are not supported.
- Avalonia is the first (and only) NuGet dependency; it lives only in the macOS
  project. The Windows shell and Core remain zero-NuGet.
- The Windows `publish.ps1` and its output are completely unaffected by the macOS port.
- If you just want a modded Discord without the auto-re-patch loop, the official
  [Vencord](https://github.com/Vencord/Installer),
  [Equicord](https://github.com/Equicord/Installer), and
  [BetterDiscord](https://betterdiscord.app) installers for macOS are the
  simpler alternative.

---

## Notes

- Windows only (for the WPF/WinForms shell — see macOS section above for the native port).
- Idle CPU is near zero; resident memory is the usual WPF range (~120-210 MB).
- `config.json` and `patchcord.log` are written next to the exe (not tracked in git).

## Credits

The asar patch and OpenAsar logic are ported from the
[Vencord Installer](https://github.com/Vencord/Installer); the BetterDiscord injection
from BetterDiscord's `scripts/inject.ts`. PatchCord is not affiliated with Vencord,
Equicord, BetterDiscord, or OpenAsar. OpenAsar is by GooseMod (AGPL-3.0); use it at
your own risk.

GPL-3.0. See [LICENSE](LICENSE).
