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
      using npx wrangler.

  ./build.sh --prod --upload
      Build the current host platform release artifacts, then upload them
      to Cloudflare R2 using npx wrangler.

  ./build.sh --prod --clean-deps
      Reinstall npm dependencies before building.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WINDOWS_CONFIG="$SCRIPT_DIR/src-tauri/tauri.windows.conf.json"
MACOS_CONFIG="$SCRIPT_DIR/src-tauri/tauri.macos.conf.json"
WINDOWS_ZIP="$SCRIPT_DIR/src-tauri/resources/BepInExSource/windows/BepInEx.zip"
MACOS_ZIP="$SCRIPT_DIR/src-tauri/resources/BepInExSource/macos/BepInEx.zip"
SIGNING_SECRETS_DIR="$SCRIPT_DIR/signing-secrets"
SIGNING_KEY_PATH="$SIGNING_SECRETS_DIR/tauri-updater.key"
SIGNING_KEY_PASSWORD_PATH="$SIGNING_SECRETS_DIR/tauri-updater.password"
APPLE_API_ISSUER_PATH="$SIGNING_SECRETS_DIR/apple-api-issuer"
APPLE_API_KEY_ID_PATH="$SIGNING_SECRETS_DIR/apple-api-key"
APPLE_API_KEY_PATH_PATH="$SIGNING_SECRETS_DIR/apple-api-key-path"
APPLE_SIGNING_IDENTITY_PATH="$SIGNING_SECRETS_DIR/apple-signing-identity"

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

platform_r2_key() {
    local platform="$1"

    case "$platform" in
        windows)
            printf '%s' "windows-x86_64"
            ;;
        macos)
            printf '%s' "darwin-aarch64"
            ;;
        *)
            echo "Error: Unsupported platform for upload: $platform" >&2
            exit 1
            ;;
    esac
}

bundle_root_for_platform() {
    local platform="$1"

    case "$platform" in
        windows)
            printf '%s' "$SCRIPT_DIR/src-tauri/target/release/bundle"
            ;;
        macos)
            printf '%s' "$SCRIPT_DIR/src-tauri/target/aarch64-apple-darwin/release/bundle"
            ;;
        *)
            echo "Error: Unsupported platform bundle root: $platform" >&2
            exit 1
            ;;
    esac
}

find_installer_artifact() {
    local platform="$1"

    case "$platform" in
        windows)
            find "$SCRIPT_DIR/src-tauri/target/release/bundle/nsis" -maxdepth 1 -type f -name '*.exe' ! -name '*.sig' | sort | head -n 1
            ;;
        macos)
            find "$SCRIPT_DIR/src-tauri/target/aarch64-apple-darwin/release/bundle/dmg" -maxdepth 1 -type f -name '*.dmg' | sort | head -n 1
            ;;
        *)
            echo "Error: Unsupported platform installer artifact lookup: $platform" >&2
            exit 1
            ;;
    esac
}

find_updater_signature() {
    local platform="$1"
    local bundle_root

    bundle_root="$(bundle_root_for_platform "$platform")"
    find "$bundle_root" -type f -name '*.sig' | sort | head -n 1
}

find_updater_artifact() {
    local platform="$1"
    local updater_sig
    local updater_file

    updater_sig="$(find_updater_signature "$platform")"
    if [ -z "$updater_sig" ]; then
        return
    fi

    updater_file="${updater_sig%.sig}"
    if [ ! -f "$updater_file" ]; then
        echo "Error: Missing updater artifact for signature: $updater_sig" >&2
        exit 1
    fi

    printf '%s\n' "$updater_file"
}

install_dependencies() {
    if [ "$CLEAN_DEPS" = false ] \
        && [ -d "$SCRIPT_DIR/node_modules" ] \
        && [ -d "$SCRIPT_DIR/node_modules/@tauri-apps/cli" ] \
        && { [ -f "$SCRIPT_DIR/node_modules/.bin/tauri" ] || [ -f "$SCRIPT_DIR/node_modules/.bin/tauri.cmd" ]; } \
        && npm ls --depth=0 >/dev/null 2>&1; then
        echo "==> Reusing existing npm dependencies"
        echo "    Remove node_modules or rerun with --clean-deps to force a reinstall."
        return
    fi

    if [ "$CLEAN_DEPS" = true ] && [ -f "$SCRIPT_DIR/package-lock.json" ]; then
        invoke_step "Installing npm dependencies" npm ci
    else
        invoke_step "Installing npm dependencies" npm install
    fi
}

