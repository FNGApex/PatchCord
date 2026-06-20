# Design — macOS Permissions (App Management) & Persistent Signing

Branch: `feat/macos-port`. Companion spec: `docs/spec/mac-permissions.md` (change log = truth).

## Problem

Layer-A patching (Vencord/Equicord) and OpenAsar write inside
`/Applications/Discord.app/Contents/Resources/app.asar`. On macOS 13+ (hardened on
macOS 26 Tahoe) writes inside another app's signed bundle are gated by the **App
Management** TCC service (`kTCCServiceSystemPolicyAppBundles`) — even when the file is
owned by the user with the POSIX write bit set. The user wanted a "cleaner install":
ideally an in-app permission prompt instead of sending users into System Settings.

## Empirical findings (dev Mac, macOS 26.5, 2026-06-19)

All four routes were tested on the live machine:

| Test | Operation | Result |
|---|---|---|
| A | non-root write into Discord bundle | **DENIED** — `Operation not permitted` (TCC, despite owner-write bit) |
| B | **root** write via `osascript … with administrator privileges` | **DENIED** — privilege escalation does **not** bypass App Management |
| C | non-root write into `~/Library/Application Support/discord/.../discord_desktop_core` | **OK** — no TCC gate (BetterDiscord path is free) |
| D | signed app (self-signed), real bundle write via LaunchServices | **DENIED** — macOS shows a **notification** ("…was prevented from modifying apps on your Mac"), **not** an Allow/Deny prompt |

### Conclusions

1. **There is no in-app consent prompt for App Management.** Unlike Camera/Mic/Automation,
   App Management (and Full Disk Access) are "Settings-only" TCC services. macOS only:
   silently fails the write → fires a missable notification → adds the app to
   *System Settings › Privacy & Security › App Management* (toggled **off**). The first
   grant **always** requires a manual Settings toggle. No signing tier (ad-hoc,
   self-signed, or paid Developer ID) changes this.
2. **Privilege escalation is a dead end** (Test B). Root is still subject to App Management.
3. **The notification is system-owned** — we can't customize or rely on it (transient,
   easily missed). It *is* click-through to the App Management pane, but only as a backstop.
4. **Signing still matters — for persistence, not prompting.** Ad-hoc signing gives an
   unstable code identity (cdhash changes every build), so a granted permission evaporates
   on the next rebuild/update, forcing a re-grant. A **stable self-signed identity** gives a
   fixed designated requirement (`identifier "com.tomgks.patchcord" and certificate leaf =
   H"…"`), so the grant **persists across all future updates** and the Settings/notification
   entry shows the correct "PatchCord" identity.
5. **BetterDiscord needs no grant** (Test C) and is the macOS first-run default — so most
   users never hit the gate. The Settings grant is required only for Vencord/Equicord/OpenAsar.

## Decision

Accept the OS model and build the cleanest install macOS permits:

- **Persistent self-signed signing** (no Apple account, $0): bake a reproducible
  "PatchCord Self-Signed" code-signing identity into the release build so a granted
  permission sticks across updates. Fall back to ad-hoc (with a loud warning) when the
  cert is absent, so a fresh checkout still builds.
- **App-Management-first launch gate**: refine the existing onboarding window to lead with
  App Management only (demote Full Disk Access — heavier and equally prompt-less), explain
  the notification reality, and **proactively** surface the gate on launch whenever an
  enabled install needs a bundle write and isn't already injected. Keep the existing
  reactive handler (fires on a real patch EPERM) and the Re-check/retry button.
- **Keep BetterDiscord as the zero-friction default** so the gate is the exception, not the rule.

### Attribution caveat

When PatchCord is launched from a terminal/IDE, TCC attributes the bundle write to the
parent (e.g. the notification in Test D read "Visual Studio Code"). Launched normally from
Finder/Dock, PatchCord is its own responsible process and the notification + Settings entry
read "PatchCord". This only affects dev-launch ergonomics, not shipped behavior.

## Non-goals

- No Developer ID / notarization (requires a paid Apple account; deferred).
- No privileged helper / SMAppService (needs signing + adds attack surface; Test B proved
  root doesn't help anyway).
- No automatic bundle probe on startup just to detect the grant (would fire the OS
  notification on every launch). The gate keys off install state instead.
</content>
</invoke>
