#!/usr/bin/env bash
set -euo pipefail

PROD=false
CLEAN_DEPS=false
UPLOAD=false
R2_BUCKET="bppinstaller"

usage() {
    cat <<'EOF'
Usage:
  ./build.sh
      Start the local Tauri dev app.

  ./build.sh --prod
      Build release artifacts for the current host platform.
      On macOS this produces an arm64 app bundle.
      Also runs version sync, prebuild checks, and loads updater signing
      env vars from signing-secrets/ when not already exported.

  ./build.sh --upload
      Upload the current host platform release artifacts to Cloudflare R2
      using the project-pinned Wrangler CLI.

  ./build.sh --prod --upload
      Build the current host platform release artifacts, then upload them
      to Cloudflare R2 using the project-pinned Wrangler CLI.

  ./build.sh --prod --clean-deps
      Reinstall npm dependencies before building.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MACOS_TRAMPOLINE_STUB="$SCRIPT_DIR/src-tauri/resources/Trampoline/macos/bpp_launcher"
SIGNING_SECRETS_DIR="${BPP_SIGNING_SECRETS_DIR:-$SCRIPT_DIR/signing-secrets}"
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

load_required_secret_env() {
    local name="$1"
    local path="$2"
    local value="${!name:-}"

    if [ -n "$value" ]; then
        echo "==> Reusing existing $name from environment"
    elif [ -f "$path" ]; then
        echo "==> Loading $name from signing-secrets"
        value="$(<"$path")"
    else
        echo "Error: Missing $name." >&2
        echo "Set $name or create $path" >&2
        exit 1
    fi

    value="$(trim_trailing_newlines "$value")"
    if [ -z "$value" ]; then
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

package_version() {
    node -p "JSON.parse(require('fs').readFileSync('package.json', 'utf8')).version"
}

updater_endpoint_url() {
    node -p "JSON.parse(require('fs').readFileSync('src-tauri/tauri.conf.json', 'utf8')).plugins.updater.endpoints[0]"
}

public_base_url() {
    local endpoint
    endpoint="$(updater_endpoint_url)"
    printf '%s\n' "${endpoint%/latest.json}"
}

release_platforms_cli() {
    node "$SCRIPT_DIR/scripts/release/release-platforms.mjs" "$@"
}

platform_r2_key() {
    local platform="$1"

    if ! release_platforms_cli r2-key "$platform"; then
        echo "Error: Unsupported platform for upload: $platform" >&2
        return 1
    fi
}

bundle_root_for_platform() {
    local platform="$1"
    local relative_path=""

    if ! relative_path="$(release_platforms_cli bundle-root "$platform")"; then
        echo "Error: Unsupported platform bundle root: $platform" >&2
        return 1
    fi
    printf '%s' "$SCRIPT_DIR/$relative_path"
}

install_dependencies() {
    local allow_reuse="${1:-false}"
    if [ "$allow_reuse" = true ] \
        && [ "$CLEAN_DEPS" = false ] \
        && [ -d "$SCRIPT_DIR/node_modules" ] \
        && [ -d "$SCRIPT_DIR/node_modules/@tauri-apps/cli" ] \
        && { [ -f "$SCRIPT_DIR/node_modules/.bin/tauri" ] || [ -f "$SCRIPT_DIR/node_modules/.bin/tauri.cmd" ]; } \
        && npm ls --depth=0 >/dev/null 2>&1; then
        echo "==> Reusing existing npm dependencies"
        echo "    Remove node_modules or rerun with --clean-deps to force a reinstall."
        return
    fi

    if [ ! -f "$SCRIPT_DIR/package-lock.json" ]; then
        echo "Error: package-lock.json is required for a reproducible dependency install; refusing to run npm install." >&2
        return 1
    fi

    invoke_step "Installing npm dependencies" npm ci
}

required_rust_targets_for_platform() {
    local platform="$1"

    release_platforms_cli rust-targets "$platform"
}

ensure_required_rust_targets() {
    local platform="$1"
    local installed_targets=""
    local required_targets=""
    local required_target=""

    required_targets="$(required_rust_targets_for_platform "$platform")"
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
    if [ -n "${TAURI_SIGNING_PRIVATE_KEY:-}" ]; then
        echo "==> Reusing existing TAURI_SIGNING_PRIVATE_KEY from environment"
    elif [ -f "$SIGNING_KEY_PATH" ]; then
        echo "==> Loading TAURI_SIGNING_PRIVATE_KEY from signing-secrets"
        TAURI_SIGNING_PRIVATE_KEY="$(<"$SIGNING_KEY_PATH")"
        export TAURI_SIGNING_PRIVATE_KEY
    else
        echo "Error: Missing updater signing key." >&2
        echo "Set TAURI_SIGNING_PRIVATE_KEY or create $SIGNING_KEY_PATH" >&2
        exit 1
    fi

    TAURI_SIGNING_PRIVATE_KEY="$(trim_trailing_newlines "$TAURI_SIGNING_PRIVATE_KEY")"
    export TAURI_SIGNING_PRIVATE_KEY

    if [ -n "${TAURI_SIGNING_PRIVATE_KEY_PASSWORD:-}" ]; then
        echo "==> Reusing existing TAURI_SIGNING_PRIVATE_KEY_PASSWORD from environment"
    elif [ -f "$SIGNING_KEY_PASSWORD_PATH" ]; then
        echo "==> Loading TAURI_SIGNING_PRIVATE_KEY_PASSWORD from signing-secrets"
        TAURI_SIGNING_PRIVATE_KEY_PASSWORD="$(<"$SIGNING_KEY_PASSWORD_PATH")"
        TAURI_SIGNING_PRIVATE_KEY_PASSWORD="$(trim_trailing_newlines "$TAURI_SIGNING_PRIVATE_KEY_PASSWORD")"
    else
        echo "==> No updater key password configured; using empty password"
        TAURI_SIGNING_PRIVATE_KEY_PASSWORD=""
    fi

    export TAURI_SIGNING_PRIVATE_KEY_PASSWORD
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
        value="$SCRIPT_DIR/$value"
    fi

    set_exported_env APPLE_API_KEY_PATH "$value"
    assert_file "$APPLE_API_KEY_PATH" "Apple API key file"
}

load_macos_developer_id_env() {
    load_apple_signing_identity_env
    load_required_secret_env APPLE_API_ISSUER "$APPLE_API_ISSUER_PATH"
    load_required_secret_env APPLE_API_KEY "$APPLE_API_KEY_ID_PATH"
    load_apple_api_key_path_env
}

is_macho_file() {
    local file_path="$1"

    file "$file_path" | grep -q 'Mach-O'
}

macos_resource_relative_path() {
    local resource_path="$1"
    local resource_root="$SCRIPT_DIR/src-tauri/resources/"

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

# Signs nested app code from the inside out, then signs the bundle itself so
# Contents/_CodeSignature/CodeResources matches the final executable bytes.
sign_macos_resource_app_bundles() {
    local payload_dir="$1"
    local app_bundle=""
    local app_relative_path=""
    local binary_path=""
    local binary_relative_path=""

    while IFS= read -r -d '' app_bundle; do
        app_relative_path="${app_bundle#$payload_dir/}"
        # This is an install-path overlay, not a complete application bundle.
        if [ "$app_relative_path" = "$MACOS_GAME_APP_OVERLAY" ]; then
            continue
        fi

        while IFS= read -r -d '' binary_path; do
            if ! is_macho_file "$binary_path"; then
                continue
            fi

            binary_relative_path="${binary_path#$payload_dir/}"
            sign_macos_resource_binary "$binary_path" "$binary_relative_path"
        done < <(find "$app_bundle/Contents" -type f -print0)

        invoke_step "Signing macOS resource app bundle $app_relative_path" \
            codesign --force --options runtime --timestamp \
            --sign "$APPLE_SIGNING_IDENTITY" "$app_bundle"
        invoke_step "Verifying macOS resource app bundle $app_relative_path" \
            codesign --verify --deep --strict --verbose=2 "$app_bundle"
    done < <(find "$payload_dir" -type d -name '*.app' -prune -print0)
}

sign_macos_resource_plugin_bundles() {
    local payload_dir="$1"
    local plugin_bundle=""
    local plugin_relative_path=""
    local binary_path=""
    local binary_relative_path=""

    while IFS= read -r -d '' plugin_bundle; do
        plugin_relative_path="${plugin_bundle#$payload_dir/}"
        if [ "$plugin_relative_path" = "$REPLAY_RECORDER_RELATIVE_BUNDLE" ]; then
            assert_ad_hoc_replay_recorder_input "$plugin_bundle"
        fi

        while IFS= read -r -d '' binary_path; do
            if ! is_macho_file "$binary_path"; then
                continue
            fi
            binary_relative_path="${binary_path#$payload_dir/}"
            sign_macos_resource_binary "$binary_path" "$binary_relative_path"
        done < <(find "$plugin_bundle/Contents" -type f -print0)

        invoke_step "Signing macOS resource plugin bundle $plugin_relative_path" \
            codesign --force --options runtime --timestamp \
            --sign "$APPLE_SIGNING_IDENTITY" "$plugin_bundle"
        invoke_step "Verifying macOS resource plugin bundle $plugin_relative_path" \
            codesign --verify --deep --strict --verbose=2 "$plugin_bundle"

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

    node "$SCRIPT_DIR/scripts/release/payload-zip.mjs" pack \
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
    invoke_step "Synchronizing package versions" node scripts/release/version-sync.mjs
    invoke_step "Verifying pinned native replay recorder inputs" \
        npm run verify:native-recorder-input
    invoke_step "Preparing $platform release resources" \
        npm run prepare:resources -- --platform "$platform"
    invoke_step "Running authoritative release verification" \
        npm run verify -- --release-platform "$platform"
}

upload_r2_object() {
    local file_path="$1"
    local object_key="$2"
    local content_type="${3:-}"

    assert_file "$file_path" "upload artifact"
    if [ -n "$content_type" ]; then
        invoke_step "Uploading $(basename "$file_path") to $object_key" \
            wrangler_cli r2 object put "$R2_BUCKET/$object_key" --file "$file_path" --content-type "$content_type" --remote
    else
        invoke_step "Uploading $(basename "$file_path") to $object_key" \
            wrangler_cli r2 object put "$R2_BUCKET/$object_key" --file "$file_path" --remote
    fi
}

artifact_manifest_paths() {
    local platform="$1"
    node "$SCRIPT_DIR/scripts/release/artifact-manifest.mjs" paths --platform "$platform"
}

upload_release_assets() {
    local platform="$1"
    local version="$2"
    local platform_key="$3"
    local base_url="$4"
    local installer_file=""
    local updater_file=""
    local updater_sig=""
    local fragment_file=""

    if ! {
        IFS= read -r installer_file
        IFS= read -r updater_file
        IFS= read -r updater_sig
    } < <(artifact_manifest_paths "$platform"); then
        echo "Error: No valid artifact manifest for platform: $platform" >&2
        exit 1
    fi

    upload_r2_object \
        "$installer_file" \
        "$version/$platform_key/installer/$(basename "$installer_file")"

    upload_r2_object \
        "$updater_file" \
        "$version/$platform_key/updater/$(basename "$updater_file")"

    upload_r2_object \
        "$updater_sig" \
        "$version/$platform_key/updater/$(basename "$updater_sig")"

    fragment_file="$SCRIPT_DIR/src-tauri/target/platform-manifest.$platform_key.json"
    node "$SCRIPT_DIR/scripts/release/generate-platform-manifest.mjs" \
        "$fragment_file" "$platform_key" "$base_url" "$version" "$updater_file" "$updater_sig"

    upload_r2_object \
        "$fragment_file" \
        "$version/$platform_key/updater/platform-manifest.json" \
        "application/json"
    rm -f "$fragment_file"
}

generate_latest_manifest() {
    local version="$1"
    local latest_file=""
    local temp_dir=""
    local platform_list=""
    local platform=""

    latest_file="$(mktemp)"
    temp_dir="$(mktemp -d)"
    platform_list="$(release_platforms_cli list)"

    while IFS= read -r platform; do
        [ -n "$platform" ] || continue
        echo "==> Fetching platform fragment for $platform"
        wrangler_cli r2 object get \
            "$R2_BUCKET/$version/$platform/updater/platform-manifest.json" \
            --file "$temp_dir/$platform.json" \
            --remote >/dev/null 2>&1 || true
    done <<<"$platform_list"

    echo "==> Fetching existing latest.json if present"
    wrangler_cli r2 object get \
        "$R2_BUCKET/latest.json" \
        --file "$temp_dir/existing-latest.json" \
        --remote >/dev/null 2>&1 || true

    node "$SCRIPT_DIR/scripts/release/generate-latest-manifest.mjs" \
        --output "$latest_file" \
        --version "$version" \
        --temp-dir "$temp_dir"

    echo "==> Generated latest.json preview"
    cat "$latest_file"
    upload_r2_object "$latest_file" "latest.json" "application/json"
    rm -f "$latest_file"
    rm -rf "$temp_dir"
}

wrangler_cli() {
    local executable="$SCRIPT_DIR/node_modules/.bin/wrangler"
    if [ ! -f "$executable" ]; then
        echo "Error: Missing project-pinned Wrangler CLI: $executable" >&2
        return 1
    fi
    "$executable" "$@"
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
    local release_config="$SCRIPT_DIR/src-tauri/tauri.release.conf.json"
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

    config="$SCRIPT_DIR/$config"
    resource_zip="$SCRIPT_DIR/$resource_zip"
    bundle_output="$SCRIPT_DIR/$bundle_output"
    bundle_cleanup_path="$SCRIPT_DIR/$bundle_cleanup_path"
    release_binary="$SCRIPT_DIR/$release_binary"

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

    invoke_step "Writing $platform artifact manifest" \
        node "$SCRIPT_DIR/scripts/release/artifact-manifest.mjs" generate --platform "$platform"

    echo
    echo "Build complete."
    echo "Binary:  $release_binary"
    echo "Bundle:  $bundle_output"
}

parse_args() {
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --prod)
                PROD=true
                ;;
            --upload)
                UPLOAD=true
                ;;
            --clean-deps)
                CLEAN_DEPS=true
                ;;
            -h|--help)
                usage
                exit 0
                ;;
            *)
                echo "Unknown argument: $1" >&2
                usage
                exit 1
                ;;
        esac
        shift
    done
}

