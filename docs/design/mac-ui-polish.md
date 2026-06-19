# Design — macOS (Avalonia) UI polish

Status: draft for approval · Date: 2026-06-19 · Owner: planning · Branch: feat/macos-port

## 1. The task, restated

POLISH the macOS Avalonia shell (`src/PatchCord.Mac/*`) so it reads as a clean port of the
shipped Windows 11 UI. NOT an overhaul: keep the two-tab (Status / Options) structure and the
existing imperative code-behind + lightweight view-model approach. The Windows shell
(`docs/status.png`, `docs/options.png`, `src/MainWindow.xaml`, `src/InstallRow.xaml`,
`src/Theme.cs`) is the design reference. Live theme switching across all 4 palettes must keep
working. Mac shell only — do not touch `PatchCord.Core`, the Windows shell, or `publish.ps1`.

Success criteria:
- Controls are fully styled (idle + hover + pressed) for every variant the app uses — no
  half-styled Fluent defaults bleeding through.
- The "PATCH CORD" wordmark no longer collides with the macOS traffic lights.
- Section labels and description text use the palette (gray `Sub`), matching the reference.
- First paint shows correct install rows + badges and working toggles WITHOUT a manual Refresh.
- `InstallRow` badges / "Managed" pill match the reference.
- Build stays green; `--mac-uitest` still passes and gains polish assertions; one final human
  visual pass.

## 2. Target palette + metrics (extracted from the reference)

The reference is the **Discord** palette in `src/Theme.cs:22-25` plus the literal hex values
baked into `src/MainWindow.xaml` / `src/InstallRow.xaml`. The Mac palette registry
(`MacTheme.cs`) already mirrors all four palettes byte-for-byte — no palette change is needed;
the gap is that controls do not consistently *consume* it.

| Token | Discord palette | Used for |
|---|---|---|
| Bg | `#1E1F22` | window background |
| Card | `#2B2D31` | mod-card idle (`#313338` Card2 for log/activity panels) |
| Border | `#3A3C42` | window border, row divider, chip border |
| Text | `#F2F3F5` | values, install name, active tab |
| Sub | `#B5BAC1` | section labels, descriptions, paths, inactive tab |
| Accent | `#5865F2` | logo dot, CORD wordmark, selected chip/card border, "Turn Off" pill |
| AccentHover | `#4752C4` | accent button hover |
| Ghost / GhostHover | `#383A40` / `#41434A` | ghost buttons (Detect/Add/Refresh), mod button |
| On / OnText | `#23A55A` / `#FFFFFF` | green pill ("Managed", "Turn On", On toggles), green badges |

Metrics lifted from the WPF reference (these are the exact numbers to reproduce):

| Element | Reference (WPF) | file:line |
|---|---|---|
| Window | 900×620, outer `Border Margin=12 CornerRadius=8 BorderThickness=1` + drop shadow | `MainWindow.xaml:4,106-107` |
| Title bar row | height 50; wordmark `Margin=22,0` | `MainWindow.xaml:109-115` |
| Logo dot | 9×9, CornerRadius 2, Accent | `MainWindow.xaml:112` |
| Wordmark | "PATCH" Text 14 Bold; "CORD" 14 Bold Accent, 3px gap | `MainWindow.xaml:113-114` |
| Tab text | FontSize 13 SemiBold; active=Text + 2px underline, inactive=Sub | `MainWindow.xaml:123-129` |
| Section label | FontSize 12, FontWeight Medium, Sub | `MainWindow.xaml:138,160,168` |
| Accent pill ("Turn Off") | H42, CornerRadius 6, Padding 20,0, FontSize 13 SemiBold | `MainWindow.xaml:23-34` |
| Ghost button | H38, CornerRadius 8, Padding 16,0, 1px Border, FontSize 12 SemiBold | `MainWindow.xaml:36-48` |
| Mod card (Options) | CornerRadius 6, Padding 16,12; selected = Accent 1px+ border, GhostHover bg | reference image + `MainWindow.xaml.cs` (Win) |
| Chip (theme/style) | CornerRadius 6, Padding 15,8; selected = Accent 2px border | `MainWindow.axaml.cs:456-479` (already close) |
| Install row | Padding 2,12, 1px bottom divider `#3A3C42`; name 14 SemiBold | `InstallRow.xaml:4-10` |
| Badge | CornerRadius 4, Padding 8,3, FontSize 10 Bold, white text | `InstallRow.xaml:11-16` |
| "Managed" pill | W96 H30, CornerRadius 5, green (On) bg, white text | `InstallRow.xaml:31-35` + image |
| Scrollbar | width 11, rounded thumb, no arrows, Accent on hover | `MainWindow.xaml:58-104` |

