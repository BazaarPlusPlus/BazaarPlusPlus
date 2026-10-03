#!/usr/bin/env bash
# The GitHub secrets .github/workflows/release.yml consumes: which ones a run
# needs, where each value comes from, and how the signing ones reach
# bazaarplusplus-installer/scripts/bundle.sh. The workflow maps every secret into
# an environment variable of the same name before calling this script, so a
# missing secret is an empty variable here. Values are never printed; the
# release-secrets.test.mjs beside this file is the executable specification.
#
#   release-secrets.sh check <macos|windows> <prepare|release>
#       Exit 1 with one ::error per missing secret, naming its source.
#   release-secrets.sh stage <macos|windows> <directory>
#       Write the files bundle.sh reads from BPP_SIGNING_SECRETS_DIR.
#   release-secrets.sh names | optional
#       List every secret name, or the ones a run may leave unset (for the test).
set -euo pipefail

# name|source: the local file or config.ini key the value is copied from. The
# local layout is documented by `node scripts/workspace.mjs --help`.
SECRET_SOURCES=(
    'BPP_GAME_LIBS_R2_ACCOUNT_ID|Cloudflare account id; config.ini [release] BPP_R2_ACCOUNT_ID'
    'BPP_GAME_LIBS_R2_ACCESS_KEY_ID|read-only R2 API token limited to the bazaarplusplus-game-libs bucket (Cloudflare dashboard)'
    'BPP_GAME_LIBS_R2_SECRET_ACCESS_KEY|secret of that read-only R2 API token'
    'BPP_R2_ACCOUNT_ID|config.ini [release] BPP_R2_ACCOUNT_ID'
    'BPP_R2_ACCESS_KEY_ID|config.ini [release] BPP_R2_ACCESS_KEY_ID (installer bucket write token)'
    'BPP_R2_SECRET_ACCESS_KEY|config.ini [release] BPP_R2_SECRET_ACCESS_KEY'
    'TAURI_SIGNING_PRIVATE_KEY|contents of keys/tauri-updater.key'
    'TAURI_SIGNING_PRIVATE_KEY_PASSWORD|config.ini [signing] TAURI_SIGNING_PRIVATE_KEY_PASSWORD; optional, omit for an unencrypted key'
    'APPLE_SIGNING_IDENTITY|config.ini [signing] APPLE_SIGNING_IDENTITY'
    'APPLE_API_ISSUER|config.ini [signing] APPLE_API_ISSUER'
    'APPLE_API_KEY|config.ini [signing] APPLE_API_KEY'
    'APPLE_API_KEY_P8|contents of keys/AuthKey_<APPLE_API_KEY>.p8'
    'APPLE_CERTIFICATE|base64 of the Developer ID Application certificate and its private key exported from Keychain Access as .p12'
    'APPLE_CERTIFICATE_PASSWORD|the password given to that .p12 export'
)
OPTIONAL_SECRETS=(TAURI_SIGNING_PRIVATE_KEY_PASSWORD)

snapshot_store_secrets() {
    echo BPP_GAME_LIBS_R2_ACCOUNT_ID BPP_GAME_LIBS_R2_ACCESS_KEY_ID BPP_GAME_LIBS_R2_SECRET_ACCESS_KEY
}

# Signing and upload secrets for one platform, on one line; Apple material is
# macOS only.
release_secrets() {
    local platform="$1"
    local names="TAURI_SIGNING_PRIVATE_KEY BPP_R2_ACCOUNT_ID BPP_R2_ACCESS_KEY_ID BPP_R2_SECRET_ACCESS_KEY"
    if [[ "$platform" == macos ]]; then
        names+=" APPLE_SIGNING_IDENTITY APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_P8 APPLE_CERTIFICATE APPLE_CERTIFICATE_PASSWORD"
    fi
    echo "$names"
}

source_of() {
    local entry
    for entry in "${SECRET_SOURCES[@]}"; do
        if [[ "${entry%%|*}" == "$1" ]]; then
            printf '%s' "${entry#*|}"
            return
        fi
    done
    echo "release-secrets.sh: $1 has no recorded source" >&2
    exit 2
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

cmd_check() {
    local platform="$1" stage="$2"
    assert_platform "$platform"
    local required=()
    case "$stage" in
        prepare) read -r -a required <<<"$(snapshot_store_secrets)" ;;
        release) read -r -a required <<<"$(snapshot_store_secrets) $(release_secrets "$platform")" ;;
        *)
            echo "release-secrets.sh: stage must be prepare or release, got '$stage'" >&2
            exit 2
            ;;
    esac
    local missing=0 name
    for name in "${required[@]}"; do
        if [[ -z "${!name:-}" ]]; then
            echo "::error title=Missing secret::$name is not set for this run; its value is: $(source_of "$name")"
            missing=$((missing + 1))
        fi
    done
    if ((missing > 0)); then
        echo "$missing required secret(s) missing for the $platform $stage stage; add them to the release environment (docs/release.md)." >&2
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

# The file layout load_updater_signing_env and load_macos_developer_id_env in
# bundle.sh read; APPLE_API_KEY_PATH is inferred from AuthKey_<APPLE_API_KEY>.p8.
cmd_stage() {
    local platform="$1" directory="$2"
    assert_platform "$platform"
    [[ -n "$directory" ]] || {
        echo "release-secrets.sh: stage needs a directory" >&2
        exit 2
    }
    local name
    for name in $(release_secrets "$platform"); do
        case "$name" in
            BPP_R2_*) continue ;;
        esac
        [[ -n "${!name:-}" ]] || {
            echo "::error title=Missing secret::$name is not set; its value is: $(source_of "$name")"
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
    echo "Usage: release-secrets.sh check <macos|windows> <prepare|release> | stage <macos|windows> <directory> | names | optional" >&2
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
        names) printf '%s\n' "${SECRET_SOURCES[@]%%|*}" ;;
        optional) printf '%s\n' "${OPTIONAL_SECRETS[@]}" ;;
        *) usage ;;
    esac
}

main "$@"