main() {
    local platform=""
    local version=""
    local platform_key=""
    local base_url=""
    local node_version=""
    local npm_version=""

    parse_args "$@"

    cd "$SCRIPT_DIR"

    assert_command node "Install Node.js first."
    assert_command npm "Install Node.js/npm first."
    node_version="$(node --version)"
    npm_version="$(npm --version)"
    node scripts/checks/check-toolchain.mjs "$node_version" "$npm_version"
    if [ "$PROD" = false ] && [ "$UPLOAD" = false ]; then
        assert_command cargo "Install Rust toolchain first."
        install_dependencies true
        invoke_step "Starting dev server" npm run tauri dev
        exit 0
    fi

    platform="$(current_platform)"
    if [ "$platform" = "unknown" ]; then
        echo "Error: Unsupported host platform: $(uname -s)" >&2
        exit 1
    fi

    version="$(package_version)"
    platform_key="$(platform_r2_key "$platform")"
    base_url="$(public_base_url)"

    if [ "$PROD" = true ]; then
        assert_command cargo "Install Rust toolchain first."
        install_dependencies false

        if [ "$platform" = "macos" ]; then
            assert_command rustup "Install rustup first so the macOS Rust target can be managed."
        fi

        load_updater_signing_env
        if [ "$platform" = "macos" ]; then
            load_macos_developer_id_env
        fi
        run_release_prechecks "$platform"
        ensure_required_rust_targets "$platform"
        version="$(package_version)"
        build_prod "$platform"
    fi

    if [ "$UPLOAD" = true ]; then
        if [ "$PROD" = false ]; then
            install_dependencies false
        fi
        upload_release_assets "$platform" "$version" "$platform_key" "$base_url"
        generate_latest_manifest "$version"
    fi
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
