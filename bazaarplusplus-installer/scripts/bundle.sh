#!/usr/bin/env bash
set -euo pipefail

# Signs and bundles the installer for the host platform. Internal to the product
# coordinator: `just release::build <platform>` (node release.mjs build) holds the
# build lock, prepares the Payload, then runs this script with BPP_RELEASE_LOCK_TOKEN.
# Functions are sourced directly by scripts/bundle.test.mjs. It lives outside
# scripts/release/ because signing is not a Payload input.

INSTALLER_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MACOS_TRAMPOLINE_STUB="$INSTALLER_ROOT/src-tauri/resources/Trampoline/macos/bpp_launcher"
SIGNING_SECRETS_DIR="${BPP_SIGNING_SECRETS_DIR:-$INSTALLER_ROOT/signing-secrets}"
SIGNING_KEY_PATH="$SIGNING_SECRETS_DIR/tauri-updater.key"
SIGNING_KEY_PASSWORD_PATH="$SIGNING_SECRETS_DIR/tauri-updater.password"
APPLE_API_ISSUER_PATH="$SIGNING_SECRETS_DIR/apple-api-issuer"
APPLE_API_KEY_ID_PATH="$SIGNING_SECRETS_DIR/apple-api-key"
APPLE_API_KEY_PATH_PATH="$SIGNING_SECRETS_DIR/apple-api-key-path"
APPLE_SIGNING_IDENTITY_PATH="$SIGNING_SECRETS_DIR/apple-signing-identity"
OFFICIAL_APPLE_TEAM_ID="9Z44S3N293"
REPLAY_RECORDER_RELATIVE_BUNDLE="TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle"
MACOS_GAME_APP_OVERLAY="TheBazaar.app"

assert_command() {
    local name="$1"
    local hint="${2:-}"
    if ! command -v "$name" &>/dev/null; then
        if [ -n "$hint" ]; then
            echo "Error: $name not found. $hint" >&2
        else
            echo "Error: $name not found." >&2
        fi
        exit 1
    fi
}

assert_file() {
    local path="$1"
    local label="$2"
    if [ ! -f "$path" ]; then
        echo "Error: Missing $label: $path" >&2
        exit 1
    fi
}

invoke_step() {
    local label="$1"
    shift
    echo "==> $label"
    "$@"
}

trim_trailing_newlines() {
    local value="$1"

    while [[ "$value" == *$'\n' || "$value" == *$'\r' ]]; do
        value="${value%$'\n'}"
        value="${value%$'\r'}"
    done

    printf '%s' "$value"
}

set_exported_env() {
    local name="$1"
    local value="$2"

    printf -v "$name" '%s' "$value"
    export "$name"
}

# load_secret_env <NAME> <file> [optional]: reuse an exported value, else read the
# ignored signing-secrets file. An optional secret defaults to empty.
load_secret_env() {
    local name="$1"
    local path="$2"
    local optional="${3:-}"
    local value="${!name:-}"

    if [ -n "$value" ]; then
        echo "==> Reusing existing $name from environment"
    elif [ -f "$path" ]; then
        echo "==> Loading $name from signing-secrets"
        value="$(<"$path")"
    elif [ -n "$optional" ]; then
        echo "==> No $name configured; using empty value"
        set_exported_env "$name" ""
        return
    else
        echo "Error: Missing $name." >&2
        echo "Set $name or create $path" >&2
        exit 1
    fi

    value="$(trim_trailing_newlines "$value")"
    if [ -z "$value" ] && [ -z "$optional" ]; then
        echo "Error: Empty $name." >&2
        echo "Set $name or write a value to $path" >&2
        exit 1
    fi

    set_exported_env "$name" "$value"
}

current_platform() {
    case "$(uname -s)" in
        Darwin) echo "macos" ;;
        MINGW*|MSYS*|CYGWIN*|Windows_NT) echo "windows" ;;
        *) echo "unknown" ;;
    esac
}

release_platforms_cli() {
    node "$INSTALLER_ROOT/../release/release-platforms.mjs" "$@"
}

install_dependencies() {
    if [ ! -f "$INSTALLER_ROOT/package-lock.json" ]; then
        echo "Error: package-lock.json is required for a reproducible dependency install; refusing to run npm install." >&2
        return 1
    fi

    invoke_step "Installing npm dependencies" npm ci
}