### Typography note (gap, not in the current Mac build)
The reference uses **Space Grotesk** (wordmark, body) and **JetBrains Mono** (log panel). The
ttf files already exist in `src/fonts/` (SpaceGrotesk-{Regular,Medium,SemiBold,Bold}, JetBrainsMono-Regular).
`src/PatchCord.Mac/Assets/` ships only `tray-icon.png` — no fonts — so the Mac build renders in
the system default. Porting the wordmark font is a cheap, high-visibility polish win; treated as
an optional sub-task (U1) because it needs the ttf added as an `AvaloniaResource` + a `FontFamily`
reference, not a structural change.

## 3. Diagnosis — confirm / correct each reported item

| # | Reported diagnosis | Verdict | Evidence |
|---|---|---|---|
| 1 | Unstyled controls: `App.axaml` declares only `<FluentTheme/>`; code-behind sets Background/Foreground per-control, but Fluent's button template reasserts brushes on `:pointerover`/`:pressed` + adds default padding/border → half-styled | **CONFIRMED** | `App.axaml:6-8` only `<FluentTheme/>`; every button is a bare `<Button>` with only `CornerRadius`/`FontSize` set in XAML (`MainWindow.axaml:62-66,95-97,131-135`, `InstallRow.axaml:43-59`); code sets `.Background`/`.Foreground` directly (`MainWindow.axaml.cs:237-239`, `InstallRow.axaml.cs:64-70`). Fluent's `Button` ControlTheme has `:pointerover`/`:pressed` setters that win at runtime over a locally-set property of the same priority, and supplies default `Padding`/`CornerRadius`/min-size — so idle looks ~right but hover/press flips to Fluent grey. |
| 2 | Title-bar collision: native traffic lights + custom header at `Margin=22` overlap | **CONFIRMED** | `MainWindow.axaml` sets NONE of `WindowDecorations` / `ExtendClientAreaToDecorationsHint` → default = native macOS title bar with traffic lights at top-left. The header `StackPanel` sits in Grid Row 0 (`height 50`) at `Margin="22,0,0,0"` (`MainWindow.axaml:15-22`) → wordmark renders under the traffic lights. (Windows had `WindowStyle=None` + its own min/close buttons; that whole custom-chrome block was dropped on Mac, but the header inset was not adjusted.) |
| 3 | Uncolored static text: section labels + descriptions have no Foreground and aren't repainted | **CONFIRMED** | All section-label `TextBlock`s (`MainWindow.axaml:51,89,101,117,124,...`) and the description blocks have no `Foreground` and are not touched in `ApplyPalette()` (`MainWindow.axaml.cs:487-500` only repaints `StatusText`/`StatusSub` + the window border). They render in the Fluent default foreground, not the palette `Sub` gray. |
| 4 | Blank-until-refresh (follow-up 001): rows/badges only appear after Refresh; toggles unresponsive until then | **CONFIRMED symptom, ROOT CAUSE corrected — see §5** | The brief's guess "`_vm.Installs` empty at first init" is **WRONG**: the `--mac-uitest` harness reports `Install rows: 1` at first `Initialize()` (§4). Data is present; the failure is a GUI render/lifecycle issue. |
| 5 | InstallRow badge / "Managed" pill styling needs to match reference | **CONFIRMED** | Badges are styled in code (`InstallRow.axaml.cs:29-60`) but the mod button / toggle inherit the same Fluent half-styling as #1 (`InstallRow.axaml:43-59`); the green "Managed" pill currently reads correctly only at idle. |

## 4. Evidence run — the headless harness disproves the "empty list" theory

`dotnet run --project src/PatchCord.Mac -- --mac-uitest` (auto-closes after 2500ms) prints, at
first `Initialize()`:

