# Design — Running PatchCord on a Mac

Status: draft for approval · Date: 2026-06-18 · Owner: planning

## 1. The question, restated

The user wants to "get PatchCord to run and work on a Mac." That phrase hides two
very different goals, and the honest answer differs for each:

- **Goal A — Develop/build the code on a Mac.** Compile, edit, hack on the source from
  macOS.
- **Goal B — Use the app's features on a Mac.** Run PatchCord and have it keep a
  *Discord install* patched (Vencord/Equicord/BetterDiscord/OpenAsar).

These are split deliberately because Goal A is partly achievable today and Goal B is
not achievable with the current code at all — the whole feature set targets a
*Windows* Discord install.

## 2. What the code actually is (primary evidence)

| Claim | Evidence (file:line) | Verdict |
|---|---|---|
| Target is Windows-only TFM | `src/PatchCord.csproj:5` `<TargetFramework>net10.0-windows</TargetFramework>` | supported |
| WPF + WinForms UI (Windows-only frameworks) | `src/PatchCord.csproj:6-7` `<UseWPF>true` / `<UseWindowsForms>true` | supported |
| Output is a Windows GUI exe | `src/PatchCord.csproj:4` `<OutputType>WinExe` | supported |
| Publish hard-codes Windows RID | `publish.ps1:9` `$Runtime = 'win-x64'`; `publish.ps1:25` `dotnet publish -r $Runtime` | supported |
| Build is a PowerShell script | `publish.ps1` (whole file); uses `$env:USERPROFILE`, `dotnet.exe` | supported |
| Startup uses Win32 COM (IShellLinkW/IPersistFile) | `src/Startup.cs:24-31, 33-68` | supported |
| Startup writes to Windows Startup folder | `src/Startup.cs:10-11` `SpecialFolder.Startup` + `PatchCord.lnk` | supported |
| Discord discovery assumes `%LOCALAPPDATA%\Discord*\Update.exe` | `src/Config.cs:96-101` `SpecialFolder.LocalApplicationData` + `Update.exe` check | supported |
| Patch path assumes `app-*\resources\app.asar` layout | `src/PatchEngine.cs:58-70` `GetLatestAppDir` globs `app-*` with a `resources` subdir | supported |
| Detects running Discord by process name | `src/PatchEngine.cs:84` `Process.GetProcessesByName(inst.Branch)` | supported |
| Stops Discord by `Process.Kill()` | `src/PatchEngine.cs:141-156` | supported |
| Restarts Discord via `Update.exe --processStart` | `src/PatchEngine.cs:182-192` | supported |
| Mod paths assume `%APPDATA%\Vencord\dist\patcher.js` etc. | `src/App.xaml.cs:71-79` `SpecialFolder.ApplicationData` | supported |
| Single-instance via global Win mutex | `src/App.xaml.cs:87` `"Global\\PatchCordApp"` | supported (also works elsewhere, but UI won't) |
| Project itself states Windows-only | `README.md` "## Notes — Windows only." | supported |

### Build attempt on this Mac (reproducible)
- SDK present: `.NET SDK 10.0.108`, host `osx-arm64`, runtimes `Microsoft.NETCore.App 10.0.8`
  + `AspNetCore.App 10.0.8` only. No Windows Desktop runtime pack (expected — none ships for osx).
- `dotnet build src/PatchCord.csproj` → **fails** with `NETSDK1100: To build a project
  targeting Windows on this operating system, set the EnableWindowsTargeting property to true.`
- `dotnet build src/PatchCord.csproj -p:EnableWindowsTargeting=true` → **succeeds**, emits
  `bin/Debug/net10.0-windows/PatchCord.dll`.

Interpretation: the C# *compiles* on macOS (the WPF/WinForms reference assemblies are
reference-only and can be targeted cross-platform via `EnableWindowsTargeting`). But the
produced binary is a `net10.0-windows` WPF/WinForms artifact. **WPF and WinForms have no
macOS runtime implementation**, so it cannot execute on macOS no matter how it is built or
published. There is no `osx` runtime pack for `Microsoft.WindowsDesktop.App`.

## 3. Why "use the features on a Mac" is a different problem entirely

Even if the UI ran, every domain operation is wired to the Windows Discord layout:

