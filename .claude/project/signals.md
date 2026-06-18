# Project Signals — PatchCord

<!-- apex-signals-ref: aae982f8d276fc853c98d4da28695482960a2c845c0e721191eea1c0af8b4de3 -->

## Framework / Runtime

| Signal | Value |
|---|---|
| Language | C# (LangVersion: latest) |
| Target | net10.0-windows |
| UI framework | WPF (UseWPF=true) + WinForms tray only (System.Windows.Forms global using removed to avoid WPF collisions; WinForms reached via `WinForms =` alias in MainWindow.xaml.cs) |
| Output type | WinExe |
| Version | 1.7.1 |
| Assembly / namespace | PatchCord / PatchCord |
| Nullable | enabled |
| Implicit usings | enabled |

## Build / Test / Publish

| Task | Command / Tool | Notes |
|---|---|---|
| Build (debug) | `dotnet build src/PatchCord.csproj` | Framework-dependent; no runtime packs needed |
| Publish (release) | `.\publish.ps1` (PowerShell) | Produces a self-contained single-file `publish\PatchCord.exe` via `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` |
| Runtime target | `win-x64` (default in publish.ps1; `-Runtime` param is overridable) | Must be built on / for Windows |
| Test hooks | `--dumpstub`, `--openasar-test`, `--bd-test`, `-selftest` CLI args on the exe | No automated test project; testing is via these debug flags in App.xaml.cs |
| Lint / format | None configured | No .editorconfig, no `dotnet format` invocation in the tree |

## Language Breakdown

| Language | Files | Notes |
|---|---|---|
| C# | src/*.cs (12 files) | All application logic |
| XAML | src/App.xaml, src/MainWindow.xaml, src/InstallRow.xaml | WPF UI declarations |
| PowerShell | publish.ps1 | Release build script |
| XML | src/PatchCord.csproj, src/app.manifest | Project file + Windows app manifest |
| Assets | src/fonts/*.ttf (5), *.ico (3) | Embedded resources |

## Domains

| Domain | Key files | What it does |
|---|---|---|
| **patch-engine** | src/PatchEngine.cs | Core asar patching: builds Vencord/Equicord stub asar, detects mod presence, patches/unpatches the app.asar↔_app.asar swap, stops/starts Discord processes, locates the latest `app-*` versioned dir |
| **bd-engine** | src/BetterDiscordEngine.cs | BetterDiscord injection path: locates `discord_desktop_core/index.js` (wrapped and legacy layouts), injects/restores the require line, dry-run hook |
| **openasar-engine** | src/OpenAsarEngine.cs | OpenAsar download (GitHub nightly), 12-hour local cache at `BaseDir/openasar.asar`, installs by replacing the underlying asar with a backup, byte-scan detection |
| **config** | src/Config.cs | Data model: `Install`, `PatchEvent`, `UiConfig`, `AppConfig`; JSON load/save; first-run discovery of standard Discord branches under `%LOCALAPPDATA%`; per-install `ClientMod` with global default fallback; 12-entry patch history; migration of legacy `openAsar` flag |
| **app-bootstrap** | src/App.xaml.cs, src/App.xaml | Startup: single-instance mutex, path resolution (BaseDir, config.json, patcher.js locations, betterdiscord.asar), tray vs. windowed launch, CLI debug hooks, global exception handler |
| **ui-main** | src/MainWindow.xaml.cs, src/MainWindow.xaml | WPF main window: status tab (monitoring toggle, install rows, patch history, mod-missing warning), options tab (client mod chooser, OpenAsar toggle, startup toggle, notification settings, theme/style chips, interval slider); WinForms tray icon + context menu; monitor loop (DispatcherTimer) that calls patch-engine/bd-engine/openasar-engine; diagnostics clipboard builder |
| **ui-install-row** | src/InstallRow.xaml, src/InstallRow.xaml.cs | Reusable WPF UserControl for a single Discord install entry: enabled/paused toggle, per-install mod dropdown popup, status badges (Vencord/Equicord/BetterDiscord/OpenAsar/error), remove button (custom installs only) |
| **ui-alert** | src/Alert.cs | Top-left banner notification window (WPF frameless): four styles (bar/solid/minimal/outline), scale + duration driven by UiConfig, auto-dismisses via DispatcherTimer |
| **ui-theme** | src/Theme.cs | Theme palette registry (Discord / Dark / Light / HighContrast), brush/glow/gradient factory helpers used across all UI code |
| **startup** | src/Startup.cs | Windows logon shortcut: creates/removes `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\PatchCord.lnk` via COM IShellLinkW/IPersistFile |
| **logging** | src/Log.cs | Single-file append logger, 1 MB rotation to `.old` |

## Cross-cutting Notes

- **Patching model**: Two independent mod layers coexist. Layer A (app.asar stub) covers Vencord and Equicord. Layer B (discord_desktop_core index.js rewrite) covers BetterDiscord. OpenAsar sits below both layers as the actual app.asar content. PatchEngine.GetState() surfaces all three via InstallState.
- **Per-install mod**: Each `Install` carries its own `ClientMod` string (vencord / equicord / betterdiscord / none). The global `AppConfig.ClientMod` is only a default for new installs and a "apply to all" shortcut in the UI.
- **No NuGet dependencies**: The project has zero third-party NuGet references; everything is BCL + Windows SDK.
- **WinForms alias pattern**: `using Drawing = System.Drawing; using WinForms = System.Windows.Forms;` in MainWindow.xaml.cs avoids ambiguity with WPF types of the same name.
- **Single-file publish caveat**: `PublishSingleFile=false` during `dotnet build` (framework-dependent, fast); only `dotnet publish` sets it true. The csproj comment explains this explicitly.
- **Config file location**: `config.json` and `patchcord.log` sit beside the running exe (`AppContext.BaseDirectory`), not in `%APPDATA%`.
- **Process guard**: `_patchFailed` HashSet prevents re-killing Discord on the same session after a patch error; cleared on monitoring toggle or mod change.
- **Patcher paths**: Vencord → `%APPDATA%\Vencord\dist\patcher.js` (overridable via `VENCORD_USER_DATA_DIR`); Equicord → `%APPDATA%\Equicord\dist\patcher.js` (overridable via `EQUICORD_USER_DATA_DIR`); BetterDiscord → `%APPDATA%\BetterDiscord\data\betterdiscord.asar`.
