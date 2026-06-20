<p align="center"><img src="docs/logo.png" width="104" alt="PatchCord"></p>
<h1 align="center">PatchCord</h1>

Keeps your Discord client mod installed. Discord wipes client mods every time it
auto-updates; PatchCord runs in the menu bar, notices when an install is running
unpatched, re-applies your mod, and restarts Discord.

Works with Vencord, Equicord, and BetterDiscord (pick one in Options). OpenAsar can
be kept installed alongside any of them.

> This README currently documents the native **macOS** build (the `feat/macos-port`
> branch). The Windows docs will be merged back in later.

## How it works

It reproduces what each mod's installer does, then re-does it after an update wipes it:

- Vencord / Equicord: rename `Contents/Resources/app.asar` to `_app.asar` and write a
  small stub `app.asar` that requires the mod's `dist/patcher.js` — which PatchCord
  downloads for you from the mod's GitHub release.
- BetterDiscord: rewrite `discord_desktop_core/index.js` so it requires
  `~/Library/Application Support/BetterDiscord/data/betterdiscord.asar`.

OpenAsar is optional and off by default. When on, it's downloaded from OpenAsar's
GitHub releases (cached locally) and re-applied under whichever mod you use.

## macOS — Testing Guide (Apple Silicon)

PatchCord runs natively on macOS (osx-arm64) and keeps your Mac's Discord patched with
Vencord, Equicord, BetterDiscord, or OpenAsar — re-applying automatically after Discord
auto-updates. This section is for testers building from the `feat/macos-port` branch.

> **Apple Silicon only.** Intel Macs are not supported.

### Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Xcode Command
Line Tools (`xcode-select --install`). From the repo root:

```bash
chmod +x publish-mac.sh
./publish-mac.sh
```

This produces a self-contained, signed `publish/PatchCord.app` (~107 MB) — no separate
.NET install needed. Optional but recommended: run `./make-signing-cert.sh` **once** first
to create a stable signing identity, so the macOS permission grant (below) survives
rebuilds. Without it the app is ad-hoc signed and you'll need to re-grant after each build.

### First launch (Gatekeeper)

The build is self-signed and not notarized, so Gatekeeper blocks the first launch.
Right-click `PatchCord.app` → **Open**, then confirm. After that it opens normally and
lives in the **menu bar** (there is no Dock icon).

### Grant App Management (needed for Vencord / Equicord / OpenAsar)

These three patch *inside* Discord's signed app bundle
(`Discord.app/Contents/Resources/app.asar`). macOS gates writes to a signed bundle behind
the **App Management** permission, so the first patch attempt fails and PatchCord shows an
onboarding dialog. Grant it once:

> **System Settings → Privacy & Security → App Management → enable PatchCord**

There is no in-app "Allow" popup for this — macOS silently blocks the write and posts a
notification — so you must enable it in System Settings, then click **Re-check / retry** in
PatchCord. The grant is one-time and (with a stable signing identity) persists across
rebuilds. **BetterDiscord needs no permission** — it patches outside the bundle.

### What to test

1. **Vencord / Equicord** — pick one in Options or the per-install row. PatchCord
   **downloads the mod's `dist` itself** (you do *not* need the official installer), then
   patches and restarts Discord. Confirm the **Vencord/Equicord** section appears in
   Discord → User Settings.
2. **OpenAsar** — toggle it on. Confirm the **Build Override** field shows up in Discord's
   debugging info (Settings → copy version) — that field is OpenAsar's. It coexists with any
   client mod.
3. **BetterDiscord** — pick it (no grant needed). If its installer targeted the wrong folder
   for your Discord build, PatchCord shows a **Fix it** button — click it to repair and inject
   the live folder.
4. **Switching mods** — change the selected mod. A confirm box appears; on confirm PatchCord
   removes the current mod, installs the new one, and restarts Discord.
5. **No client mod** — select it to uninstall the current mod (restores vanilla Discord).
6. **Re-patch after an update** — let Discord auto-update (or quit and relaunch it); the
   monitor should re-apply your mod and restart Discord. This is the core feature.
7. **Run at login** — toggle **Run at startup**; PatchCord writes a LaunchAgent
   (`~/Library/LaunchAgents/com.tomgks.patchcord.plist`) so it starts in the menu bar each login.

### Reporting issues

Use **Copy diagnostics** in the Options tab and paste it into your report. The log is at
`~/Library/Application Support/PatchCord/patchcord.log`; config at
`~/Library/Application Support/PatchCord/config.json`.

### Notes (macOS)

- The App Management grant is tied to your local build's signing identity, so **each tester
  grants it once** on their own machine.
- Avalonia is the only NuGet dependency and lives only in the macOS project; the Windows
  shell and Core stay zero-NuGet.
- If you only want a modded Discord without the auto-re-patch loop, the official
  [Vencord](https://github.com/Vencord/Installer),
  [Equicord](https://github.com/Equicord/Installer), and
  [BetterDiscord](https://betterdiscord.app) macOS installers are the simpler route.

## Credits

The asar patch and OpenAsar logic are ported from the
[Vencord Installer](https://github.com/Vencord/Installer); the BetterDiscord injection
from BetterDiscord's `scripts/inject.ts`. PatchCord is not affiliated with Vencord,
Equicord, BetterDiscord, or OpenAsar. OpenAsar is by GooseMod (AGPL-3.0); use it at
your own risk.

GPL-3.0. See [LICENSE](LICENSE).
