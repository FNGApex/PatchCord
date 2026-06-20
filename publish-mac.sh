#!/usr/bin/env bash
# Builds the release artifact: a self-contained, ad-hoc-signed PatchCord.app
# for macOS (osx-arm64 / Apple Silicon). Output: ./publish/PatchCord.app
#
# Requires:
#   - .NET 10 SDK    (https://dotnet.microsoft.com/download)
#   - sips + iconutil (bundled with macOS Xcode Command Line Tools)
#   - codesign        (bundled with Xcode Command Line Tools)
#
# Usage:
#   chmod +x publish-mac.sh
#   ./publish-mac.sh
#
# The resulting publish/PatchCord.app is ad-hoc signed (codesign -s -).
# On first launch after download, right-click → Open to bypass Gatekeeper.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/src/PatchCord.Mac/PatchCord.Mac.csproj"
ICO="$SCRIPT_DIR/app.ico"
OUT_DIR="$SCRIPT_DIR/publish"
APP_DIR="$OUT_DIR/PatchCord.app"
BUNDLE_NAME="PatchCord"
BUNDLE_ID="com.tomgks.patchcord"
VERSION="1.8.1"
MIN_MACOS="13.0"

# ── Locate dotnet ─────────────────────────────────────────────────────────────
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download" >&2
    exit 1
fi

echo "==> Publishing osx-arm64 self-contained binary..."
dotnet publish "$PROJ" -c Release -r osx-arm64 --self-contained true

PUBLISH_DIR="$SCRIPT_DIR/src/PatchCord.Mac/bin/Release/net10.0/osx-arm64/publish"
NATIVE_EXE="$PUBLISH_DIR/PatchCord.Mac"

if [[ ! -x "$NATIVE_EXE" ]]; then
    echo "ERROR: Expected native binary not found at $NATIVE_EXE" >&2
    exit 1
fi
echo "    Binary: $NATIVE_EXE ($(du -sh "$NATIVE_EXE" | cut -f1))"

# ── Convert app.ico → PatchCord.icns ─────────────────────────────────────────
echo ""
echo "==> Building PatchCord.icns from app.ico..."
ICONSET_DIR="/tmp/PatchCord.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

# sips extracts the largest frame (256x256) from the .ico to a 1024x1024 staging PNG.
# We then scale it down to every required iconset size.
STAGING_PNG="/tmp/patchcord_source.png"
sips -s format png "$ICO" --out "$STAGING_PNG" --resampleHeightWidth 1024 1024 >/dev/null 2>&1

for size in 16 32 64 128 256 512; do
    sips -z "$size" "$size" "$STAGING_PNG" --out "$ICONSET_DIR/icon_${size}x${size}.png"      >/dev/null 2>&1
    double=$((size * 2))
    sips -z "$double" "$double" "$STAGING_PNG" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null 2>&1
done

ICNS_PATH="$OUT_DIR/PatchCord.icns"
mkdir -p "$OUT_DIR"
iconutil -c icns "$ICONSET_DIR" -o "$ICNS_PATH"
rm -rf "$ICONSET_DIR" "$STAGING_PNG"
echo "    ICNS: $ICNS_PATH ($(du -sh "$ICNS_PATH" | cut -f1))"

# ── Assemble PatchCord.app skeleton ──────────────────────────────────────────
echo ""
echo "==> Assembling PatchCord.app..."
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

# Copy the entire publish directory under Contents/MacOS so all Avalonia dylibs,
# native runtimes, and resource assemblies are alongside the executable.
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"

# Rename the entrypoint to PatchCord (CFBundleExecutable).
mv "$APP_DIR/Contents/MacOS/PatchCord.Mac" "$APP_DIR/Contents/MacOS/$BUNDLE_NAME"
chmod +x "$APP_DIR/Contents/MacOS/$BUNDLE_NAME"

# Copy icon.
cp "$ICNS_PATH" "$APP_DIR/Contents/Resources/$BUNDLE_NAME.icns"

# Write Info.plist.
cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>${BUNDLE_NAME}</string>
  <key>CFBundleIdentifier</key>
  <string>${BUNDLE_ID}</string>
  <key>CFBundleName</key>
  <string>${BUNDLE_NAME}</string>
  <key>CFBundleDisplayName</key>
  <string>${BUNDLE_NAME}</string>
  <key>CFBundleIconFile</key>
  <string>${BUNDLE_NAME}.icns</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
  <key>CFBundleVersion</key>
  <string>${VERSION}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleSignature</key>
  <string>????</string>
  <key>LSMinimumSystemVersion</key>
  <string>${MIN_MACOS}</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

echo "    Layout: $(find "$APP_DIR" | wc -l | tr -d ' ') entries"
plutil -lint "$APP_DIR/Contents/Info.plist" && echo "    Info.plist: OK"

# ── Code-sign ────────────────────────────────────────────────────────────────
# Prefer the persistent self-signed identity (stable designated requirement → the
# macOS "App Management" grant survives rebuilds/updates). Fall back to ad-hoc with
# a warning when the cert is absent, so a fresh checkout still produces a runnable
# (but non-persistent) build. Create the identity once with ./make-signing-cert.sh.
SIGN_IDENTITY_NAME="PatchCord Self-Signed"
echo ""
SIGN_SHA1="$(security find-identity -p codesigning 2>/dev/null \
    | grep -F "$SIGN_IDENTITY_NAME" | head -1 | awk '{print $2}')"
if [[ -n "$SIGN_SHA1" ]]; then
    echo "==> Signing PatchCord.app with persistent identity '$SIGN_IDENTITY_NAME' ($SIGN_SHA1)..."
    codesign -s "$SIGN_SHA1" --deep --force --timestamp=none "$APP_DIR"
    echo "    Signed (App-Management grant will persist across updates)."
else
    echo "WARNING: persistent identity '$SIGN_IDENTITY_NAME' not found in the keychain."
    echo "         Falling back to AD-HOC signing — any App-Management grant the user gives"
    echo "         will evaporate on the next rebuild. Run ./make-signing-cert.sh once to fix."
    echo "==> Ad-hoc signing PatchCord.app..."
    codesign -s - --deep --force "$APP_DIR"
    echo "    Ad-hoc signed."
fi
codesign -dv "$APP_DIR" 2>&1 | grep -E "^(Identifier|Format|CodeDirectory|Signature|Authority)" || true
echo "    Designated requirement (TCC keys on this):"
codesign -d -r- "$APP_DIR" 2>&1 | grep -i "designated" || true

echo ""
echo "Done -> $APP_DIR"
echo "       ($(du -sh "$APP_DIR" | cut -f1) on disk)"
echo ""
echo "First-run: right-click PatchCord.app → Open to bypass Gatekeeper."
echo "To patch Vencord/Equicord/OpenAsar inside the Discord bundle, grant PatchCord"
echo "'App Management' in System Settings → Privacy & Security → App Management"
echo "(BetterDiscord needs no grant). The app guides this on first patch."
