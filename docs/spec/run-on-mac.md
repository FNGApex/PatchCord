# Spec — Running PatchCord on a Mac

Status: IMPLEMENTED (branch feat/macos-port) · Date: 2026-06-18

> Path A and Path B (B1–B4) are implemented and verified on macOS 26.5 / Apple Silicon. One item is
> verified mechanism-only and deferred for live confirmation: the real Vencord/Equicord bundle PATCH
> (Layer A) needs a one-time App Management / Full Disk Access grant to the packaged PatchCord.app — the
> swap is proven byte-identical on a copy; "Discord loads a real mod" awaits a real mod install + the
> app's first-patch grant. BetterDiscord (Layer B) is unaffected. See the Change log.

Scope note: this spec carries **two paths**. Path A (build/develop on a Mac) is ready to
implement now. Path B (genuine macOS port) is a large fork and is gated behind explicit
approval — do not start it without a "yes" to the open question about intent. The design
rationale and rejected options live in `docs/design/run-on-mac.md`.

---

## Path A — Develop / build PatchCord on a Mac (recommended, ready now)

Goal: a developer on macOS can `dotnet build` the project for editing/CI, understanding the
binary runs only on Windows. No behavior change to the shipped app.

| # | Checkpoint | How to verify | Done when |
|---|---|---|---|
| A1 | Add `EnableWindowsTargeting` so macOS builds resolve the Windows reference packs | Inspect `src/PatchCord.csproj` PropertyGroup | csproj contains `<EnableWindowsTargeting>true</EnableWindowsTargeting>` |
| A2 | `dotnet build` succeeds on macOS | Run `dotnet build src/PatchCord.csproj` from repo root on macOS | Build prints `Build succeeded` with 0 errors |
| A3 | Document the macOS-build caveat | Read `README.md` Build/Notes section | README states macOS can build but the binary runs only on Windows; runnable artifacts still come from a Windows `publish.ps1` run |
| A4 | No regression to the Windows publish path | Inspect `publish.ps1` | `publish.ps1` is unchanged; default RID still `win-x64`; a Windows publish still produces a runnable exe |
| A5 | (Optional) cross-platform `dotnet publish` for CI is documented, not run on mac for release | Inspect README/CI note | Note exists that a *runnable* macOS publish is impossible for this TFM; macOS publish is compile-check only |

Implementation note for A1: the single property is enough; `dotnet build
src/PatchCord.csproj -p:EnableWindowsTargeting=true` was confirmed to succeed on this Mac
(`bin/Debug/net10.0-windows/PatchCord.dll`). Baking it into the csproj makes the default
`dotnet build` work without the extra flag.

Out of scope for Path A: making the binary *run* on macOS (impossible for WPF/WinForms),
any change to Discord-patching behavior.

---

## Path B — Genuine macOS port (large fork; APPROVAL-GATED — do not start without sign-off)

Goal: a natively-runnable macOS app that keeps the **Mac's** Discord
(`/Applications/Discord.app/Contents/Resources/app.asar`) patched with the chosen mod and
re-patches after Discord auto-updates.

Precondition: approved 2026-06-18 — "yes, port it." Locked decisions:
- **UI framework: Avalonia** (XAML/MVVM, native macOS menu-bar support).
- **Target arch: osx-arm64 only** (Apple Silicon).
- **B2.6 resolved first** as a pre-port de-risking spike (phase B0 below) — the full port
  does not start until the signing/permissions question is answered.

Reuse map (carry over UNCHANGED — verified against the official installers, design §8):
`PatchEngine.BuildStubAsar` (byte-for-byte matches Vencord `WriteAppAsar`), the `app.asar`↔`_app.asar`
swap (`Patch`/`Unpatch`/`DetectMod`), the entire `BetterDiscordEngine` (`index.js` rewrite — same shape
and layout on macOS), `OpenAsarEngine` download/cache, `Config.cs` data model + JSON, byte-scan
detectors, and `MainWindow.InvokeMonitor` reconciliation logic. **Replace:** UI (WPF/WinForms→Avalonia),
run-at-login (`Startup.cs` COM→LaunchAgent), and the platform calls (discover/resolve/process/restart)
which move behind `IDiscordPlatform`.

