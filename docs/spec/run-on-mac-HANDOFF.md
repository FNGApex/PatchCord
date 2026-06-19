# HANDOFF — Run-on-Mac (Path B port)

**Status: IMPLEMENTED (B1–B4) on branch `feat/macos-port`, 2026-06-18 — 12 commits, all green.**
Path A + Path B are built and verified on macOS 26.5 / Apple Silicon. The Windows shell + publish.ps1
were never regressed. Spec checkpoint tables in `docs/spec/run-on-mac.md` are all met; the Change log
lists the per-phase commits.

**The one deferred item (mechanism-only, by user choice):** the live Layer-A real-bundle Vencord/Equicord
PATCH + real-mod load. macOS App-Management TCC blocks bundle writes from an unprivileged process; the
packaged PatchCord.app needs a one-time App-Management / Full Disk Access grant (its FDA onboarding guides
this). The swap is proven byte-identical on a copy; full live confirmation awaits a real mod install +
granting the .app. BetterDiscord (Layer B, App-Support) is unaffected.

**To finish/ship:** install Vencord (official installer), `./publish-mac.sh`, launch `publish/PatchCord.app`,
grant it App Management when prompted, and confirm a real patch+restart. Then merge `feat/macos-port`.

Approval decisions (2026-06-18): patch the bundle `app.asar` in place wherever Discord lives
(no forced `~/Applications` move); ShipIt mid-update probe IS in v1 scope (B2.6); LaunchAgent
via `launchctl`; bundle id `com.tomgks.patchcord`; osx-arm64 only.

**Key spike correction (don't miss):** Layer A (Vencord/Equicord) patches the BUNDLE's
`Contents/Resources/app.asar` (swap to `_app.asar`), NOT the App-Support `core.asar` the B0
spike assumed. Layer B (BetterDiscord) still targets the App-Support `index.js`. Verified
against the official Vencord Installer + BD injector — see design §8.

---

## Original re-plan brief (now COMPLETE — kept for reference)

---

## Decisions already locked (do not re-litigate)
- **Goal:** Path B — a native macOS app that patches the **Mac's own** Discord and re-patches
  after auto-updates. (Not "just build on Mac"; not VM/Wine/cloud — those were rejected.)
- **UI framework:** Avalonia (XAML/MVVM, native menu-bar/tray). MAUI rejected.
- **Target arch:** osx-arm64 only (Apple Silicon).
- **Patch target on macOS:** the App-Support `app-*` core, NOT the signed bundle.

## B0 spike findings (verified on a real install — high confidence)
Tested `/Applications/Discord.app`, Discord 0.0.395, Apple Silicon, SIP enabled.
- **No signing / SIP / sudo blocker.** The mod-injection targets live OUTSIDE the code-signed
  `.app` bundle, in the user's home and writable without elevation:
  `~/Library/Application Support/discord/app-<ver>/modules/discord_desktop_core-1/discord_desktop_core/`
  → `index.js` (BetterDiscord require-line target) + `core.asar` (Vencord/Equicord layer target).
  Both `-rw-r--r--`, user-owned. SIP doesn't cover `~/Library`. Patching them can't break
  Discord's signature or trip Gatekeeper.
- The bundle `Contents/Resources/app.asar` is only the **bootstrap** (`bundle.js`) — a red
  herring for patching; ignore it as a target.
- **macOS has the SAME `app-*` versioning model as Windows**, just rooted at
  `~/Library/Application Support/discord/` (module dirs carry a `-1` suffix). The plan's claim
  that "`app-*` versioning is Windows-specific" was WRONG — corrected in design §7. The Windows
  `PatchEngine` discovery ("find latest `app-*` → locate core") ports as **path-rebasing, not a
  rewrite**.

## Current understanding of port scope (to be sharpened in re-plan)
- **Reuses largely as-is** (already platform-neutral .NET): asar byte-format writer
  (`PatchEngine.BuildStubAsar`), OpenAsar download/cache (`OpenAsarEngine`), JSON config model
  (`Config.cs` data classes), byte-scan mod detectors.
- **Path-rebasing, not rewrite** (Windows→macOS rooting): `app-*` discovery, the BetterDiscord
  `index.js` rewrite (`BetterDiscordEngine`), mod-data paths (`%APPDATA%\Vencord` →
  `~/Library/Application Support/Vencord`).
- **Genuine rewrites:** UI (WPF/WinForms → Avalonia), tray (WinForms NotifyIcon → NSStatusItem
  via Avalonia), run-at-login (`Startup.cs` Win32 COM → LaunchAgent plist), Discord restart
  (`Update.exe --processStart` → `open -a Discord`), single-instance (Win `Global\` mutex → mac
  equivalent).

## What re-planning should produce (the brief for the next /ax-plan)
Flesh out a concrete, checkpoint-level B1–B4 plan, resolving the items below. Output: refreshed
`docs/spec/run-on-mac.md` Path B (replace the current coarse phases with detailed checkpoints)
and any design deltas.

1. **Project structure:** how to split a `net10.0` portable core lib from the Windows shell and
   the new macOS (Avalonia, osx-arm64) shell. One solution, multiple projects? Confirm the
   Windows build/publish stays green throughout (it's the only shipping target today).
2. **`IDiscordPlatform` abstraction:** exact surface (discover installs, resolve core dir/asar,
   is-running, stop, start, run-at-login). Map each current Windows call site to it.
3. **macOS engine specifics, grounded in the spike layout:**
   - Confirm whether the Vencord/Equicord layer on macOS patches `core.asar` (App-Support) and
     how that differs from the Windows `app.asar ↔ _app.asar` swap. **Verify against the actual
     Vencord/Equicord/BetterDiscord macOS installer behavior** before designing the swap.
   - Multi-branch discovery: `Discord` / `DiscordPTB` / `DiscordCanary` app-support dir names on
     macOS (only stable `discord` was present on this machine — confirm the others' naming).
   - Detect-running / stop / restart on macOS (process name, `open -a`, relaunch correctness).
4. **Re-patch-after-update loop:** how the monitor detects a new `app-<ver>` dir on macOS and
   re-applies (the Windows logic should map directly given finding above — confirm).
5. **Avalonia UI port plan:** which views/controls map from the existing WPF
   (MainWindow/InstallRow/Alert/Theme), and the menu-bar item.
6. **LaunchAgent design:** plist contents, install/remove, replacing `Startup.cs`.
7. **Packaging:** `.app` bundle via `dotnet publish -r osx-arm64`, icon, double-click launch.
8. **Risk/sequencing:** recommend the safest first checkpoint (likely B1 portable-core split,
   fully reversible, Windows-green).

## How to resume
1. Re-read this file + design §7 + spec.
2. `/ax-plan` on Opus 4.8 with the brief above; let it verify open items (esp. #3) against the
   real install at `~/Library/Application Support/discord/` and the official installers.
3. Approve the refreshed spec, then `/ax-implement` starting at B1.

_Written 2026-06-18 at end of session, by request. Apex test hooks on the exe:
`--dumpstub --openasar-test --bd-test -selftest` (Windows only)._
