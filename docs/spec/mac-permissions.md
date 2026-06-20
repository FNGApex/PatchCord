# Spec — macOS Permissions Gate & Persistent Signing (Phase P)

Branch: `feat/macos-port`. Design: `docs/design/mac-permissions.md`. **This change log = truth.**

Goal: make the App-Management grant *persist* across updates (stable signing) and surface a
clean, App-Management-first launch gate, since macOS offers no in-app consent prompt for
bundle writes (see design findings A–D).

## Checkpoints

| ID | Title | Done? |
|---|---|---|
| P1 | `make-signing-cert.sh` — idempotent self-signed code-signing identity | ✅ idempotent re-run is a no-op |
| P2 | `publish-mac.sh` — sign with persistent identity; ad-hoc fallback + warning | ✅ signs `flags=0x0(none)`, stable DR |
| P3 | `FdaOnboarding` copy/UI → App-Management-first; FDA demoted (constants retained) | ✅ |
| P4 | `ModNeedsBundleWrite` classifier (vencord/equicord/openasar = true) | ✅ |
| P5 | `ShowGateIfNeeded` — proactive launch gate, once per launch, state-driven | ✅ |
| P6 | Wire `ShowGateIfNeeded` into `MainWindow.RenderInitial` | ✅ |
| P7 | Verify: sln builds; `--mac-fdatest` pass; `--mac-selftest` 8/8; DR stable | ✅ build 0/0; fdatest 4/4; selftest 8/8 |

**Status: code-complete (2026-06-19). Headless verification green. Awaiting user live-test**
(launch from Finder → select Vencord/OpenAsar → gate appears → grant once → patch succeeds →
gate stops → rebuild, grant still holds). DR confirmed stable: `identifier "com.tomgks.patchcord"
and certificate leaf = H"0b1d9f25…"`.

### P1 — make-signing-cert.sh
- New script at repo root. Creates a self-signed cert with `extendedKeyUsage=codeSigning`,
  exports a legacy-algo PKCS#12 (`-macalg sha1 -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES`,
  password-protected — macOS `security import` rejects LibreSSL's default empty-password MAC),
  imports into the login keychain with `-A` (no codesign ACL prompt).
- Identity common name: `PatchCord Self-Signed`. Bundle id stays `com.tomgks.patchcord`.
- **Idempotent**: if `security find-identity -p codesigning` already lists the name, exit 0
  without recreating.

### P2 — publish-mac.sh
- Replace the ad-hoc `codesign -s - --deep --force "$APP_DIR"` block:
  - Resolve the identity SHA-1 by name: `security find-identity -p codesigning | grep "PatchCord Self-Signed"`.
  - If found → `codesign -s <sha1> --deep --force --timestamp=none "$APP_DIR"`.
  - If absent → warn (point at `make-signing-cert.sh`) and fall back to `codesign -s - --deep --force`.
- After signing, print `codesign -d -r-` so the build log shows the (stable) designated requirement.

### P3 — FdaOnboarding.cs (copy + UI)
- Keep `FdaDeepLink`, `AppMgmtDeepLink`, `IsPermissionError`, `MakeHandler`, `ShowOnboarding`,
  `ShowGuidance`, `OpenUrl` (call sites + `--mac-fdatest` B3.9 assert the two constants).
- Window changes:
  - Title → "PatchCord needs App Management to patch Discord".
  - Body explains: macOS blocked the write and showed a notification (not a prompt); grant
    App Management once in Settings and (with the signed build) it persists across updates.
  - **Primary** button: "Open App Management" (`AppMgmtDeepLink`).
  - Remove the prominent "Open Full Disk Access" button; replace with a small secondary
    "Advanced: Full Disk Access" text-button (FDA still works but is heavier — demoted).
  - Keep Re-check/retry (when install+monitor present) and Dismiss.

### P4 — ModNeedsBundleWrite
- `public static bool ModNeedsBundleWrite(string mod)` in `FdaOnboarding`:
  `mod` ∈ {`vencord`,`equicord`} → true; {`betterdiscord`,`none`,`other`,empty} → false.
- (OpenAsar is handled at the call site via `cfg.OpenAsar`.)

### P5 — ShowGateIfNeeded
- `public static void ShowGateIfNeeded(AppConfig cfg, MonitorService monitor)`:
  - Guard: a private `static bool _gateShownThisLaunch` — show at most once per process.
  - For each `cfg.Installs` where `Enabled`:
    - `needsBundle = ModNeedsBundleWrite(inst.ClientMod) || cfg.OpenAsar`
    - if `!needsBundle` → continue.
    - `state = MacAppState.GetInstallState(inst, cfg.OpenAsar)`.
    - **Skip if already satisfied**: desired asar mod already injected
      (`state.InjectedMod == inst.ClientMod` for vencord/equicord) **and**
      (`!cfg.OpenAsar || state.OpenAsarPresent`). Already-working grant → no nag.
    - **Skip if the mod isn't installed yet** (`inst.ClientMod != "none" &&
      !MacAppState.ModInstalled(inst.ClientMod)`) — the mod-missing CTA owns that case;
      don't double-warn. (OpenAsar has no installer prerequisite.)
    - Otherwise → set guard, `ShowOnboarding(inst, monitor, cfg)`, return.
- Net: fires for a Vencord/Equicord/OpenAsar install that is ready to patch but not yet
  injected (i.e. the grant is the blocker); silent for BetterDiscord/none and for
  already-patched installs.

### P6 — RenderInitial wiring
- In `MainWindow.RenderInitial`, after `SetMonitorTimer(...)`, call
  `FdaOnboarding.ShowGateIfNeeded(MacAppState.Config, _monitor!)` (guard `_monitor != null`,
  and skip when `AutoCloseAfterMs > 0` so uitest smoke runs aren't interrupted).

### P7 — Verification (headless; GUI gate is user-tested)
- `dotnet build PatchCord.sln` → 3/3 projects, no errors.
- `PatchCord --mac-fdatest` → B3.9 all pass (constants + handler intact).
- `PatchCord --mac-selftest` → 8 passed, 0 failed (no behavior regression).
- Run `make-signing-cert.sh` twice → second run is a no-op (idempotent).
- Build via `publish-mac.sh` twice → `codesign -dv` shows `flags=0x0(none)` (not adhoc) and
  identical designated requirement both times (DR stable → grant persists).
- **User live-test (out of scope for headless):** launch from Finder, select Vencord/OpenAsar,
  confirm the gate appears; grant once in Settings; confirm patch succeeds and the gate stops
  appearing; rebuild and confirm the grant still holds (no re-prompt).
</content>
