#!/usr/bin/env bash
# Boots the test site against each supported Umbraco major using ONE LinkAudit binary (compiled against
# the 17.5.0 floor) and asserts that a full audit completes with correctly-named findings on every one.
#
# This is the safety net the single-binary approach requires: the package no longer fails at restore when
# an Umbraco major moves a member, so the break has to be caught here instead.
#
#   ./test/boot-matrix.sh                     # default matrix
#   ./test/boot-matrix.sh 17.5.0 18.1.0 19.0.0
set -uo pipefail

cd "$(dirname "$0")/.."

VERSIONS=("${@:-}")
if [ -z "${VERSIONS[0]}" ]; then
    VERSIONS=(17.5.0 17.6.1 18.0.0 18.1.0)
fi

SITE=test/LinkAudit.TestSite/LinkAudit.TestSite.csproj
DATA=test/LinkAudit.TestSite/umbraco/Data
LOGDIR=$(mktemp -d)
PORT=5990
FAILED=()

echo "Log directory: $LOGDIR"
echo

for V in "${VERSIONS[@]}"; do
    PORT=$((PORT + 1))
    DB="bootmatrix-${V}.sqlite.db"
    LOG="$LOGDIR/boot-$V.log"

    echo "=============================================================="
    echo " Umbraco $V"
    echo "=============================================================="

    # Fresh database per major: an upgraded 17 database would not prove a clean 18 install works.
    rm -f "$DATA/$DB"*

    if ! dotnet build "$SITE" -c Debug --property:UmbracoVersion="$V" > "$LOG" 2>&1; then
        echo "  BUILD FAILED — see $LOG"
        FAILED+=("$V (build)")
        continue
    fi

    # Confirm the split that makes this a real test: package on the floor, site on $V.
    FLOOR=$(grep -oE '"Umbraco\.Cms\.Web\.Website/[0-9.]+"' src/Umbraco.Community.LinkAudit/obj/project.assets.json | sort -u | tr -d '"' | cut -d/ -f2 | head -1)
    echo "  package compiled against : $FLOOR"
    echo "  site running against     : $V"

    # The probe exits the site itself once it has a verdict; this watchdog is only for a hung boot.
    # (No `timeout` binary on macOS, so do it by hand.)
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
    ConnectionStrings__umbracoDbDSN="Data Source=|DataDirectory|/$DB;Cache=Shared;Foreign Keys=True;Pooling=True" \
    LinkAudit__ExternalTimeoutSeconds=5 \
    LINKAUDIT_PROBE_EXIT=1 \
    dotnet run --project "$SITE" --no-build --property:UmbracoVersion="$V" >> "$LOG" 2>&1 &
    SITE_PID=$!

    ( sleep 420; kill -9 "$SITE_PID" 2>/dev/null ) &
    WATCHDOG=$!

    wait "$SITE_PID"
    kill "$WATCHDOG" 2>/dev/null
    wait "$WATCHDOG" 2>/dev/null

    if grep -q "PROBE: PASS" "$LOG"; then
        echo "  RESULT: PASS"
        grep -E "PROBE: (running against|report|Name declared|Cultures declared)" "$LOG" | sed 's/^/    /'
    else
        echo "  RESULT: FAIL — see $LOG"
        grep -E "PROBE:|MissingMethod|TypeLoad|Unhandled exception" "$LOG" | head -20 | sed 's/^/    /'
        FAILED+=("$V")
    fi
    echo
done

if [ ${#FAILED[@]} -eq 0 ]; then
    echo "All majors passed: ${VERSIONS[*]}"
    exit 0
fi

echo "FAILED: ${FAILED[*]}"
exit 1