Verified macOS engine facts (design §8 has the evidence trail):
- **Layer A (Vencord/Equicord)** patches the BUNDLE's `…/Contents/Resources/app.asar` (swap to
  `_app.asar`), NOT the App-Support `core.asar`. (Corrects the B0 spike — confirmed by Vencord's
  `find_discord_darwin.go` + `patcher.go`.)
- **Layer A writes require a one-time Full Disk Access grant (CORRECTION — design §15).** Although the
  bundle `app.asar` is user-owned `-rw-r--r--`, macOS TCC "App Management" / `com.apple.provenance`
  blocks writes inside the signed+notarized Discord.app from an unprivileged process (`EPERM`, verified
  on Tahoe 26.5). The fix matches the official Vencord installer: the user grants the PatchCord app
  **Full Disk Access** once (System Settings → Privacy & Security). Layer B (BetterDiscord, App-Support)
  is OUTSIDE the bundle and unaffected.
- **Layer B (BetterDiscord)** patches `~/Library/Application Support/<branch>/app-<ver>/modules/discord_desktop_core-N/discord_desktop_core/index.js`
  — a different file in a different root. (Confirmed by BD `scripts/inject.ts` + the local install.)
- The two layers live in **separate roots** on macOS, so the abstraction resolves them independently.
- Process name `Discord` (PTB/Canary executables carry a space: `Discord PTB`); restart via `open -a`.

### Phase B0 — SIP / code-signing spike ✅ DONE (2026-06-18)
Result: **no SIP / sudo / re-signing blocker.** Both patch targets are user-owned and writable without
elevation; modifying them does not break Discord's launch. See design §7 (spike) and §8 (the re-plan
correction: the asar layer targets the bundle `app.asar`, not the App-Support `core.asar`).
| # | Checkpoint | Result | Status |
|---|---|---|---|
| B0.1 | Bundle/asar ownership & perms | `/Applications/Discord.app` + bundle `app.asar` user-owned, `-rw-r--r--`, no sudo | ✅ |
| B0.2 | Signature/Gatekeeper impact | Bundle not quarantined; no per-launch Developer-ID re-verification; swap is safe | ✅ |
| B0.3 | macOS layout vs Windows | Same `app-*` versioning, App-Support rooted; Layer A=bundle Resources, Layer B=App-Support core | ✅ |
| B0.4 | Decision record | Design §7+§8 + this spec updated; asar layer targets the bundle `app.asar` | ✅ |

### Phase B1 — Portable core split + platform abstraction (do FIRST; reversible, Windows-green)
Goal: extract a `net10.0` core lib and an `IDiscordPlatform` seam **without changing Windows behavior
or the Windows publish**. (Design §9, §10, §14.)

| # | Checkpoint | Verify | Done when |
|---|---|---|---|
| B1.1 | Create `src/PatchCord.Core/PatchCord.Core.csproj` (TFM `net10.0`, no `UseWPF`/`UseWindowsForms`, zero NuGet) | `cat` the csproj | csproj has `<TargetFramework>net10.0</TargetFramework>`, no Windows/UI props, no PackageReference |
| B1.2 | Move portable logic into Core: `PatchEngine` (BuildStubAsar/Patch/Unpatch/DetectMod/ContainsAscii), all of `BetterDiscordEngine`, `OpenAsarEngine`, `Config`/`Install`/`PatchEvent`/`UiConfig`/`AppConfig`, `Log`. Replace `App.BaseDir`/`App.*Path` static refs inside these with injected paths/parameters | Build Core | `dotnet build src/PatchCord.Core/PatchCord.Core.csproj` succeeds on macOS WITHOUT `-p:EnableWindowsTargeting=true` (proves zero Windows dep) |
| B1.3 | Define `IDiscordPlatform` in Core with the surface in design §10 (DiscoverInstalls, LooksLikeInstall, ResolveResourcesDir, ResolveCoreAppDir, AppVersionLabel, IsRunning, IsUpdateInProgress, Stop, Start, the three mod-path props, RunAtLogin{Enabled,Set}, TryAcquireSingleInstance) | Code review vs §10 table | Interface compiles in Core; every member is documented; no member references a Windows type |
| B1.4 | Add `WindowsDiscordPlatform : IDiscordPlatform` in the Windows shell wrapping today's behavior: `DiscoverInstalls`→`FindStandardInstalls`, `Resolve*`→`GetLatestAppDir`, `IsRunning`/`Stop`/`Start`→current `PatchEngine` process calls + `Update.exe`, paths→`App.xaml.cs:71-79`, RunAtLogin→`Startup.cs`, single-instance→`Global\` mutex | Code review; map each §10 row | Every Windows call site in design §10 routes through the impl; no direct `Process`/`Update.exe`/`Startup` calls remain in `MainWindow.InvokeMonitor` |
| B1.5 | Repoint `src/PatchCord.csproj` (Windows shell) to `<ProjectReference>` Core; refactor `MainWindow.InvokeMonitor`/`GetState` to call `IDiscordPlatform` | Build Windows shell | `dotnet build src/PatchCord.csproj -p:EnableWindowsTargeting=true` succeeds on macOS with 0 errors |
| B1.6 | Add `PatchCord.sln` tying Core + Windows shell (Mac project added in B3) | `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` | Solution builds |
| B1.7 | **Windows publish stays green** — `publish.ps1` unchanged | Inspect `publish.ps1`; (on Windows) run it | `publish.ps1` still references `src\PatchCord.csproj`, default RID `win-x64`, and a Windows publish produces the same single-file self-contained `PatchCord.exe`; `-selftest` passes |

