#!/usr/bin/env bash
set -euo pipefail

kind="${1:?native artifact kind is required}"
binary="${2:?native binary path is required}"
signed_path="${3:-$binary}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
catalog_path="$script_dir/../artifacts.json"

catalog_values() {
    local property="$1"
    node -e '
const fs = require("node:fs");
const [catalogPath, artifactId, property] = process.argv.slice(1);
const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const artifact = catalog.platforms.macos.artifacts.find(({ id }) => id === artifactId);
if (!artifact) throw new Error(`missing catalog artifact ${artifactId}`);
process.stdout.write((artifact[property] ?? []).join("\n"));
' "$catalog_path" "$kind" "$property"
}

catalog_platform_value() {
    local property="$1"
    node -e '
const fs = require("node:fs");
const [catalogPath, property] = process.argv.slice(1);
const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const value = catalog.platforms.macos[property];
if (typeof value !== "string" || value === "")
    throw new Error(`missing catalog macos.${property}`);
process.stdout.write(value);
' "$catalog_path" "$property"
}

# The catalog owns the deployment target so this assertion cannot silently drift away from
# the value the installer hashes into the input digest. The build scripts still carry the
# literal -mmacosx-version-min flag, so a catalog edit alone fails the build loudly here
# instead of passing while producing the old minos.
deployment_target="$(catalog_platform_value deploymentTarget)"

BPP_NATIVE_REQUIRED_EXPORTS="${BPP_NATIVE_REQUIRED_EXPORTS:-$(catalog_values requiredExports)}"
BPP_NATIVE_REQUIRED_WEAK_IMPORTS="${BPP_NATIVE_REQUIRED_WEAK_IMPORTS:-$(catalog_values requiredWeakImports)}"
BPP_NATIVE_ALLOWED_DEPENDENCY_PREFIXES="${BPP_NATIVE_ALLOWED_DEPENDENCY_PREFIXES:-$(catalog_values allowedDependencyPrefixes)}"

[[ -f "$binary" ]] || { echo "missing native binary: $binary" >&2; exit 1; }
[[ -n "${BPP_NATIVE_REQUIRED_EXPORTS:-}" ]] || {
    echo "BPP_NATIVE_REQUIRED_EXPORTS is required." >&2
    exit 1
}

sdk_path="$(xcrun --sdk macosx --show-sdk-path)"
clang_path="$(xcrun --sdk macosx --find clang)"

archs="$(xcrun lipo -archs "$binary")"
[[ "$archs" == "arm64" ]] || {
    echo "expected arm64 Mach-O, got: $archs" >&2
    exit 1
}

build_version="$(xcrun vtool -show-build "$binary")"
[[ "$build_version" == *"platform MACOS"* ]] || {
    echo "Mach-O does not target platform MACOS." >&2
    exit 1
}
[[ "$build_version" == *"minos $deployment_target"* ]] || {
    echo "Mach-O deployment target is not $deployment_target." >&2
    exit 1
}

install_name="$(xcrun otool -D "$binary" 2>/dev/null | tail -n +2 | head -n 1 || true)"
while IFS= read -r dependency; do
    [[ -n "$dependency" ]] || continue
    [[ -n "$install_name" && "$dependency" == "$install_name" ]] && continue
    allowed=false
    while IFS= read -r prefix; do
        [[ -n "$prefix" ]] || continue
        if [[ "$dependency" == "$prefix"* ]]; then
            allowed=true
            break
        fi
    done <<<"${BPP_NATIVE_ALLOWED_DEPENDENCY_PREFIXES:-}"
    [[ "$allowed" == "true" ]] || {
        echo "unreviewed native dependency: $dependency" >&2
        exit 1
    }
done < <(xcrun otool -L "$binary" | tail -n +2 | sed -E 's/^[[:space:]]*([^[:space:]]+).*/\1/')

actual_exports="$(xcrun nm -gUj "$binary" | sed 's/^_//' | LC_ALL=C sort)"
expected_exports="$(printf '%s\n' "$BPP_NATIVE_REQUIRED_EXPORTS" | sed '/^$/d' | LC_ALL=C sort)"
[[ "$actual_exports" == "$expected_exports" ]] || {
    echo "native export set does not match the ABI contract." >&2
    diff -u <(printf '%s\n' "$expected_exports") <(printf '%s\n' "$actual_exports") >&2 || true
    exit 1
}

# Driven entirely by the artifact's requiredWeakImports: an empty list is a natural no-op,
# so a new artifact that declares weak imports is checked without editing this script.
undefined_symbols="$(xcrun nm -m -u "$binary")"
while IFS= read -r symbol; do
    [[ -n "$symbol" ]] || continue
    line="$(printf '%s\n' "$undefined_symbols" | sed -n "/_$symbol[[:space:]]/p")"
    [[ -n "$line" && ("$line" == *"weak import"* || "$line" == *"weak external"*) ]] || {
        echo "required weak import is missing or strong: $symbol" >&2
        exit 1
    }
done <<<"${BPP_NATIVE_REQUIRED_WEAK_IMPORTS:-}"

codesign --verify --strict "$binary"
signature_details="$(codesign -d --verbose=4 "$signed_path" 2>&1)"
[[ "$signature_details" == *"Signature=adhoc"* ]] || {
    echo "producer output is not ad-hoc signed: $signed_path" >&2
    exit 1
}

smoke_dir="$(mktemp -d "${TMPDIR:-/tmp}/bpp-native-smoke.XXXXXX")"
trap 'rm -rf -- "$smoke_dir"' EXIT
"$clang_path" -isysroot "$sdk_path" -mmacosx-version-min="$deployment_target" \
    "$script_dir/BppNativeLoadSmoke.c" -o "$smoke_dir/bpp-native-load-smoke"
smoke_symbols=()
while IFS= read -r symbol; do
    [[ -n "$symbol" ]] && smoke_symbols+=("$symbol")
done <<<"$BPP_NATIVE_REQUIRED_EXPORTS"
"$smoke_dir/bpp-native-load-smoke" "$binary" "${smoke_symbols[@]}"

echo "verified $kind: arm64, minos $deployment_target, exports, dependencies, load, ad-hoc signature"
