# HANDOFF — macOS UI polish (Phase U)

**Status as of 2026-06-19 (~1am): U1–U3 DONE, U4–U6 remain. Branch `feat/macos-port`, local commits only (NOT pushed, by user's standing preference).**

Goal: polish the macOS Avalonia shell (`src/PatchCord.Mac/*`) to look clean and true to the
Windows 11 reference (`docs/status.png`, `docs/options.png`). POLISH not overhaul — keep the
two-tab structure + imperative code-behind/VM. Mac shell ONLY (no Core / Windows shell / publish.ps1).

## Ground truth
- Spec: `docs/spec/mac-ui-polish.md` (checkpoint table U1–U6 + Change log = current truth).
- Design: `docs/design/mac-ui-polish.md` (§6 styling, §7 title bar, §5b blank-until-refresh root cause).
- Reference look: `docs/status.png`, `docs/options.png` (Discord palette).

## Done (committed, verified)
- **U1 + U1.1 + Discord default** — commit `e2882d0`. New `src/PatchCord.Mac/Styles.axaml`: 14
  `DynamicResource` palette brushes + keyed `ControlTheme`s (AccentPill, OnPill, GhostPill, WarnPill,
  ModButton, TabButton, SmallToggle, CheckButton) with `:pointerover`/`:pressed`. `ApplyPalette()`
  swaps brush *values* on theme switch; monitoring toggle swaps `Theme` (AccentPill↔OnPill); tabs use
  a `.active` class. Space Grotesk bundled (`Assets/*.ttf`). Mac defaults to the **Discord** palette
  on first run (MacAppState first-run only; Core's "Dark" default untouched).
- **U2 + U3** — commit `538ac40`. Removed the 50px Row-0 header overlay that collided with the native
  macOS traffic lights; wordmark now in a content-area row (`Margin="24,20,0,4"`), `Title="PatchCord"`.
  Section labels/descriptions use `{DynamicResource Sub/Text}`.
- Each verified: `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3), Core flag-free,
  `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (U1 PASS 14/14, all 4 themes cycle).

## Remaining (resume here)
- **U4 — blank-until-refresh + responsive toggles (the behavioral fix; also follow-up 001).**
  Root cause (design §5b, CONFIRMED): the UI is built before the window is shown/laid out
  (`App.axaml.cs` ~44-46 → pre-show `BuildInstallRows`). Fix = populate from a window-realized hook
  (`Show()` before `Initialize()`, or move first `BuildInstallRows`/`UpdateStatusUi`/`UpdateOptionsUi`/
  `SwitchTab` into `MainWindow.Opened`/`Loaded`, or a `Dispatcher.UIThread.Post`). Delete the dead
  `RefreshState()` in `InstallRowViewModel.cs`. Keep `RebuildInstallRows()` as the populate path.
  CAUTION: `--mac-uitest`'s `ReportUiTestInfo` runs at the end of `Initialize()` — make sure the
  lifecycle change doesn't make uitest read state before population (keep uitest green; U6.1 will
  assert first-paint rows>0).
- **U5 — InstallRow / badges.** Apply the U1 ghost/green-pill ControlThemes to the row buttons; badges
  use the `On` brush; row divider uses `Border`. FOLD IN the two Iter-A review nits: `RowRemove`
  (the X dismiss button) is still Fluent-default → give it a Theme; hardcoded badge hex in
  `InstallRow.axaml.cs` (`#80848E` neutral/"other-mod", `#F23F43` error) → pull to palette/named
  resources (error red may stay a fixed semantic color, but not a magic literal).
- **U6 — uitest asserts + manual gate.** U6.1: extend `--mac-uitest` to assert rows populate at first
  paint with NO Refresh. U6.2: assert representative controls/labels resolve a palette brush (read
  back `BtnToggle.Background`/a label `.Foreground`). **U6.3 = the one human step:** user launches
  `PatchCord.app` and eyeballs Status+Options vs the reference screenshots (wordmark clear of traffic
  lights, gray labels, green pills, selected mod-card border, hover/press, all 4 themes).

## How to resume
1. Re-read `docs/spec/mac-ui-polish.md` (U4–U6 rows) + this file + design §5b.
2. Orchestration scratchpad (gitignored, may be stale): `.claude/.scratchpad/2026-06-19-mac-ui-polish/`.
   Workflow used: dispatch `ax-builder` per iteration → orchestrator re-verifies the build+uitest →
   `ax-reviewer` on the diff → gate on CONFIDENCE → commit. Iterations are SEQUENTIAL (they all touch
   `MainWindow.axaml`/`.axaml.cs`); don't parallelize.
3. Dispatch Iter C = **U4 + U5** (one ax-builder, Mac shell only). Then Iter D = **U6** asserts.
4. After U6.1/U6.2 green, hand U6.3 to the user (build `./publish-mac.sh` → launch → visual check).

## Verification reality
A true visual sign-off needs the user — headless `screencapture` is blocked by macOS Screen-Recording
TCC for the terminal. So lean on build-green + `--mac-uitest` structural/property-readback asserts +
the U6.3 manual pass.

## Test entrypoint
`dotnet run --project src/PatchCord.Mac -- --mac-uitest` — headless; prints `[uitest]` structure,
exercises tray/alert, cycles all 4 themes, asserts U1 (14/14 brushes + toggle Theme + font). Exit 0 = pass.

_Note: Phase B5 (Layer-B/OpenAsar/monitor) is fully done on this same branch; see
`docs/spec/run-on-mac.md`. The whole `feat/macos-port` branch is local-only / unpushed._
