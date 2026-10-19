#!/bin/sh
# The POSIX launcher in a release layout, with a fake dotnet that records what it was given. No host, login or model
# call runs.
set -eu
here=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/release/tools" "$work/release/app" "$work/bin" "$work/data root"
cp "$here/tools/start-company-local.sh" "$work/release/tools/"
: > "$work/release/app/HireZero.Host.dll"
cat > "$work/bin/dotnet" <<'FAKE'
#!/bin/sh
{ pwd; printf '%s\n' "$@"; env | grep '^HIREZERO_' | sort; } > "$CAPTURE"
FAKE
chmod +x "$work/bin/dotnet"
checks=0
check() { checks=$((checks + 1)); grep -qxF -- "$1" "$work/capture" || { echo "FAIL: $2" >&2; cat "$work/capture" >&2; exit 1; }; }

CAPTURE=$work/capture PATH=$work/bin:$PATH sh "$work/release/tools/start-company-local.sh" \
    --data-root "$work/data root" --owner-login owner@example.invalid \
    --codex --codex-executable /opt/fictional/codex --codex-identity fictional-reviewed-bundle --codex-home "$work/data root" \
    --coding --coding-read "$work/data root"
check "$work/release/app" "A release runs the published host from app/."
check "$work/release/app/HireZero.Host.dll" "The published host is started."
check "--company-local" "Local owner mode is selected."
check "--company-coding" "Live coding is selected only when asked."
check "HIREZERO_DATA_ROOT=$work/data root" "A data root with a space is passed whole."
check "HIREZERO_OWNER_LOGIN=owner@example.invalid" "An existing owner can be selected."
check "HIREZERO_COMPANY_CODEX_IDENTITY=fictional-reviewed-bundle" "Only the pinned connection is selected."
check "HIREZERO_COMPANY_CODING_READ=$work/data root" "Read roots are absolute."
check "HIREZERO_EXECUTION_HOLD=0" "The launcher clears the hold for its own process only."

if CAPTURE=$work/capture PATH=$work/bin:$PATH sh "$work/release/tools/start-company-local.sh" --data-root "$work/data root" --coding 2>/dev/null; then
    echo "FAIL: live coding without the pinned connection must be refused" >&2
    exit 1
fi
checks=$((checks + 1))
echo "PASS $checks POSIX launcher checks (fake dotnet; no runtime, login or model call)"