ensure_required_rust_targets() {
    local platform="$1"
    local installed_targets=""
    local required_targets=""
    local required_target=""

    required_targets="$(release_platforms_cli rust-targets "$platform")"
    if [ -z "${required_targets//[[:space:]]/}" ]; then
        return 0
    fi

    installed_targets="$(rustup target list --installed)"
    while IFS= read -r required_target; do
        [ -n "$required_target" ] || continue
        if ! grep -Fxq "$required_target" <<<"$installed_targets"; then
            echo "Error: Missing Rust target: $required_target" >&2
            echo "Run: rustup target add $required_target" >&2
            return 1
        fi
    done <<<"$required_targets"
}

load_updater_signing_env() {
    load_secret_env TAURI_SIGNING_PRIVATE_KEY "$SIGNING_KEY_PATH"
    load_secret_env TAURI_SIGNING_PRIVATE_KEY_PASSWORD "$SIGNING_KEY_PASSWORD_PATH" optional
}

detect_developer_id_application_identity() {
    security find-identity -v -p codesigning 2>/dev/null \
        | sed -n 's/.*"\(Developer ID Application: [^"]*\)".*/\1/p' \
        | sort -u
}

load_apple_signing_identity_env() {
    local value="${APPLE_SIGNING_IDENTITY:-}"
    local identities=""
    local identity_count=""

    if [ -n "$value" ]; then
        echo "==> Reusing existing APPLE_SIGNING_IDENTITY from environment"
    elif [ -f "$APPLE_SIGNING_IDENTITY_PATH" ]; then
        echo "==> Loading APPLE_SIGNING_IDENTITY from signing-secrets"
        value="$(<"$APPLE_SIGNING_IDENTITY_PATH")"
    else
        identities="$(detect_developer_id_application_identity)"
        identity_count="$(printf '%s\n' "$identities" | sed '/^$/d' | wc -l | tr -d ' ')"

        case "$identity_count" in
            1)
                echo "==> Auto-detected APPLE_SIGNING_IDENTITY from keychain"
                value="$identities"
                ;;
            0)
                echo "Error: Missing APPLE_SIGNING_IDENTITY." >&2
                echo "Set APPLE_SIGNING_IDENTITY, create $APPLE_SIGNING_IDENTITY_PATH, or install a Developer ID Application certificate." >&2
                exit 1
                ;;
            *)
                echo "Error: Multiple Developer ID Application identities found." >&2
                echo "Set APPLE_SIGNING_IDENTITY or create $APPLE_SIGNING_IDENTITY_PATH with the exact identity to use." >&2
                printf '%s\n' "$identities" >&2
                exit 1
                ;;
        esac
    fi

    value="$(trim_trailing_newlines "$value")"
    if [ -z "$value" ]; then
        echo "Error: Empty APPLE_SIGNING_IDENTITY." >&2
        echo "Set APPLE_SIGNING_IDENTITY or write a value to $APPLE_SIGNING_IDENTITY_PATH" >&2
        exit 1
    fi

    set_exported_env APPLE_SIGNING_IDENTITY "$value"
}

