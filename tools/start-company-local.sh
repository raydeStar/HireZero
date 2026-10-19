#!/bin/sh
# Starts the company workspace in local owner mode on macOS and Linux, as tools/Start-CompanyLocal.ps1 does on any
# platform with PowerShell. Settings go to the host process only. See README and docs/adr/0026.
set -eu

usage() {
    echo "usage: $0 --data-root DIR [--owner-login EMAIL] [--text-model ROUTE] [--coding-model ROUTE]" >&2
    echo "          [--codex --codex-executable FILE --codex-identity ID --codex-home DIR [--coding [--coding-read DIR]...]]" >&2
    exit 2
}

absolute() { (cd "$(dirname "$1")" 2>/dev/null && printf '%s/%s\n' "$(pwd)" "$(basename "$1")") || printf '%s\n' "$1"; }

data_root= owner= text_model=openai/gpt-6-luna coding_model=openai/gpt-6.1-sol
codex= codex_executable= codex_identity= codex_home= coding= coding_read=
while [ $# -gt 0 ]; do
    case "$1" in
        --data-root) data_root=$2; shift 2 ;;
        --owner-login) owner=$2; shift 2 ;;
        --text-model) text_model=$2; shift 2 ;;
        --coding-model) coding_model=$2; shift 2 ;;
        --codex) codex=1; shift ;;
        --codex-executable) codex_executable=$2; shift 2 ;;
        --codex-identity) codex_identity=$2; shift 2 ;;
        --codex-home) codex_home=$2; shift 2 ;;
        --coding) coding=1; shift ;;
        --coding-read) coding_read=${coding_read:+$coding_read;}$(absolute "$2"); shift 2 ;;
        *) usage ;;
    esac
done
[ -n "$data_root" ] || usage

here=$(cd "$(dirname "$0")/.." && pwd)
if [ -f "$here/app/HireZero.Host.dll" ]; then
    project=$here/app binary=$here/app/HireZero.Host.dll
else
    project=$here/src/HireZero.Host binary=$here/src/HireZero.Host/bin/Debug/net10.0/HireZero.Host.dll
fi
[ -f "$binary" ] || { echo "Build HireZero.Host and the web frontend before starting company-local mode." >&2; exit 1; }

set -- --company-local
codex_settings=
if [ -n "$codex" ]; then
    if [ -z "$codex_executable" ] || [ -z "$codex_identity" ] || [ -z "$codex_home" ]; then
        echo "Select an exact Codex executable, reviewed bundle identity and dedicated profile home." >&2
        exit 1
    fi
    set -- "$@" --company-codex
    codex_settings=1
fi
if [ -n "$coding" ]; then
    [ -n "$codex" ] || { echo "Live coding needs --codex with the pinned Codex executable." >&2; exit 1; }
    set -- "$@" --company-coding
fi

cd "$project"
exec env \
    HIREZERO_LOCAL_OWNER=1 \
    ${owner:+HIREZERO_OWNER_LOGIN="$owner"} \
    HIREZERO_DATA_ROOT="$(absolute "$data_root")" \
    HIREZERO_EXECUTION_HOLD=0 \
    HIREZERO_COMPANY_TEXT_MODEL="$text_model" \
    HIREZERO_COMPANY_CODING_MODEL="$coding_model" \
    ${codex_settings:+HIREZERO_COMPANY_CODEX_EXECUTABLE="$codex_executable"} \
    ${codex_settings:+HIREZERO_COMPANY_CODEX_IDENTITY="$codex_identity"} \
    ${codex_settings:+HIREZERO_COMPANY_CODEX_HOME="$(absolute "$codex_home")"} \
    ${coding:+HIREZERO_COMPANY_CODING_READ="$coding_read"} \
    dotnet "$binary" "$@"
