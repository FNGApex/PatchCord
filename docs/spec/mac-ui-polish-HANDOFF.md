# HANDOFF — macOS UI polish (Phase U)

**Status as of 2026-06-19: U1–U5 + U6.1/U6.2 DONE. Only U6.3 (manual visual gate, user-run) remains.
Branch `feat/macos-port`, local commits only (NOT pushed, by user's standing preference).**

Goal: polish the macOS Avalonia shell (`src/PatchCord.Mac/*`) to look clean and true to the
Windows 11 reference (`docs/status.png`, `docs/options.png`). POLISH not overhaul — keep the
two-tab structure + imperative code-behind/VM. Mac shell ONLY (no Core / Windows shell / publish.ps1).

## Ground truth
- Spec: `docs/spec/mac-ui-polish.md` (checkpoint table U1–U6 + Change log = current truth).
- Design: `docs/design/mac-ui-polish.md` (§6 styling, §7 title bar, §5b blank-until-refresh root cause).
- Reference look: `docs/status.png`, `docs/options.png` (Discord palette).

## Done (committed, verified)
- **U1 + U1.1 + Discord default** — `e2882d0`. `Styles.axaml`: 14 DynamicResource palette brushes +
  keyed ControlThemes; `ApplyPalette()` swaps brush values on theme switch; Space Grotesk bundled;
  Mac defaults to Discord palette on first run.
- **U2 + U3** — `538ac40`. Removed the Row-0 header overlay colliding with the native traffic lights;
  wordmark moved into a content-area row; section labels/descriptions use `{DynamicResource Sub/Text}`.
- **U4 + U5** — `6ddeb28` (Iter C). U4: first full render moved out of synchronous `Initialize()` into
  a one-shot self-detaching `MainWindow.Opened` hook (`RenderInitial()`, guarded by `_firstRenderDone`)
  → rows/badges/toggles render + respond on first paint with NO manual Refresh (closes follow-up 001);
  dead `RefreshState()` deleted. U5: removed the imperative `RowModBtn` bg/fg that clobbered the
  ModButton hover/press; magic badge hex pulled to `MacTheme.BadgeNeutral/BadgeError/BadgeText`
  (HighContrast dark-text-on-red regression caught in review and fixed pre-commit; reviewer
  CONFIDENCE 88, 1 risk fixed).
- **U6.1 + U6.2** — `23aa9ff` (Iter D). `--mac-uitest` (`RunU4U5UiTests`) now asserts first-paint rows
  populate with no Refresh (`_firstRenderDone` + rendered `InstallRow` count) and that
  `BtnToggle.Background`/`StatusSub.Foreground` read back the active palette's `Accent`/`On`/`Sub`.
- Each verified: `dotnet build PatchCord.sln -p:EnableWindowsTargeting=true` (3/3), Core flag-free,
  `dotnet run --project src/PatchCord.Mac -- --mac-uitest` (U1 + U6.1 + U6.2 PASS, all 4 themes cycle).

## Remaining — U6.3 ONLY (the one human step, like B5.3)
A true visual sign-off needs the user — headless `screencapture` is blocked by macOS Screen-Recording
TCC for the terminal, so it cannot be automated. To run it:
1. Build the app bundle: `./publish-mac.sh` (produces `PatchCord.app`).
2. Launch `PatchCord.app` and eyeball **Status** + **Options** vs `docs/status.png` / `docs/options.png`:
   - wordmark clear of the native traffic lights;
   - gray section labels; green "on" pills; selected mod-card has the accent border;
   - button hover/press behave (no flip to Fluent grey);
   - rows + badges + toggles work immediately on launch (no manual Refresh needed);
   - cycle all 4 themes (Discord / Dark / Light / HighContrast) — check badge text legibility,
     especially neutral/error badges under HighContrast.
3. When it looks right, flip U6 → DONE in `docs/spec/mac-ui-polish.md` and note Phase U complete.

## Test entrypoint
`dotnet run --project src/PatchCord.Mac -- --mac-uitest` — headless; prints `[uitest]` structure,
exercises tray/alert, cycles all 4 themes, asserts U1 (14/14 brushes + toggle Theme + font),
U6.1 (first-paint rows, no Refresh), U6.2 (palette-brush readback). Exit 0 = pass. (No `timeout`
command on this macOS — run directly; the window self-closes after 2500ms.)

_Note: Phase B5 (Layer-B/OpenAsar/monitor) is fully done on this same branch; see
`docs/spec/run-on-mac.md`. The whole `feat/macos-port` branch is local-only / unpushed._