load_apple_api_key_path_env() {
    local value="${APPLE_API_KEY_PATH:-}"
    local inferred_path=""

    if [ -n "$value" ]; then
        echo "==> Reusing existing APPLE_API_KEY_PATH from environment"
    elif [ -f "$APPLE_API_KEY_PATH_PATH" ]; then
        echo "==> Loading APPLE_API_KEY_PATH from signing-secrets"
        value="$(<"$APPLE_API_KEY_PATH_PATH")"
    else
        inferred_path="$SIGNING_SECRETS_DIR/AuthKey_${APPLE_API_KEY}.p8"
        if [ -f "$inferred_path" ]; then
            echo "==> Inferring APPLE_API_KEY_PATH from signing-secrets"
            value="$inferred_path"
        else
            echo "Error: Missing APPLE_API_KEY_PATH." >&2
            echo "Set APPLE_API_KEY_PATH, create $APPLE_API_KEY_PATH_PATH, or place AuthKey_${APPLE_API_KEY}.p8 in $SIGNING_SECRETS_DIR" >&2
            exit 1
        fi
    fi

    value="$(trim_trailing_newlines "$value")"
    if [ -z "$value" ]; then
        echo "Error: Empty APPLE_API_KEY_PATH." >&2
        echo "Set APPLE_API_KEY_PATH or write a value to $APPLE_API_KEY_PATH_PATH" >&2
        exit 1
    fi

    if [[ "$value" != /* ]]; then
        value="$INSTALLER_ROOT/$value"
    fi

    set_exported_env APPLE_API_KEY_PATH "$value"
    assert_file "$APPLE_API_KEY_PATH" "Apple API key file"
}

load_macos_developer_id_env() {
    load_apple_signing_identity_env
    load_secret_env APPLE_API_ISSUER "$APPLE_API_ISSUER_PATH"
    load_secret_env APPLE_API_KEY "$APPLE_API_KEY_ID_PATH"
    load_apple_api_key_path_env
}

is_macho_file() {
    local file_path="$1"

    file "$file_path" | grep -q 'Mach-O'
}

macos_resource_relative_path() {
    local resource_path="$1"
    local resource_root="$INSTALLER_ROOT/src-tauri/resources/"

    if [[ "$resource_path" == "$resource_root"* ]]; then
        printf '%s' "${resource_path#$resource_root}"
    else
        printf '%s' "$resource_path"
    fi
}

sign_macos_resource_binary() {
    local binary_path="$1"
    local relative_path="$2"

    invoke_step "Signing macOS resource binary $relative_path" \
        codesign --force --options runtime --timestamp \
        --sign "$APPLE_SIGNING_IDENTITY" "$binary_path"
}

codesign_details() {
    local code_path="$1"
    codesign -dvvv "$code_path" 2>&1
}

assert_official_codesign_team_id() {
    local code_path="$1"
    local details=""
    local actual_team_id=""

    details="$(codesign_details "$code_path")"
    actual_team_id="$(sed -n 's/^TeamIdentifier=//p' <<<"$details" | head -n 1)"
    if [ "$actual_team_id" != "$OFFICIAL_APPLE_TEAM_ID" ]; then
        echo "Error: Official macOS release code must use TeamIdentifier=$OFFICIAL_APPLE_TEAM_ID." >&2
        echo "Found TeamIdentifier=${actual_team_id:-missing} for $code_path" >&2
        exit 1
    fi
}

assert_ad_hoc_replay_recorder_input() {
    local plugin_bundle="$1"
    local details=""

    if ! codesign --verify --deep --strict --verbose=2 "$plugin_bundle"; then
        echo "Error: Replay recorder plugin input has an invalid ad-hoc signature: $plugin_bundle" >&2
        exit 1
    fi
    details="$(codesign_details "$plugin_bundle")"
    if ! grep -q '^Signature=adhoc$' <<<"$details" \
        || ! grep -q '^TeamIdentifier=not set$' <<<"$details"; then
        echo "Error: Replay recorder plugin input must be ad-hoc signed with no TeamIdentifier." >&2
        exit 1
    fi
}

# Pre-signs Mach-O binaries that live inside bundled resource zips. Tauri treats
# zips as opaque resource data, so its outer .app signing never reaches these.
# Notarization and runtime library validation reject unsigned nested code.
sign_macos_resource_binaries() {
    local payload_dir="$1"
    local binary_path=""
    local relative_path=""

    while IFS= read -r -d '' binary_path; do
        if ! is_macho_file "$binary_path"; then
            continue
        fi

        relative_path="${binary_path#$payload_dir/}"
        sign_macos_resource_binary "$binary_path" "$relative_path"
    done < <(find "$payload_dir" \( -type d -name '*.app' -o -type d -name '*.bundle' \) -prune -o -type f -print0)
}

# Signs every Mach-O file inside a bundle, then the bundle itself, then verifies it,
# so Contents/_CodeSignature/CodeResources matches the final executable bytes.
sign_macos_bundle_inside_out() {
    local payload_dir="$1"
    local bundle="$2"
    local kind="$3"
    local relative_path="${bundle#$payload_dir/}"
    local binary_path=""

    while IFS= read -r -d '' binary_path; do
        if is_macho_file "$binary_path"; then
            sign_macos_resource_binary "$binary_path" "${binary_path#$payload_dir/}"
        fi
    done < <(find "$bundle/Contents" -type f -print0)

    invoke_step "Signing macOS resource $kind bundle $relative_path" \
        codesign --force --options runtime --timestamp \
        --sign "$APPLE_SIGNING_IDENTITY" "$bundle"
    invoke_step "Verifying macOS resource $kind bundle $relative_path" \
        codesign --verify --deep --strict --verbose=2 "$bundle"
}

sign_macos_resource_app_bundles() {
    local payload_dir="$1"
    local app_bundle=""

    while IFS= read -r -d '' app_bundle; do
        # This is an install-path overlay, not a complete application bundle.
        if [ "${app_bundle#$payload_dir/}" != "$MACOS_GAME_APP_OVERLAY" ]; then
            sign_macos_bundle_inside_out "$payload_dir" "$app_bundle" app
        fi
    done < <(find "$payload_dir" -type d -name '*.app' -prune -print0)
}

sign_macos_resource_plugin_bundles() {
    local payload_dir="$1"
    local plugin_bundle=""
    local plugin_relative_path=""

    while IFS= read -r -d '' plugin_bundle; do
        plugin_relative_path="${plugin_bundle#$payload_dir/}"
        if [ "$plugin_relative_path" = "$REPLAY_RECORDER_RELATIVE_BUNDLE" ]; then
            assert_ad_hoc_replay_recorder_input "$plugin_bundle"
        fi

        sign_macos_bundle_inside_out "$payload_dir" "$plugin_bundle" plugin

        if [ "$plugin_relative_path" = "$REPLAY_RECORDER_RELATIVE_BUNDLE" ]; then
            assert_official_codesign_team_id \
                "$plugin_bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox"
            assert_official_codesign_team_id "$plugin_bundle"
        fi
    done < <(find "$payload_dir" -type d -name '*.bundle' -prune -print0)
}

prepare_signed_macos_resource_binary() {
    local resource_binary="$1"
    local relative_path=""

    assert_command file "Install file first."
    assert_command codesign "Install Xcode command line tools first."
    assert_file "$resource_binary" "macOS resource binary"

    if ! is_macho_file "$resource_binary"; then
        echo "Error: macOS resource binary is not Mach-O: $resource_binary" >&2
        exit 1
    fi

    relative_path="$(macos_resource_relative_path "$resource_binary")"
    sign_macos_resource_binary "$resource_binary" "$relative_path"
    assert_official_codesign_team_id "$resource_binary"
}

create_zip_from_directory() {
    local source_dir="$1"
    local output_zip="$2"
    local output_manifest="$3"

    node "$INSTALLER_ROOT/../release/payload-zip.mjs" pack \
        --source "$source_dir" --output "$output_zip" \
        --manifest-output "$output_manifest" --platform macos
}

prepare_signed_macos_resource_zip() {
    local resource_zip="$1"
    local temp_dir=""
    local payload_dir=""
    local signed_zip=""
    local signed_manifest=""
    local resource_manifest="${resource_zip}.manifest.json"

    assert_command ditto "Install macOS command line tools first."
    assert_command file "Install file first."
    assert_command codesign "Install Xcode command line tools first."
    assert_command plutil "Install macOS command line tools first."
    assert_command spctl "Install macOS command line tools first."
    assert_command xcrun "Install Xcode command line tools first."
    assert_file "$resource_zip" "macOS resource zip"

    temp_dir="$(mktemp -d)"
    payload_dir="$temp_dir/payload"
    signed_zip="$temp_dir/BepInEx.zip"
    signed_manifest="$temp_dir/BepInEx.zip.manifest.json"
    mkdir -p "$payload_dir"
    trap 'rm -rf "$temp_dir"' RETURN

    invoke_step "Extracting macOS resource zip for signing" \
        ditto -x -k "$resource_zip" "$payload_dir"
    assert_ad_hoc_replay_recorder_input \
        "$payload_dir/$REPLAY_RECORDER_RELATIVE_BUNDLE"
    sign_macos_resource_binaries "$payload_dir"
    sign_macos_resource_plugin_bundles "$payload_dir"
    sign_macos_resource_app_bundles "$payload_dir"
    invoke_step "Repacking signed macOS resource zip" \
        create_zip_from_directory "$payload_dir" "$signed_zip" "$signed_manifest"
    invoke_step "Replacing macOS resource zip and checksum manifest with signed copies" \
        mv "$signed_zip" "$resource_zip"
    mv "$signed_manifest" "$resource_manifest"

    rm -rf "$temp_dir"
    trap - RETURN
}

run_release_prechecks() {
    local platform="$1"
    invoke_step "Checking product build ownership" node "$INSTALLER_ROOT/../release.mjs" assert-build-owner
    invoke_step "Running authoritative release verification" \
        npm run verify -- --release-platform "$platform"
}

build_prod() {
    local platform="$1"
    local config=""
    local resource_zip=""
    local bundle_target=""
    local bundle_output=""
    local bundle_cleanup_path=""
    local release_binary=""
    local tauri_target=""
    local release_config="$INSTALLER_ROOT/src-tauri/tauri.release.conf.json"
    local -a build_command
    local -a bundle_command

    if ! {
        IFS= read -r config
        IFS= read -r resource_zip
        IFS= read -r bundle_target
        IFS= read -r bundle_output
        IFS= read -r bundle_cleanup_path
        IFS= read -r release_binary
        IFS= read -r tauri_target
    } < <(release_platforms_cli build-env "$platform"); then
        echo "Error: Unsupported platform: $platform" >&2
        exit 1
    fi

    if [ -z "$config" ] || [ -z "$resource_zip" ] || [ -z "$bundle_target" ] \
        || [ -z "$bundle_output" ] || [ -z "$bundle_cleanup_path" ] || [ -z "$release_binary" ]; then
        echo "Error: Unsupported platform: $platform" >&2
        exit 1
    fi

    config="$INSTALLER_ROOT/$config"
    resource_zip="$INSTALLER_ROOT/$resource_zip"
    bundle_output="$INSTALLER_ROOT/$bundle_output"
    bundle_cleanup_path="$INSTALLER_ROOT/$bundle_cleanup_path"
    release_binary="$INSTALLER_ROOT/$release_binary"

    assert_file "$config" "$platform Tauri config"
    assert_file "$resource_zip" "$platform resource zip"
    assert_file "$release_config" "release Tauri config"

    if [ -d "$bundle_cleanup_path" ]; then
        invoke_step "Removing stale $platform bundle artifacts" rm -rf "$bundle_cleanup_path"
    fi

    build_command=(
        npm run tauri build -- --no-bundle --config "$config" --config "$release_config"
    )
    bundle_command=(
        npm run tauri bundle -- --bundles "$bundle_target" --config "$config" --config "$release_config"
    )

    if [ -n "$tauri_target" ]; then
        build_command+=(--target "$tauri_target")
        bundle_command+=(--target "$tauri_target")
    fi

    invoke_step "Building $platform app binary" "${build_command[@]}"

    if [ "$platform" = "macos" ]; then
        prepare_signed_macos_resource_zip "$resource_zip"
        prepare_signed_macos_resource_binary "$MACOS_TRAMPOLINE_STUB"
    fi

    invoke_step "Bundling $platform installer" "${bundle_command[@]}"

    echo
    echo "Build complete."
    echo "Binary:  $release_binary"
    echo "Bundle:  $bundle_output"
}

main() {
    local platform=""

    if [ "$#" -gt 0 ]; then
        echo "Error: bundle.sh takes no arguments; run just release::build <platform>." >&2
        exit 2
    fi
    if [ -z "${BPP_RELEASE_LOCK_TOKEN:-}" ]; then
        echo "Error: bundle.sh runs inside the product build lock; run just release::build <platform>." >&2
        exit 1
    fi

    cd "$INSTALLER_ROOT"
    node "$INSTALLER_ROOT/../release.mjs" assert-build-owner

    assert_command node "Install Node.js first."
    assert_command npm "Install Node.js/npm first."
    node scripts/checks/check-toolchain.mjs "$(node --version)" "$(npm --version)"
    assert_command cargo "Install Rust toolchain first."

    platform="$(current_platform)"
    if [ "$platform" = "unknown" ]; then
        echo "Error: Unsupported host platform: $(uname -s)" >&2
        exit 1
    fi
    if [ "$platform" = "macos" ]; then
        assert_command rustup "Install rustup first so the macOS Rust target can be managed."
    fi

    install_dependencies
    load_updater_signing_env
    if [ "$platform" = "macos" ]; then
        load_macos_developer_id_env
    fi
    run_release_prechecks "$platform"
    ensure_required_rust_targets "$platform"
    build_prod "$platform"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
