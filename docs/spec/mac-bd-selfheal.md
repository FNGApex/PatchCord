# Spec — BetterDiscord Self-Heal ("Fix it") + Bare-Layout Fix (Phase F)

Branch: `feat/macos-port`. Background: `docs/design/mac-permissions.md` + memory `mac-discord-bare-layout`.
**This change log = truth.**

## Why
Discord's NEW macOS/Linux updater (rolling out 2026) loads the Windows-style WRAPPED
`…/discord/app-<X.Y.Z>/modules/discord_desktop_core-N/discord_desktop_core/index.js` — **a
load-marker test proved Discord executes the `app-` copy**, even when a legacy `bare X.Y.Z` folder
also exists with a newer `core.asar`. PatchCord's original `GetLatestCoreAppDir` globbed `app-*` but
mis-resolved on mixed machines (and a first fix wrongly preferred bare by mtime) → BD never managed
correctly (and earlier, a 20s Stop/Inject/Start kill-loop). BBD's own installer/CLI broke on the
same change, leaving BD **malformed** (asar present but the live `app-` file not injected). PatchCord
should detect that and offer a one-click **Fix it** that injects the live folder and self-heals a
missing asar, then keep it patched via the monitor. See memory `mac-discord-bare-layout`.

Verified manually 2026-06-20: injecting the live `app-` folder → BD loads.

## Checkpoints

| ID | Title | Done? |
|---|---|---|
| F1 | `GetLatestCoreAppDir` — accept `app-X.Y.Z` + bare `X.Y.Z`; **prefer `app-`** (live, new updater) | ✅ test F1/F1b |
| F2 | `BetterDiscordEngine.DownloadAsar` — fetch latest asar from official GitHub release | ✅ (network path; not unit-exercised) |
| F3 | `MacAppState.IsBdMalformed` / `FixBetterDiscord` — detection + fix action | ✅ test F3 |
| F4 | Mod-missing banner excludes `betterdiscord` (BD now handled by Fix-it path) | ✅ |
| F5 | Status-tab "Fix it" banner (axaml `BdFixWarn`/`BtnFixBd` + VM props + wiring) | ✅ |
| F6 | Headless test `--mac-bdfix` — dual-layout resolution + detection + inject round-trip | ✅ 4/4 |
| F7 | Verify: sln builds; `--mac-fdatest` 4/4; `--mac-bdfix` 4/4 (`--mac-selftest` deferred¹) | ✅ |

¹ `--mac-selftest` skipped this turn — it stops/starts Discord and the user has a live BD session;
run it when convenient (the change doesn't touch the B2 paths it covers).

**Status: code-complete (2026-06-19). Build 0/0; bdfix 4/4; fdatest 4/4. Awaiting user live-test of the GUI Fix-it banner.**

### F1 — layout resolution (MacDiscordPlatform.cs ~178)
- Replace the `GetDirectories("app-*")` glob: enumerate all top-level dirs whose name parses as a
  version after optionally stripping an `app-` prefix (`^(app-)?\d+\.\d+\.\d+$`), that contain a
  `modules/` subdir **and** for which `BetterDiscordEngine.FindCoreIndexJs(dir)` resolves non-null.
- Order by parsed `Version` desc; tie-break (same version, e.g. `app-0.0.395` vs legacy `0.0.395`)
  by **preferring the `app-` prefix** — the folder Discord's new updater loads (load-marker proven).
  Do NOT use `core.asar` mtime (the legacy bare copy carries a newer-but-ignored decoy core.asar).
- `FindCoreIndexJs` already resolves both wrapped + bare layouts; only top-dir selection changes.

### F2 — self-heal download (BetterDiscordEngine.cs)
- `public static void DownloadAsar(string asarPath)`: GET
  `https://github.com/BetterDiscord/BetterDiscord/releases/latest/download/betterdiscord.asar`
  (static `HttpClient`, UA "PatchCord", 30s timeout — mirror OpenAsarEngine); throw on empty;
  `Directory.CreateDirectory` the parent; write bytes; `Log.Write`. Add `using System.Net.Http;`.

### F3 — detection + fix (MacAppState.cs)
- `IsBdMalformed(Install inst)`: false unless `inst.ClientMod=="betterdiscord"`. Resolve
  `appDir=Platform.ResolveCoreAppDir(inst)` (null → false, can't classify). `asar=Platform.
  BetterDiscordAsarPath`. Healthy iff `BetterDiscordEngine.IsInjected(appDir) && File.Exists(asar)`;
  malformed = not healthy.
- `FixBetterDiscord(Install inst)`: resolve appDir (throw if null). If `!File.Exists(asar)` →
  `BetterDiscordEngine.DownloadAsar(asar)`. `wasRunning=Platform.IsRunning`; if so `Stop`; then
  `BetterDiscordEngine.Inject(appDir, asar)`; if `wasRunning` `Start`. Log each step.

### F4 — mod-missing scope (MainViewModel.cs ~109–149)
- Change the three `i.ClientMod != "none"` filters to `i.ClientMod is "vencord" or "equicord"` so the
  generic "Get <mod>" external-link CTA no longer fires for BetterDiscord (the Fix-it banner owns BD).

### F5 — Fix-it banner (MainWindow.axaml + .axaml.cs + MainViewModel)
- VM: `BdFixVisible` = any enabled install with `IsBdMalformed`. `BdFixText` = explains BD looks
  broken from the installer and Fix it will repair + keep it patched. `FirstBdFixInstall()` helper.
- axaml: a second `Border x:Name="BdFixWarn"` (mirror `ModWarn`, reuse `WarnPill`) directly under
  `ModWarn`, with `BdFixText` + `BtnFixBd` (content "Fix it").
- `UpdateStatusUi`: set `BdFixWarn.IsVisible=_vm.BdFixVisible` + text. Click handler in `Initialize`:
  run `MacAppState.FixBetterDiscord(first)` on a background thread (it Stops/Starts Discord +
  downloads), then on the UI thread enable monitoring (`MonitoringEnabled=true`, restart timer —
  "keep patching"), `MacAppState.Save()`, alert, `UpdateStatusUi`/`BuildInstallRows`. Guard against
  double-clicks; surface download/inject errors via `MacAlert`.

### F6 — headless test `--mac-bdfix` (Program.cs, new branch)
Using the `PATCHCORD_APPSUPPORT_ROOT` seam, build a /tmp fixture with BOTH
`discord/0.0.395/modules/discord_desktop_core/{index.js=vanilla, core.asar=newest}` and
`discord/app-0.0.395/modules/discord_desktop_core-1/discord_desktop_core/{index.js, core.asar=older}`:
- Assert `ResolveCoreAppDir` returns the **bare** `0.0.395` dir (F1 tie-break).
- Assert `FindCoreIndexJs` → the bare index.js; `IsInjected==false` (malformed when vanilla).
- `Inject(bareDir, fakeAsar)` → `IsInjected==true` + file has `require(...betterdiscord.asar)`;
  `Restore` → vanilla. (No network: use a fake asar file; `DownloadAsar` is covered by an opt-in flag.)

### F7 — verification (headless; GUI banner user-tested)
`dotnet build PatchCord.sln` 0/0; `--mac-selftest` 8/8; `--mac-fdatest` 4/4; `--mac-bdfix` pass.
**User live-test:** with a malformed BD (BBD installed wrong), launch PatchCord → Fix-it banner →
click → BD loads in Discord → monitoring stays on and keeps it patched across a Discord update.
</content>
