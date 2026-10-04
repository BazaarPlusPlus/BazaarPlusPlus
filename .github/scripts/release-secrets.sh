#!/usr/bin/env bash
# The GitHub secrets and variables .github/workflows/release.yml consumes:
# which ones a run needs and how the signing ones reach
# bazaarplusplus-installer/scripts/bundle.sh. The names, scopes and local
# sources live in release/github-secrets.json, read through
# release/github-secrets.mjs; this script restates none of them. The workflow
# maps every secret and variable into an environment variable of the same name
# before calling this script, so a missing one is an empty variable here.
# Values are never printed; release-secrets.test.mjs beside this file is the
# executable specification.
#
#   release-secrets.sh check <macos|windows> <prepare|release>
#       Exit 1 with one ::error per missing name, naming its local source.
#   release-secrets.sh stage <macos|windows> <directory>
#       Write the files bundle.sh reads from BPP_SIGNING_SECRETS_DIR.
#   release-secrets.sh names
#       List every name release.yml reads (for the test).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TABLE="$SCRIPT_DIR/../../release/github-secrets.mjs"

table() {
    node "$TABLE" "$@"
}

assert_platform() {
    case "$1" in
        macos | windows) ;;
        *)
            echo "release-secrets.sh: platform must be macos or windows, got '$1'" >&2
            exit 2
            ;;
    esac
}

report_missing() {
    echo "::error title=Missing secret::$1 is not set for this run; its value is: $(table source "$1")"
}

cmd_check() {
    local platform="$1" stage="$2"
    assert_platform "$platform"
    case "$stage" in
        prepare | release) ;;
        *)
            echo "release-secrets.sh: stage must be prepare or release, got '$stage'" >&2
            exit 2
            ;;
    esac
    local required=()
    read -r -a required <<<"$(table required "$platform" "$stage" | tr '\n' ' ')"
    local missing=0 name
    for name in "${required[@]}"; do
        if [[ -z "${!name:-}" ]]; then
            report_missing "$name"
            missing=$((missing + 1))
        fi
    done
    if ((missing > 0)); then
        echo "$missing required secret(s) missing for the $platform $stage stage; add them with 'just secrets-sync' (docs/release.md)." >&2
        exit 1
    fi
    echo "All ${#required[@]} secrets for the $platform $stage stage are present."
}

write_secret_file() {
    local directory="$1" file="$2" name="$3"
    (
        umask 077
        printf '%s' "${!name}" >"$directory/$file"
    )
}

# The names stage writes as files, in the layout load_updater_signing_env and
# load_macos_developer_id_env in bundle.sh read; APPLE_API_KEY_PATH is inferred
# from AuthKey_<APPLE_API_KEY>.p8. TAURI_SIGNING_PRIVATE_KEY_PASSWORD is not in
# the table (the updater key has no password) but is still staged when set.
staged_names() {
    local platform="$1"
    local names="TAURI_SIGNING_PRIVATE_KEY"
    if [[ "$platform" == macos ]]; then
        names+=" APPLE_SIGNING_IDENTITY APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_P8"
    fi
    echo "$names"
}

cmd_stage() {
    local platform="$1" directory="$2"
    assert_platform "$platform"
    [[ -n "$directory" ]] || {
        echo "release-secrets.sh: stage needs a directory" >&2
        exit 2
    }
    local name
    for name in $(staged_names "$platform"); do
        [[ -n "${!name:-}" ]] || {
            report_missing "$name"
            exit 1
        }
    done
    (
        umask 077
        mkdir -p "$directory"
    )
    write_secret_file "$directory" tauri-updater.key TAURI_SIGNING_PRIVATE_KEY
    if [[ -n "${TAURI_SIGNING_PRIVATE_KEY_PASSWORD:-}" ]]; then
        write_secret_file "$directory" tauri-updater.password TAURI_SIGNING_PRIVATE_KEY_PASSWORD
    fi
    if [[ "$platform" == macos ]]; then
        write_secret_file "$directory" apple-signing-identity APPLE_SIGNING_IDENTITY
        write_secret_file "$directory" apple-api-issuer APPLE_API_ISSUER
        write_secret_file "$directory" apple-api-key APPLE_API_KEY
        write_secret_file "$directory" "AuthKey_${APPLE_API_KEY}.p8" APPLE_API_KEY_P8
    fi
    echo "Staged $platform signing secrets into $directory"
}

usage() {
    echo "Usage: release-secrets.sh check <macos|windows> <prepare|release> | stage <macos|windows> <directory> | names" >&2
    exit 2
}

main() {
    local command="${1:-}"
    shift || true
    case "$command" in
        check)
            [[ $# -eq 2 ]] || usage
            cmd_check "$@"
            ;;
        stage)
            [[ $# -eq 2 ]] || usage
            cmd_stage "$@"
            ;;
        names) table names ;;
        *) usage ;;
    esac
}

main "$@"
