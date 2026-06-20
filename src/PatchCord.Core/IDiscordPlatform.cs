namespace PatchCord;

/// <summary>
/// Platform abstraction for Discord discovery, path resolution, process control,
/// mod-data paths, run-at-login, and single-instance enforcement.
/// Implementations live in the platform shells (Windows shell, Mac shell).
/// Core logic (PatchEngine, BetterDiscordEngine, OpenAsarEngine) calls into this
/// interface rather than touching OS-specific APIs directly.
/// </summary>
public interface IDiscordPlatform
{
    // ── Discovery ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the standard Discord branches installed on this machine.
    /// Windows: scans %LOCALAPPDATA% for branches that have Update.exe.
    /// Mac: scans /Applications (and ~/Applications) for Discord*.app bundles.
    /// </summary>
    IReadOnlyList<Install> DiscoverInstalls();

    /// <summary>
    /// Return true if <paramref name="path"/> looks like a Discord install root,
    /// and set <paramref name="branch"/> to the inferred branch name.
    /// Used for the "add custom install" picker.
    /// Windows: checks for Update.exe or app-* subdirs.
    /// Mac: checks for a Discord*.app bundle at the path.
    /// </summary>
    bool LooksLikeInstall(string path, out string branch);

    // ── Path resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the resources directory for Layer A (app.asar swap + OpenAsar).
    /// Windows: highest app-*/resources dir under inst.Path.
    /// Mac: &lt;bundle&gt;/Contents/Resources.
    /// Returns null if the install cannot be resolved (e.g. not yet launched).
    /// </summary>
    string? ResolveResourcesDir(Install inst);

    /// <summary>
    /// Resolve the app versioned directory for Layer B (BetterDiscord index.js rewrite).
    /// Windows: highest app-* dir under inst.Path (same root as Layer A).
    /// Mac: highest ~/Library/Application Support/&lt;branch&gt;/app-&lt;ver&gt; that contains modules/.
    /// Returns null if not resolvable.
    /// </summary>
    string? ResolveCoreAppDir(Install inst);

    /// <summary>
    /// A human-readable version label for the status UI (e.g. "app-0.0.395").
    /// Returns null if the install is not yet resolved.
    /// </summary>
    string? AppVersionLabel(Install inst);

    // ── Process control ────────────────────────────────────────────────────────

    /// <summary>
    /// Return true if at least one Discord process for this install is currently running.
    /// Windows/Mac: matches by process name (branch → process name mapping differs on Mac).
    /// </summary>
    bool IsRunning(Install inst);

    /// <summary>
    /// Return true if a Discord auto-update is currently in progress for this install.
    /// Windows: checks for a running "Update" process.
    /// Mac: checks for a running ShipIt process OR a recently modified ShipIt_request.json.
    /// </summary>
    bool IsUpdateInProgress(Install inst);

    /// <summary>
    /// Stop all Discord processes for this install (Kill loop with poll).
    /// </summary>
    void Stop(Install inst);

    /// <summary>
    /// Start (relaunch) Discord for this install.
    /// Windows: Update.exe --processStart &lt;branch&gt;.exe.
    /// Mac: open -a &lt;bundle name&gt;.
    /// </summary>
    void Start(Install inst);

    // ── Mod data paths ─────────────────────────────────────────────────────────

    /// <summary>
    /// Path to Vencord's patcher.js on the host machine.
    /// Windows: %APPDATA%\Vencord\dist\patcher.js (honoring VENCORD_USER_DATA_DIR).
    /// Mac: ~/Library/Application Support/Vencord/dist/patcher.js.
    /// </summary>
    string VencordPatcherPath { get; }

    /// <summary>
    /// Path to Equicord's patcher.js on the host machine.
    /// Windows: %APPDATA%\Equicord\dist\patcher.js (honoring EQUICORD_USER_DATA_DIR).
    /// Mac: ~/Library/Application Support/Equicord/dist/patcher.js.
    /// </summary>
    string EquicordPatcherPath { get; }

    /// <summary>
    /// Path to BetterDiscord's betterdiscord.asar on the host machine.
    /// Windows: %APPDATA%\BetterDiscord\data\betterdiscord.asar.
    /// Mac: ~/Library/Application Support/BetterDiscord/data/betterdiscord.asar.
    /// </summary>
    string BetterDiscordAsarPath { get; }

    // ── BandagedBD (Layer C) ─────────────────────────────────────────────────────
    //
    // BandagedBD is the Windows-only "app folder" BD variant: it drops a whole
    // resources/app folder Electron loads instead of app.asar, so it is snapshotted and
    // restored after each Discord update (BandagedBDEngine). It is a separate installer
    // from BetterDiscord (Layer B) and is NOT supported on macOS — the Mac shell no-ops
    // every method below (Injected/HasSnapshot → false, the rest do nothing), so the
    // shared monitor's Layer-C path is inert on macOS.

    /// <summary>True if a BandagedBD app folder is currently injected for this install.</summary>
    bool BandagedBdInjected(Install inst);

    /// <summary>True if we hold a snapshot of this install's BandagedBD app folder.</summary>
    bool BandagedBdHasSnapshot(Install inst);

    /// <summary>Snapshot the currently-injected BandagedBD app folder so it can be restored later.</summary>
    void BandagedBdSnapshot(Install inst);

    /// <summary>Restore the snapshotted BandagedBD app folder (e.g. after a Discord update wiped it).</summary>
    void BandagedBdRestore(Install inst);

    /// <summary>Remove the injected BandagedBD app folder (switching away from BandagedBD).</summary>
    void BandagedBdRemove(Install inst);

    // ── Run-at-login ───────────────────────────────────────────────────────────

    /// <summary>
    /// Return true if PatchCord is configured to run at login.
    /// Windows: checks for PatchCord.lnk in the Startup folder.
    /// Mac: checks for the LaunchAgent plist at ~/Library/LaunchAgents/com.tomgks.patchcord.plist.
    /// </summary>
    bool RunAtLoginEnabled { get; }

    /// <summary>
    /// Enable or disable run-at-login.
    /// Windows: creates/removes PatchCord.lnk via COM IShellLinkW/IPersistFile.
    /// Mac: writes/removes a LaunchAgent plist and calls launchctl bootstrap/bootout.
    /// </summary>
    void SetRunAtLogin(bool enabled);

    // ── Single instance ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to acquire the single-instance lock.
    /// Returns true if this process is the first instance; false if another is already running.
    /// Windows: tries to acquire Global\PatchCordApp named mutex.
    /// Mac: tries to acquire a lock file or named POSIX semaphore.
    /// </summary>
    bool TryAcquireSingleInstance();
}