| Concept | Windows (current code) | macOS reality | Portable? |
|---|---|---|---|
| Install discovery | `%LOCALAPPDATA%\Discord*\Update.exe` (`Config.cs:96-101`) | `/Applications/Discord.app/Contents/Resources/` | No — different rooting, no `Update.exe` |
| asar location | `app-<ver>\resources\app.asar` (`PatchEngine.cs:58-70`) | `Discord.app/Contents/Resources/app.asar` (single, unversioned) | No — the whole `app-*` versioning model is Windows-specific |
| Detect running | `Process.GetProcessesByName("Discord")` (`PatchEngine.cs:84`) | process name `Discord`, but `.exe`-name assumptions and branch mapping differ | Partly |
| Stop Discord | `Process.Kill()` (`PatchEngine.cs:141`) | works via `pkill`/Process, but app-bundle relaunch differs | Partly |
| Restart Discord | `Update.exe --processStart Discord.exe` (`PatchEngine.cs:182-192`) | no `Update.exe`; relaunch is `open -a Discord` | No |
| Mod files | `%APPDATA%\Vencord\dist\patcher.js` (`App.xaml.cs:71-79`) | `~/Library/Application Support/Vencord/dist/patcher.js` | Partly (path shape only) |
| BetterDiscord core | `app-*\modules\discord_desktop_core\index.js` (`BetterDiscordEngine.cs:16-32`) | inside the app bundle's modules dir, different rooting | Partly |
| Run-at-login | Win32 COM shortcut in Startup folder (`Startup.cs`) | macOS LaunchAgent plist / Login Items | No — full rewrite |
| Tray icon | WinForms NotifyIcon | macOS NSStatusItem (needs Avalonia/MAUI tray support) | No |

**Genuinely portable, untouched:** the asar *byte format* writer (`PatchEngine.BuildStubAsar`,
`PatchEngine.cs:25-55`), the OpenAsar download/cache logic (`OpenAsarEngine.cs`, pure
`HttpClient` + file IO), the JSON config model (`Config.cs` data classes), the byte-scan
mod detectors. These are platform-neutral .NET. Everything that *locates*, *enumerates*,
or *controls* Discord is Windows-bound.

## 4. Options, with honest tradeoffs

| # | Option | Gets Goal A (build) | Gets Goal B (patch Mac Discord) | Effort | Honest verdict |
|---|---|---|---|---|---|
| 0 | **Compile-only on Mac** (`EnableWindowsTargeting=true`) | Yes (compiles, won't run) | No | Trivial (1 flag) | Good for editing/CI syntax checks; produces a non-runnable Windows binary |
| a | **Windows VM** (Parallels / UTM / VMware Fusion) | Yes | Yes — but patches the *VM's* Discord, not the Mac's | Medium (install Win + Discord + mod in VM) | Runs unchanged. But it patches Discord *inside the VM*, which is almost never what a Mac user wants |
| b | **Wine / CrossOver** | n/a | Unlikely | Medium-High, fragile | WPF needs DirectX9 + the full Windows Desktop stack under Wine; .NET 10 self-contained WPF on CrossOver is unsupported and historically breaks. Not recommended |
| c | **Cloud / remote Windows** (Azure VM, cloud PC) | Yes | Only for a Discord inside that remote Windows | Medium, ongoing cost | Same fundamental mismatch as the VM: it patches a Windows Discord, not your Mac's |
| d | **Genuine macOS port** (Avalonia UI + mac Discord engine + LaunchAgent) | Yes, natively runnable | Yes, against the real Mac Discord | **Large** — effectively a fork | The only option that actually fulfills "run *and work* on a Mac." Large but bounded; most domain logic (asar format, OpenAsar, config) is reusable |

Notes on the "patches the VM's Discord" trap (options a/c): PatchCord manipulates files of
the Discord install on the machine it runs on. A Windows VM/cloud PC has its *own* Discord
under its own `%LOCALAPPDATA%`. It cannot reach across into the host Mac's
`/Applications/Discord.app`. So a VM lets you *run the program*, but it does not keep the
*Mac's* Discord patched. For a Mac user who wants their daily Discord modded, the VM is a
non-answer.

## 5. Recommendation

Pick by intent:

- **If the goal is to develop/build on a Mac (Goal A):** adopt **Option 0** today — add
  `EnableWindowsTargeting` so `dotnet build` works from macOS for editing and syntax/CI
  checks. Understand the output cannot run here; final runnable artifacts still come from a
  Windows publish (`publish.ps1` on Windows). This is the spec's primary, ready-now path.

- **If the goal is to actually patch the Mac's Discord (Goal B):** the honest answer is
  there is **no quick win**. The realistic route is **Option d — a real macOS port**, which
  is a substantial fork (new UI, new platform engine, new startup mechanism). For users who
  just want a modded Discord on macOS *today*, the correct recommendation is to **use the
  official Vencord/Equicord/BetterDiscord installers directly on macOS** — they already
  handle the Mac Discord layout. PatchCord's value-add (auto re-patch after Discord
  auto-updates) would only materialize after the port. The spec captures Option d as a
  phased plan, gated behind an explicit "do you want this large fork?" approval.

