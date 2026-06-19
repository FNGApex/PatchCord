# Spec — macOS (Avalonia) UI polish

Status: COMPLETE — U1–U6 DONE incl. U6.3 manual visual gate (branch feat/macos-port) · Date: 2026-06-19

> Polish pass on the macOS Avalonia shell (`src/PatchCord.Mac/*`) to match the shipped Windows 11
> reference UI (`docs/status.png`, `docs/options.png`). Keep the two-tab structure and the
> imperative code-behind + view-model approach — no conversion to full XAML data-binding. Mac
> shell ONLY: do not touch `PatchCord.Core`, the Windows shell, or `publish.ps1`. The build stays
> green (sln 3/3 + Core flag-free) and `--mac-uitest` keeps passing throughout. Design rationale,
> evidence trail, palette/metric extraction, and the root-cause analysis live in
> `docs/design/mac-ui-polish.md`.

Locked decisions (design §6, §7):
- **Default theme (user, 2026-06-19):** the macOS app defaults to the **Discord** palette on first run
  to pixel-match the reference screenshots. Implement Mac-shell-side only (do NOT change any Core
  default shared with the Windows shell). All 4 themes remain switchable.
- **Wordmark font (user, 2026-06-19):** U1.1 is IN SCOPE — bundle Space Grotesk for the wordmark/labels.
- **Styling system:** keep `FluentTheme`; ADD keyed `ControlTheme`s per button/scrollbar/chip
  variant in a resource dictionary, fed by `DynamicResource` palette brushes that `ApplyPalette()`
  swaps on theme switch. No SimpleTheme, no per-control brush-fighting in code-behind.