### Phase B2 — macOS `IDiscordPlatform` implementation (`MacDiscordPlatform`)
Goal: a Core-consuming mac platform impl, testable headless before any UI. (Design §8.)

| # | Checkpoint | Verify | Done when |
|---|---|---|---|
| B2.1 | `DiscoverInstalls` / `LooksLikeInstall` — scan `/Applications` + `~/Applications` for `Discord.app`, `Discord PTB.app`, `Discord Canary.app`, `Discord Development.app`; map to branch | Run on this Mac (stable present) | Returns stable Discord with its bundle path; branch mapping matches design §8.4 |
| B2.2 | `ResolveResourcesDir` (Layer A) → `<bundle>/Contents/Resources`; verify `app.asar` present, `_app.asar` = patched marker | Inspect `/Applications/Discord.app/Contents/Resources` | Resolver returns the dir containing the bundle `app.asar`; a `Patch`/`Unpatch` round-trip on a COPY swaps `app.asar`↔`_app.asar` correctly |
| B2.3 | `ResolveCoreAppDir` (Layer B) → highest `~/Library/Application Support/<branch>/app-<ver>` containing `modules/`; `BetterDiscordEngine.FindCoreIndexJs` finds `…/discord_desktop_core-N/discord_desktop_core/index.js` | Run against local `app-0.0.395` | Returns `app-0.0.395`; `FindCoreIndexJs` resolves the real `index.js` (currently vanilla) |
| B2.4 | `IsRunning` / `Stop` — process name per branch (`Discord`, `Discord PTB`, …); `Process.Kill` + poll | Manual: launch Discord, call Stop | Running detected; process terminated; poll confirms exit |
| B2.5 | `Start` — `Process.Start("open", "-a \"<bundle name>\"")` (or `-b com.hnc.Discord`) | Manual after a patch | Discord relaunches; if patched, launches modded |
| B2.6 | `IsUpdateInProgress` — real `ShipIt` probe (design §8.5): true when a `ShipIt` process runs OR `ShipIt_request.json` in the branch App-Support dir was modified within a short recency window | Manual: trigger/simulate an update; inspect `ShipIt_request.json` mtime | Returns true while a `ShipIt` update is in flight (or `ShipIt_request.json` is fresh), false otherwise; monitor defers the patch while true; next-tick convergence still backstops |
| B2.7 | Mod-data path props → `~/Library/Application Support/Vencord/dist/patcher.js`, `…/Equicord/dist/patcher.js`, `…/BetterDiscord/data/betterdiscord.asar` (honor `VENCORD_USER_DATA_DIR`/`EQUICORD_USER_DATA_DIR`) | Inspect resolved paths | Paths match the official installers' locations (design §8.2, §8.1) |
| B2.8 | End-to-end headless patch: with Vencord installed, drive Core+`MacDiscordPlatform` to stop→swap bundle `app.asar`→start; then unpatch | Manual on a real install (or a copied bundle) | Discord runs Vencord after patch; clean revert after unpatch; no signature/launch breakage |