- **VM/Wine/cloud (a/b/c):** documented as fallbacks, not recommended, because they either
  patch the wrong Discord (VM/cloud) or are fragile/unsupported (Wine).

## 6. Rejected approaches (and why)

- **"Just change the RID to osx-arm64 in publish.ps1."** Rejected — the TFM is
  `net10.0-windows` and pulls WPF/WinForms; there is no osx runtime pack for those. Changing
  the RID alone yields a publish error or a binary that still can't load WPF on macOS.
- **Wine/CrossOver as the primary path (Option b).** Rejected as primary — unsupported for
  .NET 10 self-contained WPF, fragile, and even if the UI rendered it would still patch a
  *Wine-prefix* Discord, not the Mac's. Kept only as a documented fallback.
- **MAUI instead of Avalonia for the port.** Noted, not chosen here — MAUI on macOS (Mac
  Catalyst) has weaker desktop tray/menu-bar support than Avalonia and a heavier toolchain;
  Avalonia is the closer WPF analogue (XAML, MVVM) and has native macOS tray support.
  **Decided 2026-06-18: Avalonia, osx-arm64 only.**

## 7. B0 spike findings — SIP / code-signing / layout (2026-06-18, on a real install)

Run against `/Applications/Discord.app` (Discord 0.0.395) on this Apple-Silicon Mac, SIP enabled.

**Decisive result: there is no SIP / sudo / re-signing blocker. The port is also structurally
closer to Windows than §3 assumed — one claim there is now corrected.**

Ground truth:
- `/Applications/Discord.app` is **user-owned** (`bear:staff`), notarized Developer-ID,
  **Hardened Runtime on** (`flags=0x10000(runtime)`), `Sealed Resources … files=24`, and
  currently **not quarantined** (`com.apple.quarantine` xattr absent) → Gatekeeper already
  trusts it and will not re-assess on relaunch.
- The **bundle** `Contents/Resources/app.asar` is just the **bootstrap** (`bundle.js`); it is
  part of the sealed resources. It is *not* the mod-injection target.
