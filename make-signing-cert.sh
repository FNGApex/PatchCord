#!/usr/bin/env bash
# Creates a persistent, self-signed code-signing identity for PatchCord.app.
#
# Why: ad-hoc signing (codesign -s -) gives an UNSTABLE code identity — the cdhash
# changes on every build, so any macOS "App Management" permission the user grants
# evaporates on the next rebuild/update. A stable self-signed identity gives a fixed
# designated requirement, so the grant PERSISTS across updates. No Apple Developer
# account is required (this is NOT Developer ID / notarization — Gatekeeper still
# wants a right-click → Open on first launch).
#
# Idempotent: if the identity already exists in the login keychain, this is a no-op.
#
# Usage:  ./make-signing-cert.sh
# Then:   ./publish-mac.sh   (signs with this identity automatically)

set -euo pipefail

IDENTITY_NAME="PatchCord Self-Signed"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
P12_PASS="patchcord"   # transient — only used to move the key into the keychain

# ── Already present? ──────────────────────────────────────────────────────────
if security find-identity -p codesigning 2>/dev/null | grep -qF "$IDENTITY_NAME"; then
    echo "==> Identity '$IDENTITY_NAME' already in the keychain — nothing to do."
    security find-identity -p codesigning 2>/dev/null | grep -F "$IDENTITY_NAME" || true
    exit 0
fi

echo "==> Creating self-signed code-signing identity '$IDENTITY_NAME'..."

WORK="$(mktemp -d /tmp/pc-cert.XXXXXX)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"

# ── 1. Config: codeSigning EKU is what makes codesign accept the identity ──────
cat > cert.cnf <<'CNF'
[req]
distinguished_name = dn
x509_extensions = v3
prompt = no
[dn]
CN = PatchCord Self-Signed
[v3]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
CNF

# ── 2. Self-signed cert (10 years) ────────────────────────────────────────────
openssl req -x509 -newkey rsa:2048 -nodes \
    -keyout key.pem -out cert.pem -days 3650 -config cert.cnf >/dev/null 2>&1

# ── 3. Legacy-algo PKCS#12 — macOS `security import` rejects LibreSSL's default
#       MAC/empty-password p12, so force SHA1/3DES with a real password. ────────
openssl pkcs12 -export -out pc.p12 -inkey key.pem -in cert.pem \
    -passout "pass:$P12_PASS" -name "$IDENTITY_NAME" \
    -macalg sha1 -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES >/dev/null 2>&1

# ── 4. Import into the login keychain. -A = no per-use codesign ACL prompt. ────
security import pc.p12 -k "$KEYCHAIN" -P "$P12_PASS" -A

echo ""
if security find-identity -p codesigning 2>/dev/null | grep -qF "$IDENTITY_NAME"; then
    echo "==> Done. Identity installed:"
    security find-identity -p codesigning 2>/dev/null | grep -F "$IDENTITY_NAME"
    echo ""
    echo "    (CSSMERR_TP_NOT_TRUSTED is expected — a self-signed root is untrusted for"
    echo "     Gatekeeper, but TCC keys on the cert leaf hash, so the App-Management grant"
    echo "     will still persist across rebuilds. Run ./publish-mac.sh to sign with it.)"
else
    echo "ERROR: import reported success but the identity is not listed. Aborting." >&2
    exit 1
fi