### Phase B3 — Avalonia UI, tray, lifecycle
Goal: a runnable mac app reusing Core + `MacDiscordPlatform`. (Design §11, §12.)

| # | Checkpoint | Verify | Done when |
|---|---|---|---|
| B3.1 | Create `src/PatchCord.Mac/PatchCord.Mac.csproj` (TFM `net10.0`, Avalonia PackageReferences, `osx-arm64`); add to `PatchCord.sln` | `dotnet build src/PatchCord.Mac` on macOS | Project builds; Avalonia is the only NuGet dep; Core + Windows shell still build |
| B3.2 | Port `MainWindow` (status + options tabs) to an Avalonia `Window`/`MainViewModel`; port `InstallRow` UserControl | Run the app | Window renders; install rows, per-install mod chooser, monitoring toggle, OpenAsar/startup toggles, interval slider all functional and bound to Core |
| B3.3 | macOS menu-bar item via Avalonia `TrayIcon` + `NativeMenu` with header, pause/resume monitoring, quit | Run the app | Menu-bar (NSStatusItem) appears; actions match the Windows tray (`MainWindow.xaml.cs:183-197`) |
| B3.4 | Port `Alert` banner + `Theme` palette to Avalonia | Trigger a patch event | Top-left banner shows with the configured style/scale/duration and auto-dismisses |
| B3.5 | Monitor loop on Avalonia `DispatcherTimer` driving Core `InvokeMonitor` through `MacDiscordPlatform` | Run with monitoring on | Timer ticks; reconciliation runs; patch history + alerts update |
| B3.6 | Run-at-login via LaunchAgent (`MacDiscordPlatform.SetRunAtLogin`) writing `~/Library/LaunchAgents/com.tomgks.patchcord.plist` + `launchctl bootstrap`/`bootout` | Toggle on; inspect plist; re-login | Plist present with the `--tray` ProgramArguments (design §12); toggle off removes it; enabled-state reflects file presence |
| B3.7 | Single-instance on macOS (`TryAcquireSingleInstance`) via a lock file / named lock (replaces `Global\` mutex) | Launch the app twice | Second launch detects the first and exits/defers |
| B3.8 | Re-patch-after-update on macOS: with a mod applied, restore the bundle `app.asar` to vanilla (simulate an update), wait one interval | Observe | App detects the mod is gone, re-applies, restarts Discord (proves item #4 mapping) |
| B3.9 | **Full Disk Access onboarding (design §15):** `MacDiscordPlatform` surfaces the Layer-A `EPERM` as a typed condition; the UI detects it and shows an explanation + a button deep-linking to `x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles`; re-checks after grant | Run unpatched without FDA, then grant and retry | Without FDA a clear "grant Full Disk Access" prompt shows (no silent failure); after granting FDA, the Layer-A patch succeeds and Discord runs the mod |

### Phase B4 — Packaging
Goal: a double-clickable `.app`. (Design §13.)

| # | Checkpoint | Verify | Done when |
|---|---|---|---|
| B4.1 | `dotnet publish src/PatchCord.Mac -c Release -r osx-arm64 --self-contained true` produces the binary | Run the publish | Publish succeeds; self-contained binary emitted |
| B4.2 | Wrap as `PatchCord.app` with `Info.plist` (`CFBundleIdentifier=com.tomgks.patchcord`, `LSUIElement=true`, icon) + `PatchCord.icns` (from `app.ico`); **ad-hoc code-sign the bundle** (`codesign -s -`) so it has a stable identity for the Full Disk Access list (design §15) | Inspect bundle; double-click; `codesign -dv` | Double-clicking `PatchCord.app` launches it to the menu bar on osx-arm64; bundle is ad-hoc signed |
| B4.3 | Add `publish-mac.sh` (mac analog of `publish.ps1`); keep `publish.ps1` Windows-only/untouched | Inspect both scripts | `publish-mac.sh` builds the `.app`; `publish.ps1` unchanged |
| B4.4 | README documents the macOS build/install + Gatekeeper first-run + the official-installer alternative | Read README | macOS section present; notes Avalonia as the first NuGet dep and that Windows publish is unaffected |

### Path B Out of scope
- iOS/Android/Linux (Avalonia could later, not now).
- osx-x64 / Intel Macs (locked to osx-arm64 only).
- Patching a Discord that lives inside a Windows VM from the macOS app.
- Distribution code-signing/notarization of the `.app` (local build only in v1).

---

## Change log
- 2026-06-18 — **Path B IMPLEMENTED (B1–B4), branch feat/macos-port, 12 commits.** B1 core split +
  IDiscordPlatform (9fb47ab); B2 MacDiscordPlatform (1a59d4d); B3a Avalonia bootstrap (f1a011e); probe
  opt-in (7bd5f2f); B3b MainWindow/InstallRow (811cf82); B3c tray/alert/themes (4ab2c29); B3d shared
  MonitorService (9801ec1); B3e run-at-login/single-instance/FDA onboarding (44ee9f2); B4 packaging +
  publish-mac.sh + README (4507a98). Verified: sln 3/3 green (Windows shell stayed green throughout,
  publish.ps1 untouched), Core builds flag-free, --mac-selftest 8/8 + b36/b37/fda harnesses pass, the
  ad-hoc-signed PatchCord.app launches as a menu-bar agent. Deferred (mechanism-only): live Layer-A
  real-bundle patch + real-mod load — needs a mod install + the app's one-time App-Management/FDA grant.
- 2026-06-18 — B2 done (commit 1a59d4d) + **TCC CORRECTION.** MacDiscordPlatform implemented & green
  (8/8 self-test, process control + ShipIt probe live, swap byte-identical on a copy). Discovered the B0
  spike was WRONG: Layer A bundle writes are blocked by macOS App-Management/`com.apple.provenance` TCC
  (`EPERM`) from an unprivileged process — NOT a POSIX-perms issue. Resolution (matches the official
  Vencord installer): one-time **Full Disk Access** grant. Added FDA requirement to verified-facts,
  B3.9 (FDA onboarding), B4.2 (ad-hoc signing); full detail in design §15. Layer B unaffected. B2.8
  real-bundle write + real-mod load remain unproven until FDA is granted to a signed .app.
- 2026-06-18 — Approval: signed off Path B. Confirmed Layer A patches the bundle `app.asar` in
  place wherever Discord lives (no forced move to `~/Applications`); LaunchAgent via `launchctl`;
  bundle id `com.tomgks.patchcord`; osx-arm64 only (Intel unsupported). **Scope change:** pulled
  the `ShipIt` mid-update probe into v1 (B2.6 now implements a real probe, no longer a `false`
  stub); removed it from Path B out-of-scope. Next: hand B1 to the builder.
- 2026-06-18 — Re-plan: replaced coarse B1–B4 with checkpoint-level tasks (each independently
  verifiable). Resolved all 8 open items in design §8–§14 (project split, `IDiscordPlatform`
  surface + call-site map, verified macOS engine specifics, re-patch loop mapping, Avalonia UI map,
  LaunchAgent, packaging, sequencing). **Corrected the B0/§7 inference:** the Vencord/Equicord
  asar layer patches the BUNDLE's `Contents/Resources/app.asar` (swap to `_app.asar`), NOT the
  App-Support `core.asar` — verified against the official Vencord Installer (`find_discord_darwin.go`,
  `patcher.go`, `app_asar.go`) and BetterDiscord injector (`scripts/inject.ts`). BetterDiscord still
  targets the App-Support core `index.js`. `BuildStubAsar` confirmed byte-identical to Vencord's
  `WriteAppAsar`. New first checkpoint is B1 (portable-core split, Windows-green).
- 2026-06-18 — B0 spike done: no SIP/signing blocker; patch targets are the App-Support
  `app-*` core (outside the signed bundle). Locked Path B decisions: Avalonia, osx-arm64.
  Corrected the design's "app-* is Windows-specific" claim (see design §7).
- 2026-06-18 — Initial spec. Path A (macOS build via `EnableWindowsTargeting`, confirmed
  working) and Path B (approval-gated macOS port, phased). Grounded in source review; see
  `docs/design/run-on-mac.md` for evidence and rejected options.