- The **real patch targets live OUTSIDE the signed bundle**, under
  `~/Library/Application Support/discord/app-<ver>/modules/discord_desktop_core-1/discord_desktop_core/`:
  - `index.js` (41 bytes — the BetterDiscord require-line target), user-owned `-rw-r--r--`
  - `core.asar` (the desktop-core asar — the Vencord/Equicord layer's equivalent)
- These files are user-owned and writable **without sudo**, are **not covered by the bundle's
  code signature**, and SIP does not protect `~/Library` → modifying them cannot break
  Discord's signature, trip Gatekeeper, or require elevation.

Conclusions:
- **B0.1/B0.2 answered:** patch the App-Support `app-*` tree, not the bundle. No signing,
  quarantine, SIP, or permission obstacle. (Even the bundle app.asar could be modified and
  Discord would still launch — no quarantine, no per-launch Developer-ID re-verification — but
  targeting the App-Support core sidesteps the signature question entirely, so that is the
  engine's path.)
- **Correction to §3:** the row claiming "the whole `app-*` versioning model is
  Windows-specific" is **wrong**. macOS has the same `app-<ver>/modules/discord_desktop_core`
  layout, just rooted at `~/Library/Application Support/discord/` (note the `-1` suffix on
  module dirs, e.g. `discord_desktop_core-1`) instead of `%LOCALAPPDATA%\Discord\`. The Windows
  `PatchEngine` "find latest `app-*` → locate core" logic ports far more directly than feared;
  the unversioned bundle `app.asar` is a red herring for patching.
- **Net effect on scope:** Path B is still a real port (UI + startup + path rooting), but the
  highest-risk unknown is retired and the engine reuse map grows — `GetLatestAppDir`-style
  discovery and the BetterDiscord `index.js` rewrite are mostly path-rebasing, not rewrites.

## 8. Re-plan delta — verified macOS engine behavior (2026-06-18, re-plan pass)

The re-plan verified the actual mod-installer behavior against primary sources (the official
Vencord Installer Go source and the BetterDiscord injector TypeScript) and the real local
install. **One spike conclusion is corrected here**; the rest is confirmed and sharpened.

### 8.1 CORRECTION: Vencord/Equicord patch the BUNDLE's `app.asar`, not the App-Support `core.asar`

The B0 spike (§7) inferred that the Vencord/Equicord layer would target
`~/Library/Application Support/discord/app-<ver>/.../core.asar` and called the bundle
`Contents/Resources/app.asar` a "red herring." **That inference was wrong for the asar layer.**
The official Vencord Installer's macOS path resolver is decisive:

- `find_discord_darwin.go`: `appPath = /Applications/Discord.app/Contents/Resources/app`;
  `isPatched = ExistsFile(Contents/Resources/_app.asar)`.
- `patcher.go`: patch operates on `path.Join(appPath, "..")` = `…/Contents/Resources/`, renaming
  `app.asar` → `_app.asar` and writing a stub `app.asar`. Unpatch reverses it. This is the **exact
  same app.asar ↔ _app.asar swap as Windows**, just rooted at the bundle's `Contents/Resources/`.
- `app_asar.go` `WriteAppAsar` byte-for-byte matches `PatchEngine.BuildStubAsar` (same little-endian
  int32 header, same `require(<patcher>)` index.js, same `{"name":"discord","main":"index.js"}`
  package.json, same 4-byte alignment). **Reuse `BuildStubAsar` unchanged.**

Verified locally: `/Applications/Discord.app/Contents/Resources/app.asar` is `bear:staff`,
`-rw-r--r--`, **writable without sudo**. It IS inside `Sealed Resources` (codesign reports
`files=24`), but the spike already proved Discord launches fine after the swap — the bundle is not
quarantined and macOS does not re-verify the Developer-ID signature per launch for an
already-trusted, non-quarantined app. So the asar swap is safe.

### 8.2 CONFIRMED: BetterDiscord patches the App-Support core `index.js` (a DIFFERENT file)

The BetterDiscord injector (`scripts/inject.ts`) on macOS:
- base dir = `~/Library/Application Support/discord` (branch name lowercased);
- `pickVersionDir` chooses the highest `app-X.Y.Z` that contains a `modules/` subdir;
- `resolveCorePath` finds `modules/discord_desktop_core-<N>/discord_desktop_core` (wrapped),
  falling back to legacy `modules/discord_desktop_core`;
- writes `index.js` = `require("<bdAsarPath>");\nmodule.exports = require("./core.asar");`.

This is **identical in shape and location** to PatchCord's existing `BetterDiscordEngine`
(`FindCoreIndexJs` already handles the `discord_desktop_core-*` wrapped + legacy layouts; the
local install confirms `discord_desktop_core-1/discord_desktop_core/index.js`, currently vanilla
`module.exports = require('./core.asar');`). `BetterDiscordEngine` ports with **zero logic change**
once it is handed the App-Support `app-<ver>` dir instead of the Windows one.

### 8.3 The two layers live in DIFFERENT roots on macOS

| Layer | Windows root | macOS root | Engine reuse |
|---|---|---|---|
| A — Vencord/Equicord (`app.asar`↔`_app.asar` swap) | `…\app-<ver>\resources\` | `/Applications/Discord{,' PTB',' Canary',' Development'}.app/Contents/Resources/` | `PatchEngine` swap + `BuildStubAsar` unchanged; only the **resources dir resolver** changes |
| B — BetterDiscord (`index.js` rewrite) | `…\app-<ver>\modules\…` | `~/Library/Application Support/<branch>/app-<ver>/modules/discord_desktop_core-N/discord_desktop_core/` | `BetterDiscordEngine` unchanged; only the **appDir resolver** changes |
| OpenAsar (underlying asar) | `…\app-<ver>\resources\{_,}app.asar` | bundle `Contents/Resources/{_,}app.asar` (same dir as Layer A) | `OpenAsarEngine` unchanged; operates on the Layer-A resources dir |

Consequence for the abstraction: on Windows both layers derive from one `app-<ver>` dir; on macOS
**Layer A's resources dir (bundle) and Layer B's appDir (App-Support core) are unrelated paths**.
The platform abstraction must therefore expose them as two separate resolved paths per install,
not one. (On Windows the same `IDiscordPlatform` impl returns paths under the one `app-<ver>`.)

### 8.4 Branch directory naming (verified)

| Branch | macOS bundle (Layer A) | macOS App-Support dir (Layer B) | Windows |
|---|---|---|---|
| stable | `/Applications/Discord.app` | `~/Library/Application Support/discord` | `Discord` |
| ptb | `/Applications/Discord PTB.app` | `~/Library/Application Support/discordptb` | `DiscordPTB` |
| canary | `/Applications/Discord Canary.app` | `~/Library/Application Support/discordcanary` | `DiscordCanary` |
| dev | `/Applications/Discord Development.app` | `~/Library/Application Support/discorddevelopment` | `DiscordDevelopment` |

(Source: Vencord `find_discord_darwin.go` `macosNames`; BD `inject.ts` `release.toLowerCase().replace(" ","")`.
Bundles are also searched under `~/Applications`. Only stable was present locally; others' naming
is taken from the installers' own maps.)

### 8.5 Detect / stop / restart on macOS (verified)

- **Process name:** `/Applications/Discord.app/Contents/MacOS/Discord` → process name `Discord`
  (Info.plist `CFBundleExecutable=Discord`, `CFBundleIdentifier=com.hnc.Discord`). `Process.GetProcessesByName("Discord")`
  works cross-platform on .NET (matches the executable leaf name). PTB/Canary executables are
  `Discord PTB` / `Discord Canary` — note the SPACE, unlike Windows' `DiscordPTB`. The platform
  abstraction must map branch → mac process name separately from branch → mac bundle name.
- **Stop:** `Process.Kill()` works on macOS (SIGKILL). The existing `StopProcesses` poll loop is
  platform-neutral. Prefer a gentler `SIGTERM`-then-`SIGKILL`, but `Kill()` is acceptable parity.
- **Restart:** no `Update.exe`. Relaunch via `open -a "Discord"` (or `open -b com.hnc.Discord`).
  `Process.Start("open", "-a Discord")` replaces `StartDiscord`'s `Update.exe --processStart`.
- **Mid-update guard:** the Windows monitor defers when `GetProcessesByName("Update")` is non-empty.
  macOS has no `Update.exe`; the analog is Squirrel.Mac `ShipIt` (seen locally as
  `ShipIt_request.json` in the App-Support dir). The mac platform should report "update in progress"
  by checking for a `ShipIt` process or a fresh `ShipIt_request.json`. Decision (approved
  2026-06-18): **implement the `ShipIt` probe in v1.** `IsUpdateInProgress` returns true when a
  `ShipIt` process is running OR `ShipIt_request.json` in the branch's App-Support dir was modified
  within a short recency window (defer the patch while either holds). Next-tick convergence still
  backstops it (the patch is idempotent and backs off on failure via `_patchFailed`), but the probe
  avoids racing a half-applied update rather than relying on self-heal alone.

### 8.6 Re-patch-after-update loop maps directly (item #4)

`MainWindow.InvokeMonitor` (src/MainWindow.xaml.cs:786–930) is platform-neutral logic over four
platform calls: `GetState` (→ `GetLatestAppDir` + `GetProcessesByName` + detectors), `StopProcesses`,
`StartDiscord`, and the `GetProcessesByName("Update")` guard. Route those four through
`IDiscordPlatform` and the entire desired-vs-actual reconciliation (per-install mod compare, OpenAsar
layering, BD layering, alert/history/back-off) ports with **no change**. After a macOS auto-update,
a new `app-<ver>` appears in App-Support (Layer B) and the bundle `app.asar` reverts to vanilla
(Layer A) — `GetState` observes "mod missing," and the loop re-applies and relaunches. Confirmed
direct mapping.

## 9. Project structure (item #1) — one solution, three projects

Decision: a single solution, three projects, sharing a portable core. This keeps the **Windows
single-file publish the only thing `publish.ps1` builds, unchanged**, while adding a mac shell.

```
PatchCord.sln
├─ src/PatchCord.Core/PatchCord.Core.csproj   (net10.0, no UI, no Windows refs, zero NuGet)
├─ src/PatchCord/PatchCord.csproj              (net10.0-windows, WPF+WinForms; references Core)  ← unchanged TFM/output
└─ src/PatchCord.Mac/PatchCord.Mac.csproj      (net10.0, Avalonia, osx-arm64; references Core)
```

- **Core** holds the platform-neutral logic + the `IDiscordPlatform` interface + the data model:
  `BuildStubAsar`, `DetectMod`/`ContainsAscii`, `Unpatch`/`Patch`, `BetterDiscordEngine` (all of it),
  `OpenAsarEngine`, `Config`/`Install`/`PatchEvent`/`UiConfig`/`AppConfig`, `Log`. These compile on
  plain `net10.0` and build on macOS **without** `EnableWindowsTargeting`. Verified pure/impure split:
  `BuildStubAsar`, `DetectMod`, `ContainsAscii`, `InjectContent`, `ContainsMarker` are pure; the
  file-IO methods (`Patch`, `Unpatch`, `Inject`, `Restore`, `OpenAsarEngine.*`) are platform-neutral
  IO over **injected paths** and move to Core as-is.
- **Windows shell** keeps `OutputType=WinExe`, `net10.0-windows`, WPF/WinForms, the icons, fonts,
  app.manifest — i.e. today's `src/PatchCord.csproj` essentially unchanged except `ProjectReference`
  to Core and the Win impl of `IDiscordPlatform` (wrapping `Config.FindStandardInstalls`, `GetLatestAppDir`,
  the process calls, `StartDiscord`, `Startup.cs`, the `Global\` mutex).
- **publish.ps1 stays green:** it points at `src\PatchCord.csproj` (an absolute join of `$PSScriptRoot`).
  If the Windows shell csproj stays at `src/PatchCord/PatchCord.csproj`, update the one path literal;
  if we keep it at `src/PatchCord.csproj` and nest Core/Mac as siblings, **no change at all**. To
  minimize risk, **keep `src/PatchCord.csproj` where it is** and add `src/PatchCord.Core/` and
  `src/PatchCord.Mac/` as new sibling folders. `publish.ps1` then needs zero edits.
- **NuGet note:** Avalonia is the project's FIRST third-party dependency, and it lives ONLY in
  `PatchCord.Mac`. Core and the Windows shell stay zero-NuGet. The Windows single-file self-contained
  publish is unaffected (Avalonia native libs never enter that graph).

Rejected: a single multi-targeted csproj (`net10.0-windows;net10.0`) with `#if` — rejected because
WPF/WinForms `Resource`/`ApplicationDefinition` items and Avalonia's build targets fight in one csproj,
and it makes the Windows single-file publish fragile. Three projects keep each shell's build trivially
correct.

## 10. `IDiscordPlatform` abstraction (item #2) — surface + call-site map

```csharp
public interface IDiscordPlatform
{
    // discovery: standard branches present on this machine
    IReadOnlyList<Install> DiscoverInstalls();                 // Win: %LOCALAPPDATA%\<branch>\Update.exe; Mac: /Applications/<bundle>.app
    bool LooksLikeInstall(string path, out string branch);     // for the "add custom" picker

    // path resolution — TWO independent layers (see design §8.3)
    string? ResolveResourcesDir(Install inst);  // Layer A + OpenAsar dir. Win: app-<ver>\resources ; Mac: <bundle>/Contents/Resources
    string? ResolveCoreAppDir(Install inst);     // Layer B (BetterDiscord). Win: app-<ver> ; Mac: ~/Library/Application Support/<branch>/app-<ver>
    string? AppVersionLabel(Install inst);       // for status UI ("app-0.0.395")

    // process control
    bool IsRunning(Install inst);                // Win/Mac: process name (branch→procname map differs)
    bool IsUpdateInProgress(Install inst);       // Win: "Update" process ; Mac: ShipIt process OR fresh ShipIt_request.json (v1, see §8.5)
    void Stop(Install inst);                      // Process.Kill loop (shared)
    void Start(Install inst);                     // Win: Update.exe --processStart ; Mac: open -a <name>

    // mod data paths (host-rooted)
    string VencordPatcherPath { get; }            // Win: %APPDATA%\Vencord\dist\patcher.js ; Mac: ~/Library/Application Support/Vencord/dist/patcher.js
    string EquicordPatcherPath { get; }
    string BetterDiscordAsarPath { get; }

    // run-at-login
    bool RunAtLoginEnabled { get; }
    void SetRunAtLogin(bool enabled);             // Win: Startup.cs COM .lnk ; Mac: LaunchAgent plist (§12)

    // single instance
    bool TryAcquireSingleInstance();              // Win: Global\ mutex ; Mac: lock file / named lock (§11 of spec)
}
```

Call-site map (each current Windows site → interface member). Cited file:line:

| Current Windows call site | Maps to |
|---|---|
| `Config.cs:96-101` `FindStandardInstalls` (`LocalApplicationData` + `Update.exe`) | `DiscoverInstalls()` |
| `MainWindow.xaml.cs:239-254` `DetectBranch` / `:264-265` `AddCustom` (`Update.exe` + `app-*` probe) | `LooksLikeInstall()` |
| `PatchEngine.cs:58-70` `GetLatestAppDir` (resources variant) | `ResolveResourcesDir()` |
| `PatchEngine.cs:58-70` `GetLatestAppDir` (appDir variant, used by `GetState` line 96 for BD) | `ResolveCoreAppDir()` |
| `PatchEngine.cs:84` `GetProcessesByName(inst.Branch)` | `IsRunning()` |
| `MainWindow.xaml.cs:849` `GetProcessesByName("Update")` | `IsUpdateInProgress()` |
| `PatchEngine.cs:141-156` `StopProcesses` (`Process.Kill`) | `Stop()` |
| `PatchEngine.cs:182-192` `StartDiscord` (`Update.exe --processStart`) | `Start()` |
| `App.xaml.cs:71-79` mod-data paths (`%APPDATA%\Vencord` etc.) | `VencordPatcherPath` / `EquicordPatcherPath` / `BetterDiscordAsarPath` |
| `Startup.cs` (whole file; COM `IShellLinkW`/`IPersistFile`, Startup folder) | `RunAtLoginEnabled` / `SetRunAtLogin()` |
| `App.xaml.cs:87-88,124` `Global\PatchCordApp` mutex | `TryAcquireSingleInstance()` |

`PatchEngine.GetState` (the only method that touches three platform concerns at once) is refactored to
take an `IDiscordPlatform` (or to receive the already-resolved `resourcesDir` + `coreAppDir` + `running`
from the caller). `MainWindow.InvokeMonitor` calls the interface, not `PatchEngine`/`Process` statics.

## 11. Avalonia UI port (item #5)

| WPF/WinForms today | Avalonia equivalent | Notes |
|---|---|---|
| `App.xaml` + `App.xaml.cs` (`System.Windows.Application`) | `Application` + `AppBuilder.Configure<App>().UsePlatformDetect()` in `Program.Main` | osx-arm64 entry point |
| `MainWindow.xaml(.cs)` status + options tabs | Avalonia `Window` + `TabControl`, MVVM (`MainViewModel`) | Same two tabs; bind to Core state via `IDiscordPlatform` |
| `InstallRow.xaml(.cs)` UserControl | Avalonia `UserControl` (`InstallRow`) | enabled toggle, per-install mod popup, status badges, remove — straight port |
| `Alert.cs` frameless WPF banner (4 styles) | Avalonia borderless `Window`, top-left, `DispatcherTimer` auto-dismiss | Avalonia has `DispatcherTimer`; styles map to Avalonia `Styles` |
| `Theme.cs` palette registry | Reuse as-is (pure brush/color factory) OR Avalonia `ThemeVariant` + resource dictionaries | brushes are `SolidColorBrush` — Avalonia type names differ; thin shim |
| WinForms `NotifyIcon` + `ContextMenuStrip` (`MainWindow.xaml.cs:22-23,183-197`) | Avalonia `TrayIcon` + `NativeMenu` (renders as a macOS `NSStatusItem`) | Avalonia `TrayIcon.Menu` gives the menu-bar item with the same actions (header, pause/resume, quit) |
| WinForms `FolderBrowserDialog` (`:258-262`) | Avalonia `StorageProvider.OpenFolderPickerAsync` | "add custom install" picker |
| `Process.Start(url, UseShellExecute=true)` (`:431`) | `Process.Start("open", url)` or Avalonia `TopLevel.Launcher.LaunchUriAsync` | open mod download page |
| tray icons `tray-on.ico`/`tray-off.ico`, `app.ico` | `.png`/`.ico` Avalonia assets; `.icns` for the bundle (§13) | menu-bar template-image ideally monochrome |
| bundled fonts (Space Grotesk, JetBrains Mono) | Avalonia `FontFamily` `avares://` resources | embed identically |

The monitor `DispatcherTimer` (`MainWindow.xaml.cs:173-178`) maps to Avalonia's `DispatcherTimer` 1:1.

## 12. LaunchAgent (item #6) — replaces `Startup.cs`

macOS "run at login" = a per-user LaunchAgent plist at
`~/Library/LaunchAgents/com.tomgks.patchcord.plist`. The mac `IDiscordPlatform.SetRunAtLogin`:

- **enable:** write the plist (below), then `launchctl bootstrap gui/$UID <plist>` (or
  `launchctl load -w` on older systems);
- **disable:** `launchctl bootout gui/$UID <plist>` then delete the file;
- **enabled?:** the plist file exists.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>            <string>com.tomgks.patchcord</string>
  <key>ProgramArguments</key>
  <array>
    <string>/Applications/PatchCord.app/Contents/MacOS/PatchCord</string>
    <string>--tray</string>
  </array>
  <key>RunAtLoad</key>        <true/>
  <key>ProcessType</key>      <string>Interactive</string>
</dict>
</plist>
```

`--tray` reuses the existing CLI flag (`App.xaml.cs:81`) so it starts hidden to the menu bar, same as
the Windows `.lnk` arguments `-Tray`. The program path is resolved from the running bundle at write
time (`Environment.ProcessPath` → walk up to the `.app`). Login Items (SMAppService) is an alternative
but requires a helper bundle; the LaunchAgent plist is simpler and the closest analog to today's
"drop a file that the OS runs at login" model.

## 13. Packaging (item #7) — `.app` bundle

- `dotnet publish src/PatchCord.Mac/PatchCord.Mac.csproj -c Release -r osx-arm64 --self-contained true`
  produces the binary; Avalonia's `Avalonia.app` packaging (or a manual `.app` skeleton) wraps it.
- `.app` layout: `PatchCord.app/Contents/{Info.plist, MacOS/PatchCord, Resources/PatchCord.icns}`.
- `Info.plist` keys: `CFBundleExecutable=PatchCord`, `CFBundleIdentifier=com.tomgks.patchcord`,
  `CFBundleName=PatchCord`, `CFBundleIconFile=PatchCord.icns`, `LSMinimumSystemVersion`, and
  `LSUIElement=true` (menu-bar/agent app, no Dock icon — matches the tray-first UX).
- **Icon:** convert `app.ico` → `PatchCord.icns` (`iconutil`/`sips`).
- **Double-click launch:** the bundle launches the self-contained binary; no separate .NET install.
- **Signing/notarization:** out of scope for v1 — the user runs their own local build; document the
  Gatekeeper right-click-open first-run step. (Distribution signing is a later concern.)
- A `publish-mac.sh` (sibling to `publish.ps1`) is the mac analog; `publish.ps1` stays Windows-only.

## 14. Sequencing & risk (item #8)

Safest first checkpoint is the **portable-core split (B1)** because it is fully reversible and keeps
Windows green at every step: extract Core, point the Windows shell at it via `ProjectReference`, behind
`IDiscordPlatform` with the Windows impl wrapping today's behavior — observable behavior on Windows is
unchanged, and `publish.ps1` still builds the same single-file exe. Only after B1 is green do we add the
Mac shell (B2+), which cannot regress Windows because it is a separate project Avalonia/NuGet graph.
Order: **B1 (core + abstraction, Windows-green) → B2 (mac engine impl) → B3 (Avalonia UI + lifecycle) →
B4 (packaging).** The single highest-value early de-risk inside B1 is proving `PatchCord.Core` compiles
on macOS with plain `net10.0` (no `EnableWindowsTargeting`).

## 15. CORRECTION (2026-06-18): Layer A bundle writes require Full Disk Access (TCC)

**The B0 spike (§7) and §8.1 were WRONG that the bundle `app.asar` is "writable without sudo."**
They measured POSIX ownership/perms (`bear:staff`, `-rw-r--r--`) and inferred writability, but never
attempted the write from an unprivileged process. Verified during B2 on macOS 26.5 (Tahoe), Apple
Silicon:

- A plain (unsigned, no-FDA) process writing inside `/Applications/Discord.app/Contents/Resources/`
  gets **`EPERM` "Operation not permitted"**, NOT `EACCES`. The dir is `drwxr-xr-x bear:staff` so POSIX
  would allow the owner — the block is macOS **TCC App-Management / `com.apple.provenance`** protection
  of signed+notarized app bundles (Discord: hardened runtime, TeamID `53Q6R32WPB`). The
  `com.apple.provenance` xattr is present on the bundle `app.asar`.
- **Resolution (matches the official Vencord Installer):** the user grants the PatchCord app
  **Full Disk Access** once (System Settings → Privacy & Security → Full Disk Access). The Vencord
  Installer detects `os.ErrPermission` and shows: *"Permission denied. Please grant the installer Full
  Disk Access in the system settings (privacy & security page)."* with a `sudo chown -R user:wheel`
  fallback. There is **no Info.plist purpose-string that auto-grants FDA** — it is a manual, persistent
  user grant (the app may deep-link to the pane via `x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles`).

**Scope of impact:**
- **Layer A (Vencord/Equicord bundle swap) + OpenAsar** — affected; require FDA. This is the primary use case.
- **Layer B (BetterDiscord, `~/Library/Application Support/<branch>/app-<ver>/.../index.js`)** — NOT
  inside the bundle, NOT subject to App Management; unaffected.

**Plan deltas (fold into spec):**
- **B3 (UI):** add a first-run / on-EPERM **FDA onboarding** step — detect the permission error, show a
  clear explanation + a button deep-linking to the Full Disk Access settings pane; re-check after grant.
  `MacDiscordPlatform` Patch/Install must surface `EPERM` as a typed, catchable condition (not a generic IOException).
- **B4 (packaging):** the `.app` should be **at least ad-hoc code-signed** so it has a stable identity to
  add to the FDA list (an unsigned binary's FDA entry is path/identity-fragile across rebuilds). Document
  the one-time FDA grant in the README install steps.
- **Verification:** B2.8's real-bundle write and the real-mod load remain **unproven until FDA is granted
  to a packaged/signed PatchCord.app**; the swap mechanism itself is proven byte-identical on a copy.

Path B remains viable — this adds a one-time FDA grant (parity with the reference installer), not a hard blocker.