required_rust_targets_for_platform() {
    local platform="$1"

    case "$platform" in
        macos)
            printf '%s\n' aarch64-apple-darwin
            ;;
        *)
            ;;
    esac
}

ensure_required_rust_targets() {
    local platform="$1"
    local installed_targets=""
    local required_target=""

    installed_targets="$(rustup target list --installed)"
    while IFS= read -r required_target; do
        [ -n "$required_target" ] || continue
        if ! grep -Fxq "$required_target" <<<"$installed_targets"; then
            echo "Error: Missing Rust target: $required_target" >&2
            echo "Run: rustup target add $required_target" >&2
            return 1
        fi
    done < <(required_rust_targets_for_platform "$platform")
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

# Pre-signs Mach-O binaries that live inside BepInEx.zip. Tauri treats the zip
# as opaque resource data, so its outer .app signing never reaches these. The
# game later loads them under hardened runtime + library validation, which
# rejects unsigned dylibs.
sign_macos_resource_binaries() {
    local payload_dir="$1"
    local binary_path=""
    local relative_path=""

    while IFS= read -r -d '' binary_path; do
        if ! is_macho_file "$binary_path"; then
            continue
        fi

        relative_path="${binary_path#$payload_dir/}"
        invoke_step "Signing macOS resource binary $relative_path" \
            codesign --force --options runtime --timestamp \
            --sign "$APPLE_SIGNING_IDENTITY" "$binary_path"
    done < <(find "$payload_dir" -type f -print0)
}

create_zip_from_directory() {
    local source_dir="$1"
    local output_zip="$2"

    (
        cd "$source_dir"
        zip -qry -X "$output_zip" .
    )
}

prepare_signed_macos_resource_zip() {
    local resource_zip="$1"
    local temp_dir=""
    local payload_dir=""
    local signed_zip=""

    assert_command ditto "Install macOS command line tools first."
    assert_command zip "Install zip first."
    assert_command file "Install file first."
    assert_command codesign "Install Xcode command line tools first."
    assert_file "$resource_zip" "macOS resource zip"

    temp_dir="$(mktemp -d)"
    payload_dir="$temp_dir/payload"
    signed_zip="$temp_dir/BepInEx.zip"
    mkdir -p "$payload_dir"
    trap 'rm -rf "$temp_dir"' RETURN

    invoke_step "Extracting macOS resource zip for signing" \
        ditto -x -k "$resource_zip" "$payload_dir"
    sign_macos_resource_binaries "$payload_dir"
    invoke_step "Repacking signed macOS resource zip" \
        create_zip_from_directory "$payload_dir" "$signed_zip"
    invoke_step "Replacing macOS resource zip with signed copy" \
        mv "$signed_zip" "$resource_zip"

    rm -rf "$temp_dir"
    trap - RETURN
}

run_release_prechecks() {
    invoke_step "Synchronizing package versions" node scripts/version-sync.mjs
    invoke_step "Running prebuild checks" npm run prebuild-check
}

upload_r2_object() {
    local file_path="$1"
    local object_key="$2"
    local content_type="${3:-}"

    assert_file "$file_path" "upload artifact"
    if [ -n "$content_type" ]; then
        invoke_step "Uploading $(basename "$file_path") to $object_key" \
            npx wrangler r2 object put "$R2_BUCKET/$object_key" --file "$file_path" --content-type "$content_type" --remote
    else
        invoke_step "Uploading $(basename "$file_path") to $object_key" \
            npx wrangler r2 object put "$R2_BUCKET/$object_key" --file "$file_path" --remote
    fi
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

    installer_file="$(find_installer_artifact "$platform")"
    updater_file="$(find_updater_artifact "$platform")"
    updater_sig="$(find_updater_signature "$platform")"

    if [ -z "$installer_file" ]; then
        echo "Error: No installer artifact found for platform: $platform" >&2
        exit 1
    fi

    if [ -z "$updater_file" ] || [ -z "$updater_sig" ]; then
        echo "Error: No updater artifact/signature pair found for platform: $platform" >&2
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
    node "$SCRIPT_DIR/scripts/generate-platform-manifest.mjs" \
        "$fragment_file" "$platform_key" "$base_url" "$version" "$updater_file" "$updater_sig"

    upload_r2_object \
        "$fragment_file" \
        "$version/$platform_key/updater/platform-manifest.json" \
        "application/json"
    rm -f "$fragment_file"
}

generate_latest_manifest() {
    local version="$1"
    local base_url="$2"
    local latest_file=""
    local temp_dir=""
    local platform=""

    latest_file="$(mktemp)"
    temp_dir="$(mktemp -d)"

    for platform in windows-x86_64 darwin-aarch64; do
        echo "==> Fetching platform fragment for $platform"
        npx wrangler r2 object get \
            "$R2_BUCKET/$version/$platform/updater/platform-manifest.json" \
            --file "$temp_dir/$platform.json" \
            --remote >/dev/null 2>&1 || true
    done

    echo "==> Fetching existing latest.json if present"
    npx wrangler r2 object get \
        "$R2_BUCKET/latest.json" \
        --file "$temp_dir/existing-latest.json" \
        --remote >/dev/null 2>&1 || true

    node "$SCRIPT_DIR/scripts/generate-latest-manifest.mjs" \
        "$latest_file" "$version" "$base_url" "$temp_dir"

    echo "==> Generated latest.json preview"
    cat "$latest_file"
    upload_r2_object "$latest_file" "latest.json" "application/json"
    rm -f "$latest_file"
    rm -rf "$temp_dir"
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
    local -a build_command
    local -a bundle_command

    case "$platform" in
        windows)
            config="$WINDOWS_CONFIG"
            resource_zip="$WINDOWS_ZIP"
            bundle_target="nsis"
            bundle_output="$SCRIPT_DIR/src-tauri/target/release/bundle/nsis"
            bundle_cleanup_path="$bundle_output"
            release_binary="$SCRIPT_DIR/src-tauri/target/release/bppinstaller.exe"
            ;;
        macos)
            config="$MACOS_CONFIG"
            resource_zip="$MACOS_ZIP"
            bundle_target="app,dmg"
            bundle_output="$SCRIPT_DIR/src-tauri/target/aarch64-apple-darwin/release/bundle/dmg"
            bundle_cleanup_path="$SCRIPT_DIR/src-tauri/target/aarch64-apple-darwin/release/bundle"
            release_binary="$SCRIPT_DIR/src-tauri/target/aarch64-apple-darwin/release/bppinstaller"
            tauri_target="aarch64-apple-darwin"
            ;;
        *)
            echo "Error: Unsupported platform: $platform" >&2
            exit 1
            ;;
    esac

    assert_file "$config" "$platform Tauri config"
    assert_file "$resource_zip" "$platform resource zip"

    if [ -d "$bundle_cleanup_path" ]; then
        invoke_step "Removing stale $platform bundle artifacts" rm -rf "$bundle_cleanup_path"
    fi

    build_command=(
        npm run tauri build -- --no-bundle --config "$config"
    )
    bundle_command=(
        npm run tauri bundle -- --bundles "$bundle_target" --config "$config"
    )

    if [ -n "$tauri_target" ]; then
        build_command+=(--target "$tauri_target")
        bundle_command+=(--target "$tauri_target")
    fi

    invoke_step "Building $platform app binary" "${build_command[@]}"

    if [ "$platform" = "macos" ]; then
        prepare_signed_macos_resource_zip "$resource_zip"
    fi

    invoke_step "Bundling $platform installer" "${bundle_command[@]}"

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

    parse_args "$@"

    cd "$SCRIPT_DIR"

    assert_command node "Install Node.js first."
    if [ "$PROD" = false ] && [ "$UPLOAD" = false ]; then
        assert_command npm "Install Node.js/npm first."
        assert_command cargo "Install Rust toolchain first."
        install_dependencies
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
        assert_command npm "Install Node.js/npm first."
        assert_command cargo "Install Rust toolchain first."
        install_dependencies

        if [ "$platform" = "macos" ]; then
            assert_command rustup "Install rustup first so the macOS Rust target can be managed."
        fi

        load_updater_signing_env
        if [ "$platform" = "macos" ]; then
            load_macos_developer_id_env
        fi
        run_release_prechecks
        ensure_required_rust_targets "$platform"
        version="$(package_version)"
        build_prod "$platform"
    fi

    if [ "$UPLOAD" = true ]; then
        assert_command npx "Install Node.js/npm first so npx is available."
        upload_release_assets "$platform" "$version" "$platform_key" "$base_url"
        generate_latest_manifest "$version" "$base_url"
    fi
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