```
[uitest] Install rows: 1
[uitest]   install: name=Discord enabled=True mod=BetterDiscord badge=Vencord oaSar=False
```

So at first paint: the install IS discovered, the row VM IS built, and the badge text IS
computed. Two consequences:

- The blank-until-refresh bug is NOT a missing-data bug in the model layer. `MainViewModel`'s
  ctor calls `RebuildInstallRows()` (`MainViewModel.cs:19,30-40`) which seeds `Installs` from
  `_cfg.Installs` (populated by first-run discovery in `AppConfig.Load`/`FirstRun`,
  `Config.cs:64-97`, via `MacAppState.EnsureLoaded` passing `Platform.DiscoverInstalls`,
  `MacAppState.cs:55-60`). `BuildInstallRows()` in `Initialize()` then materializes the controls.
- `badge=Vencord` while `mod=BetterDiscord` is NOT itself a bug: `InstallState.InjectedMod`
  reflects what is physically on disk (a leftover Vencord stub: `_app.asar` present + "Vencord"
  bytes), independent of the configured `ClientMod` (`PatchEngine.cs:14-15,93-107`). The badge is
  reporting true disk state. The "wrong/stale badge" the user saw in the GUI is therefore the
  same render-staleness as the blank rows — the view simply wasn't showing the computed state
  until Refresh forced a re-layout.

This is the load-bearing finding: the fix is in the **view/lifecycle**, not the model.

## 5. Root cause of blank-until-refresh (item 4)

The reported "blank until Refresh" conflates **two separate things**; they have different causes.

### 5a. The wrong/stale-badge symptom is (most likely) NOT a bug — badge tracks disk by design