- **Title bar:** keep the **native** macOS title bar; move the "PATCH CORD" wordmark into the
  content area, inset left (aligned with the tab bar's `Margin=24`) so it never sits under the
  traffic lights. No extend-client-area / custom chrome (Avalonia 12 removed
  `ExtendClientAreaChromeHints`; the chrome path is in flux, issue #21160).
- **Blank-until-refresh root cause (design §5b):** the whole UI is built before the window is
  shown/laid out. Fix = populate from a window-realized lifecycle hook. The wrong-badge symptom
  (design §5a) is by-design (badge = on-disk injected mod ≠ configured mod), not a bug.

---

## Phase U — UI polish (U1–U6)

| # | Checkpoint | Files touched | Verification command | Status |
|---|---|---|---|---|
| U1 | **Styling system.** Add a resources dictionary (in `App.axaml` or a merged `Styles.axaml`) declaring the 13 palette brush keys as `DynamicResource` (`Bg,Card,Card2,Border,Text,Sub,Accent,AccentHover,OnAccent,Ghost,GhostHover,On,OnText,Scroll`) seeded from the Discord palette, plus keyed `ControlTheme`s with full idle/`:pointerover`/`:pressed` for: accent pill (H42 r6 pad20,0), green "on" pill, ghost/dark pill (H38 r8 1px border pad16,0), per-install mod button (MinW118 H30 r5), tab button (active underline state), small toggle (W62 H30 r5 On/Off), chip (r6 pad15,8 selected=Accent 2px), mod card (r6 pad16,12 selected=Accent border+GhostHover). Update `ApplyPalette()` (`MainWindow.axaml.cs:487`) to replace the brush *values* in `Application.Current.Resources` for the resolved palette so DynamicResource consumers repaint. Swap the bare `<Button>`s in `MainWindow.axaml`/`InstallRow.axaml` to `Theme="{StaticResource …}"`. Keep `FluentTheme`. | `src/PatchCord.Mac/App.axaml` (+ optional `Styles.axaml` + csproj `AvaloniaResource`), `MainWindow.axaml`, `MainWindow.axaml.cs`, `InstallRow.axaml`, `InstallRow.axaml.cs` | `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3) + `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (theme-cycle 4/4 still PASS) | DONE (e2882d0) |
| U1.1 | **Wordmark font (in scope).** Add `SpaceGrotesk-{Bold,SemiBold,Medium,Regular}.ttf` (from `src/fonts/`) as `AvaloniaResource` under `Assets/`; set the window/`FontFamily` for the wordmark (and JetBrains Mono for any mono log text if present) to match the reference. | `src/PatchCord.Mac/Assets/*.ttf`, `PatchCord.Mac.csproj`, `App.axaml`/`MainWindow.axaml` | `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3) | DONE (e2882d0) |
| U2 | **Title bar.** Remove the Grid Row-0 header overlay; keep native decorations (no chrome props). Render the logo dot (9×9 r2 Accent) + "PATCH" (Text) / "CORD" (Accent) wordmark at the top of the content, inset left to clear the traffic lights (align with tab bar `Margin=24`). Set `Title="PatchCord"`. Verify the wordmark never overlaps the native traffic lights. | `src/PatchCord.Mac/MainWindow.axaml`, `MainWindow.axaml.cs` (any header-paint code) | `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3); manual visual = no traffic-light collision (folded into U6.3) | DONE (538ac40) |
| U3 | **Section-label / text color pass.** Give every section-label `TextBlock` `Foreground="{DynamicResource Sub}"` and every value/description label its correct `Sub`/`Text` brush in `MainWindow.axaml` (Monitoring, Discord installs, Recent patches, Client mod + its description, OpenAsar, Startup, Permissions, Check interval, Appearance & notifications, Theme, Notification style, slider readouts). Remove now-redundant `Foreground` repaints from `ApplyPalette()` where DynamicResource covers them. Match the reference's gray section labels. | `src/PatchCord.Mac/MainWindow.axaml`, `MainWindow.axaml.cs` | `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (new U6.2 label-brush assertion PASS); `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3) | DONE (538ac40) |
| U4 | **Fix blank-until-refresh + responsive toggles (design §5b).** Populate the first full UI from a window-realized hook instead of synchronously pre-show: either `Show()` the window before `Initialize()` in `App.OnFrameworkInitializationCompleted` (`App.axaml.cs:44-46`), or move the first `BuildInstallRows()` + `UpdateStatusUi()` + `UpdateOptionsUi()` + `SwitchTab` into the `MainWindow.Opened`/`Loaded` handler (or a single `Dispatcher.UIThread.Post`). Confirm install rows + badges render and toggles/buttons respond on first paint with NO manual Refresh. Delete the dead `RefreshState()` (`InstallRowViewModel.cs:68`) OR leave it (do not wire a new path) — pick deletion to avoid dead code. Keep `RebuildInstallRows()` as the populate path. | `src/PatchCord.Mac/App.axaml.cs`, `MainWindow.axaml.cs`, `InstallRowViewModel.cs` | `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (new U6.1 first-paint assertion: rows>0 + a representative toggle has its palette brush, with NO Refresh call) PASS; manual = toggles work before any Refresh (U6.3) | DONE (6ddeb28) |
| U5 | **InstallRow / badges.** Apply the U1 ghost-pill (mod button) + green-pill (Managed toggle, W96 H30 r5 On bg white text) ControlThemes so hover/press stop flipping to Fluent grey. Ensure badges use the `On` brush key (CornerRadius 4, Padding 8,3, 10px Bold white) and the row divider uses the `Border` brush key (replace the hard-coded `#3A3C42` at `InstallRow.axaml:5`). Match the reference badge/pill look across all 4 themes. | `src/PatchCord.Mac/InstallRow.axaml`, `InstallRow.axaml.cs` | `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (theme-cycle + row-render still PASS); `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3) | DONE (6ddeb28) |
| U6 | **Build-green + uitest assertions + manual visual gate.** (U6.1) Extend `--mac-uitest` (`MainWindow.axaml.cs` `ReportUiTestInfo`/`RunB3UiTests`) to assert install rows populate at first paint without a manual Refresh (count>0). (U6.2) Assert representative controls/labels resolve a palette brush, not the Fluent default — read back e.g. `BtnToggle.Background` == expected `On`/`Accent` brush and a section label `.Foreground` == `Sub`. (U6.3) **MANUAL VISUAL PASS (the one human step — like B5.3):** launch `PatchCord.app`, eyeball Status + Options vs `docs/status.png`/`docs/options.png`: wordmark clear of traffic lights, gray section labels, green pills, selected mod-card border, button hover/press, all 4 themes. | `src/PatchCord.Mac/MainWindow.axaml.cs` (uitest asserts only) | `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3, 0 err) + `dotnet build src/PatchCord.Core/PatchCord.Core.csproj` (flag-free) + `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (all asserts PASS, exit 0) + **manual** visual pass U6.3 | DONE — U6.1/U6.2 (23aa9ff); U6.3 visual pass confirmed by user on PatchCord.app |

### Phase U Out of scope
- Full XAML data-binding / MVVM commands (keep imperative code-behind + VM).
- Custom title-bar chrome / extend-client-area / custom min-close buttons.
- Any change to `PatchCord.Core`, the Windows shell, or `publish.ps1`.
- Palette value changes (the 4 palettes already match the Windows shell byte-for-byte).
- Automated pixel/screenshot verification (blocked by macOS Screen-Recording TCC for the terminal;
  if a TCC-free offscreen `RenderTargetBitmap` color check proves feasible it MAY supplement U6.2,
  but it is not relied upon).

---

## Change log

- 2026-06-19 — **Phase U COMPLETE — U6.3 visual pass confirmed + live-test bug fixes (ebddc88).**
  User ran the packaged `PatchCord.app` against a freshly-cleaned Discord and signed off on the
  visual gate (wordmark, gray labels, green pills, mod-card border, hover/press, all 4 themes).
  Live testing surfaced five functional bugs (outside the original U-scope but fixed in the same
  pass, all user-confirmed): **B1** per-install mod dropdown was decorative → wired a MenuFlyout +
  `MainViewModel.SetInstallMod`; **B2** mod-missing CTA hardcoded "Get Vencord" + dead → label now
  reflects the missing mod and opens its install page; **B3** macOS window close quit the whole app
  → `ShutdownMode=OnExplicitShutdown` + `Closing`→hide-to-tray (App.Quit sets `IsQuitting`);
  **B4** row picker didn't sync the Options selection → `SetInstallMod` syncs the global default +
  repaints Options; **B5** first-run defaulted to Core's `vencord` → macOS first-run default is now
  BetterDiscord (Vencord/Equicord parked on mac). Verified: sln 3/3, `--mac-uitest` U1/U6.1/U6.2
  PASS + clean auto-close under the new ShutdownMode. NOTE: live patching into `/Applications/
  Discord.app` requires the host app's one-time macOS **App Management** grant (per-app).
- 2026-06-19 — **U4 + U5 + U6.1/U6.2 DONE; only the U6.3 manual visual gate remains.** Iter C
  (commit 6ddeb28): **U4** — moved the first full render (`ApplyPalette`/`UpdateStatusUi`/
  `UpdateOptionsUi`/`BuildInstallRows`/`SwitchTab`/`SetMonitorTimer`/`ReportUiTestInfo`) out of
  synchronous `Initialize()` into a one-shot self-detaching `MainWindow.Opened` hook
  (`RenderInitial()`, double-guarded by `_firstRenderDone`); controls are now measured/hit-testable
  on first paint, so rows/badges/toggles render and respond with no manual Refresh (closes
  follow-up 001). Deleted dead `RefreshState()` (0 callers). **U5** — removed the imperative
  `RowModBtn` bg/fg sets in `InstallRow.Bind()` that clobbered the ModButton ControlTheme
  hover/press; pulled the magic badge hex to named semantic consts
  (`MacTheme.BadgeNeutral`/`BadgeError`/`BadgeText`); known-mod badges keep theme-aware `OnText`,
  neutral/error badges keep fixed white (avoids a HighContrast dark-text-on-red regression caught
  in review, CONFIDENCE 88, 1 risk fixed pre-commit). Iter D (commit 23aa9ff): **U6.1/U6.2** —
  extended `--mac-uitest` (`RunU4U5UiTests`) to assert first-paint rows populate with no Refresh
  (`_firstRenderDone` + rendered `InstallRow` count) and that `BtnToggle.Background`/
  `StatusSub.Foreground` read back the active palette's `Accent`/`On`/`Sub` brushes. Verified each:
  sln 3/3, Core flag-free, `--mac-uitest` U1+U6.1+U6.2 PASS + 4 themes cycle. **Remaining: U6.3**
  — the one human step: launch `PatchCord.app` and eyeball Status+Options vs the reference
  screenshots across all 4 themes. Local commits only; not pushed.
- 2026-06-19 — **U1–U3 DONE; U4–U6 remain.** Iter A (commit e2882d0): U1 styling system
  (`Styles.axaml` — 14 DynamicResource palette brushes + keyed ControlThemes incl. a `WarnPill`
  added in review for the amber mod-missing CTA; `ApplyPalette()` swaps brush values on theme
  switch; toggle swaps Theme AccentPill↔OnPill; tabs use `.active` class) + U1.1 Space Grotesk +
  Discord default (Mac-shell first-run only). Iter B (commit 538ac40): U2 title bar (removed the
  50px Row-0 overlay; wordmark moved to a content-area row clear of the native traffic lights;
  `Title="PatchCord"`) + U3 label colors (DynamicResource Sub/Text). Verified each: sln 3/3, Core
  flag-free, `--mac-uitest` U1 PASS 14/14 + all 4 themes cycle. Iter-A reviewer CONFIDENCE 87, 0
  blockers (the one risk fixed pre-commit). **Remaining: U4** (blank-until-refresh + responsive
  toggles + delete dead `RefreshState()`), **U5** (InstallRow/badges — also folds in the Iter-A
  review nits: `RowRemove` X-button still Fluent-default, hardcoded badge hex `#80848E`/`#F23F43`),
  **U6** (uitest asserts U6.1/U6.2 + the U6.3 MANUAL visual gate). Local commits only; not pushed.
- 2026-06-19 — **U1–U6 PLANNED.** Polish the macOS Avalonia UI to the Win11 reference. Decisions:
  keyed ControlThemes + DynamicResource palette (keep FluentTheme); native title bar + content-area
  wordmark inset (Avalonia 12 removed `ExtendClientAreaChromeHints`, chrome path unstable per
  issue #21160). Confirmed (headless `--mac-uitest` + strategist code review) that blank-until-
  refresh is NOT an empty-install-list bug: rows are present at first init; root cause is the UI
  being built before the window is shown/laid out (`App.axaml.cs:44-46` → pre-show
  `BuildInstallRows`), with `RefreshState()` confirmed dead code. The wrong-badge symptom is
  by-design (badge = on-disk injected mod, decoupled from configured mod). Verification: build-green
  + uitest property-readback asserts + one manual visual gate (U6.3). Nothing built yet. Design:
  `docs/design/mac-ui-polish.md`.
