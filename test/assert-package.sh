#!/usr/bin/env bash
# Asserts that a packed LinkAudit .nupkg is actually installable and visible in the backoffice.
#
# Both CI and the release workflow run this against the real artifact, because the things it checks are
# invisible to a normal build:
#
#   * umbraco-package.json is how Umbraco DISCOVERS the package. Without it the assembly still loads and
#     the audit still runs, but the dashboard is never registered and the package does not appear under
#     Installed packages — which is exactly how 2.0.0 shipped: the manifest is generated at build time and
#     gitignored, so on a clean checkout it did not exist when the SDK's wwwroot glob was evaluated, and
#     the first build (CI is always a first build) packed a nupkg without it. Local builds packed it fine,
#     having generated it on a previous run.
#   * The dependency range is the package's compatibility contract and the only thing the Marketplace
#     reads. Unbounded, it advertises support for Umbraco majors that have never been tested.
#
#   ./test/assert-package.sh [path/to/package.nupkg]
#
# With no argument, uses the single non-symbols nupkg in artifacts/.
set -euo pipefail

cd "$(dirname "$0")/.."

NUPKG="${1:-}"
if [ -z "$NUPKG" ]; then
    NUPKG=$(ls artifacts/*.nupkg 2>/dev/null | grep -v symbols | head -1 || true)
fi

if [ -z "$NUPKG" ] || [ ! -f "$NUPKG" ]; then
    echo "::error::No .nupkg to inspect (looked for: ${1:-artifacts/*.nupkg})"
    exit 1
fi

echo "Inspecting $NUPKG"

PLUGIN_DIR=staticwebassets/App_Plugins/UmbracoCommunity.LinkAudit
MANIFEST="$PLUGIN_DIR/umbraco-package.json"
FAILED=0

fail() { echo "::error::$1"; FAILED=1; }

LISTING=$(unzip -Z1 "$NUPKG")
NUSPEC=$(unzip -p "$NUPKG" '*.nuspec')

# --- The backoffice manifest -------------------------------------------------------------------------
if ! grep -qxF "$MANIFEST" <<< "$LISTING"; then
    fail "$MANIFEST is MISSING from the package. Umbraco discovers packages by this file: without it there is no dashboard and no entry under Installed packages. See StampUmbracoPackageManifest in the csproj."
    echo "Packed static web assets were:"
    grep '^staticwebassets/' <<< "$LISTING" | sed 's/^/  /' || echo "  (none)"
else
    echo "OK: $MANIFEST is packed."

    MANIFEST_JSON=$(unzip -p "$NUPKG" "$MANIFEST")

    # The manifest version is what the backoffice shows under Installed packages; it is stamped from the
    # build version, so a mismatch means the stamping silently stopped working.
    PKG_VERSION=$(sed -nE 's@.*<version>([^<]+)</version>.*@\1@p' <<< "$NUSPEC" | head -1)
    MANIFEST_VERSION=$(sed -nE 's@.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*@\1@p' <<< "$MANIFEST_JSON" | head -1)
    if [ "$PKG_VERSION" != "$MANIFEST_VERSION" ]; then
        fail "umbraco-package.json version ('$MANIFEST_VERSION') does not match the package version ('$PKG_VERSION'); the backoffice would show the wrong version."
    else
        echo "OK: manifest version matches the package version ($PKG_VERSION)."
    fi

    # Every extension's element must resolve to a file that is actually in the package — a manifest
    # pointing at a missing script yields an empty dashboard rather than an install-time error.
    while read -r ELEMENT; do
        [ -n "$ELEMENT" ] || continue
        ASSET="staticwebassets/${ELEMENT#/}"
        if ! grep -qxF "$ASSET" <<< "$LISTING"; then
            fail "manifest references element '$ELEMENT' but '$ASSET' is not in the package."
        else
            echo "OK: manifest element '$ELEMENT' is packed."
        fi
    done <<< "$(sed -nE 's@.*"element"[[:space:]]*:[[:space:]]*"([^"]+)".*@\1@p' <<< "$MANIFEST_JSON")"
fi

# --- The Umbraco dependency range ------------------------------------------------------------------
grep -E '<dependency id="Umbraco' <<< "$NUSPEC" | sed 's/^[[:space:]]*/  /'
if grep -qE '<dependency id="Umbraco[^"]*" version="\[[^)]*\)"' <<< "$NUSPEC"; then
    echo "OK: Umbraco dependencies declare a bounded range."
else
    fail "Umbraco dependency range is unbounded. An open range makes the Marketplace claim support for untested majors."
fi

if [ "$FAILED" -ne 0 ]; then
    echo
    echo "Package assertions FAILED."
    exit 1
fi

echo
echo "All package assertions passed."