The row badge shows `InstallState.InjectedMod` — what is physically injected on disk (`DetectMod`
reading the bundle's app.asar) — which is **independent by design** of the configured `ClientMod`
shown in the mod dropdown (`InstallRowViewModel.cs:91` badge vs `:58` ModLabel;
`PatchEngine.cs:14-15`). So the harness line `mod=BetterDiscord badge=Vencord` is the legitimate
"config says BetterDiscord, but the disk currently carries a leftover Vencord stub" state, not
staleness. If the user perceives Refresh as "fixing" the badge, it is because a **monitor tick ran
a patch in between** (`MainWindow.axaml.cs:189-199`), changing the disk — not because recompute
changed anything. This is the per-install-mod model documented in CLAUDE.md. **Verdict: not a UI
bug.** (Worth a one-line UI clarification at most; out of scope for a rebuild.)

### 5b. Blank rows + dead toggles: the whole UI is built BEFORE the window is shown/laid out — CHOSEN root cause

Decisive structural facts (strategist pass, all verified in code):
- `RefreshState()` — the only method that re-reads disk into an *existing* row VM
  (`InstallRowViewModel.cs:68`) — **has zero callers.** Dead code. The Refresh button does NOT use
  it.
- Both the ctor path and the Refresh button call the **identical** `RebuildInstallRows()` +
  `BuildInstallRows()` with identical inputs; `GetInstallState` reads disk live with no caching
  (`MacAppState.cs:72-79`). So a *recompute* cannot differ between the two paths.
- The ONLY thing that differs is **when** they run. `App.OnFrameworkInitializationCompleted`
  (`App.axaml.cs:44-46`) does `new MainWindow()` then `_mainWindow.Initialize(vm)` — and there is
  no `.Show()` here. `Initialize()` synchronously runs `BuildInstallRows()` (and wires the static
  XAML buttons' `Click` handlers), stuffing `InstallList.Children` (`MainWindow.axaml.cs:295-324`)
  into a control tree that **has not had a layout pass yet**. Avalonia drives measure/arrange/
  hit-test from the render loop *after* the window is shown — not before. So imperative children
  added pre-show are not laid out / not hit-testable until a later invalidation. Clicking Refresh
  rebuilds the rows *after* the window is live, so they render and respond — which is exactly the
  observed "fixed by Refresh."

This single cause (pre-show build) explains all of: blank/missing rows, "stale" layout, and dead
toggles/buttons. The swallowed-exception theory (`MainViewModel.cs:36`) is ruled out — the harness
reports a non-blank `Installed=true` state at first init, so the ctor never hit its catch.

**Confidence:** the badge half (5a) is high-confidence from code; the layout-timing half (5b) is a
strong structural inference (strategist CONFIDENCE 62 read-only). Three cheap runtime probes
de-risk it before/at build time:
1. In `BuildInstallRows`, log `this.IsLoaded` + `InstallList.Bounds` at the first (Initialize)
   call vs the Refresh-triggered call. Pre-show should show `IsLoaded=false` / empty Bounds.
2. Before any Refresh, try the static **Options tab** and **Turn Off** buttons. If those are ALSO
   dead, the root is window-level (confirms 5b); if only row buttons are dead it is the imperative-
   children path specifically (still 5b's family).
3. Confirm the leftover Vencord stub on disk matches the badge (rules 5a in/out).

**Fix direction (small, local to the Mac shell):** populate the UI from a window-realized
lifecycle hook instead of synchronously pre-show — i.e. either call `_mainWindow.Show()` before/at
`Initialize()`, or move the first `BuildInstallRows()` + `UpdateStatusUi()` + `UpdateOptionsUi()`
into the window's `Opened`/`Loaded` handler (or a single `Dispatcher.UIThread.Post`), so the first
full paint happens against a realized, hit-testable tree. No Core change; no data-path change.
Build U4 to drive this from a realized hook AND to delete the dead `RefreshState()` or wire it —
the spec picks the realized-hook approach and keeps `RebuildInstallRows()` as the populate path.

## 6. Styling-system decision (item 1)

Three options considered:

- **(Rejected) Keep fighting Fluent in code-behind.** Status quo. Setting `.Background` after
  the fact loses to Fluent's pseudo-class setters on hover/press. Endless whack-a-mole; this is
  the current "ugly".
- **(Rejected) Drop FluentTheme, hand-roll a SimpleTheme.** Largest blast radius; would restyle
  ScrollViewer/Slider/Window internals we don't want to own. Overhaul, not polish.
- **(CHOSEN) Add a keyed `ControlTheme` per button variant + a styled `ScrollBar`/chip in a
  resource dictionary, keep FluentTheme for everything else.** Define `ControlTheme x:Key=...`
  for: accent pill, green/"on" pill, ghost/dark pill, the per-install mod button, the tab button
  (with active-underline state), the small toggle (On/Off), the chip, and the mod card; plus a
  `ScrollBar` ControlTheme (thin rounded thumb, no arrows) mirroring `MainWindow.xaml:58-104`.
  Each variant's `:pointerover` / `:pressed` is defined in the theme so Fluent never reasserts.

Palette feeding — how the theme gets colors and survives live theme switching:

- Use **`DynamicResource` brush keys** (`Bg`, `Card`, `Border`, `Text`, `Sub`, `Accent`,
  `AccentHover`, `OnAccent`, `Ghost`, `GhostHover`, `On`, `OnText`, `Scroll`) declared in
  `App.axaml` resources, exactly the WPF key set (`MainWindow.xaml:11-22`). The ControlThemes
  bind to those keys.
- On theme switch, `ApplyPalette()` updates the brush *values* in `Application.Current.Resources`
  (replace each `SolidColorBrush` for the 4-palette set). Because the ControlThemes use
  `DynamicResource`, every styled control repaints automatically — including hover/press states —
  with no per-control code. This REPLACES most of the imperative `.Background =` churn in
  `MainWindow.axaml.cs` / `InstallRow.axaml.cs`, but is additive: existing code-behind that sets
  state-dependent brushes (e.g. toggle On vs Off) can stay, now layered on a control that no
  longer fights back.

Scope guard: this is still "polish" — we are adding styles and swapping a handful of `<Button>`s
to `Theme="{StaticResource ...}"`, not converting the app to XAML data-binding. The VM and the
imperative row-building stay.

## 7. Title-bar decision (item 2)

Avalonia **12.0.4** (this project, `PatchCord.Mac.csproj`) — NOT 11.x as the brief assumed.
Verified facts:
- `ExtendClientAreaChromeHints` was **removed** in v12; the new model is `WindowDecorations` +
  `ExtendClientAreaToDecorationsHint` + a `WindowDrawnDecorations` control. (Avalonia 12 breaking
  changes doc.)
- `ExtendClientAreaToDecorationsHint` is still present.
- An open v12 issue (#21160) reports the custom-chrome/traffic-light path is in flux on macOS.

Options:

- **(Rejected) `ExtendClientAreaToDecorationsHint=true` + custom title-bar region with a ~78px
  left inset to clear the traffic lights.** Closest to the Windows custom-chrome feel, but rides
  the exact v12 chrome surface that is unstable (#21160) and is more than a polish-sized change.
- **(CHOSEN) Keep the native macOS title bar; move the wordmark into the content area and inset
  it so it never sits under the traffic lights.** Concretely: keep the default decorations (no
  chrome props), drop the Title Row 0 header overlay, and render "PATCH CORD" at the top of the
  content with a left inset clear of where the title text/traffic lights are — i.e. align it with
  the tab bar's `Margin=24` left edge (already clear of the lights since the lights live in the
  native bar above the client area). This keeps the Win11 wordmark look, needs no custom
  min/close on mac (correct macOS convention), and avoids the fragile chrome API. Set the
  `Window.Title` to "PatchCord" so the native bar reads cleanly.

Decision: native bar + content-area wordmark inset. Re-evaluate the extend-client-area path only
if the user wants the wordmark literally on the title bar (out of scope for polish).

## 8. Section-label / text color pass (item 3)

Once the `Sub`/`Text` brushes are `DynamicResource` keys (§6), set `Foreground="{DynamicResource Sub}"`
on every section-label and description `TextBlock` in `MainWindow.axaml`, and `{DynamicResource Text}`
on value labels (slider readouts, "Keep OpenAsar installed", etc.) — mirroring the WPF reference
which sets these explicitly. Because they are static text, declaring the brush in XAML (not
code-behind) is enough and they repaint on theme switch via DynamicResource. This removes the need
to repaint them in `ApplyPalette()`.

## 9. InstallRow polish (item 5)

- Mod button + toggle become the ghost-pill / green-pill ControlThemes from §6 (so hover/press
  stop flipping to Fluent grey).
- "Managed" pill: green `On` bg, white text, CornerRadius 5, the W96×H30 size from the reference
  (`InstallRow.xaml:31-35`).
- Badges already match (CornerRadius 4, Padding 8,3, 10px Bold white) — keep, just ensure the
  green uses the `On` brush key so it tracks the theme.
- Row divider uses the `Border` brush key (currently a hard-coded `#3A3C42` in
  `InstallRow.axaml:5`, which is correct only for the Discord palette).

## 10. Verification strategy (and the one manual gate)

A true visual check needs a human: headless `screencapture` is blocked by macOS Screen-Recording
TCC for the terminal (same constraint that gated B5.3). So verification is three-legged:

1. **Build-green** — `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3) +
   `dotnet build src/PatchCord.Core/PatchCord.Core.csproj` (flag-free) + the Mac project.
2. **`--mac-uitest` structural assertions** (extend the existing harness, `MainWindow.axaml.cs`
   `ReportUiTestInfo`/`RunB3UiTests`): assert (a) install rows populate at first paint with no
   manual refresh (count > 0 and a non-default state), (b) representative buttons/labels resolve a
   palette brush (not the Fluent default) — e.g. read back `BtnToggle.Background` and assert it
   equals the expected `On`/`Accent` brush, and a section label's `Foreground` equals `Sub`,
   (c) theme cycling still repaints all 4 (already covered by `RunB3UiTests`). These are
   property-readback assertions, feasible headless.
3. **Manual visual pass (the one human step, call it out like B5.3):** launch `PatchCord.app`,
   eyeball Status + Options against `docs/status.png` / `docs/options.png`: wordmark clear of
   traffic lights, gray section labels, green pills, selected mod-card border, hover/press on
   buttons, all 4 themes.

No automated pixel check is assumed. If a feasible one surfaces (e.g. an offscreen Avalonia
`RenderTargetBitmap` of a control to assert a fill color without TCC), it can supplement leg 2,
but it is not relied upon.

## 11. Out of scope
- Converting to full XAML data-binding / MVVM commands.
- Custom title-bar chrome (extend-client-area) / custom min-close buttons.
- Any change to `PatchCord.Core`, the Windows shell, or `publish.ps1`.
- New palettes or palette value changes (the 4 are already correct in `MacTheme.cs`).
