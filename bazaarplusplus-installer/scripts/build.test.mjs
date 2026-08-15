import { test, expect } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { relative } from 'node:path';
import { runShell, toBashPath } from './test-support/shell.mjs';

const projectDir = process.cwd();

// Runs the callback against an ISOLATED, throwaway signing-secrets directory so
// tests never read, write, or restore the developer's real signing-secrets/.
// (The old backup/restore approach clobbered real keys whenever a run was
// interrupted before its finally block ran.) The temp dir lives under the
// project root with a `signing-secrets` leaf, so build.sh's SCRIPT_DIR-relative
// resolution of a relative apple-api-key-path still lands inside the fixtures.
// `files` may be an object or a `(ctx) => object` builder that needs the paths;
// `fn` receives the same ctx { dir, dirBash, relBash }.
function withSigningSecretFiles(files, fn) {
  const base = mkdtempSync(`${projectDir}/.bpp-signing-test-`);
  const dir = `${base}/signing-secrets`;
  mkdirSync(dir, { recursive: true });

  const dirBash = process.platform === 'win32' ? toBashPath(dir) : dir;
  const rel = relative(projectDir, dir);
  const relBash = process.platform === 'win32' ? toBashPath(rel) : rel;
  const ctx = { dir, dirBash, relBash };

  const resolved = typeof files === 'function' ? files(ctx) : files;
  for (const [name, content] of Object.entries(resolved)) {
    writeFileSync(`${dir}/${name}`, content);
  }

  try {
    fn(ctx);
  } finally {
    rmSync(base, { recursive: true, force: true });
  }
}

test('macOS production build targets arm64 artifacts', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    assert_file() { :; }
    prepare_signed_macos_resource_zip() {
      printf 'Preparing signed macos resource zip|%s\\n' "$*"
    }
    prepare_signed_macos_resource_binary() {
      printf 'Preparing signed macos resource binary|%s\\n' "$*"
    }
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
    }
    build_prod macos
  `);

  expect(output).toMatch(
    /Building macos app binary\|npm run tauri build -- --no-bundle --config .*src-tauri\/tauri\.macos\.conf\.json --config .*src-tauri\/tauri\.release\.conf\.json --target aarch64-apple-darwin/
  );
  expect(output).toMatch(
    /Preparing signed macos resource zip\|.*src-tauri\/resources\/BepInExSource\/macos\/BepInEx\.zip/
  );
  expect(output).toMatch(
    /Preparing signed macos resource binary\|.*src-tauri\/resources\/Trampoline\/macos\/bpp_launcher/
  );
  expect(output).toMatch(
    /Bundling macos installer\|npm run tauri bundle -- --bundles app,dmg --config .*src-tauri\/tauri\.macos\.conf\.json --config .*src-tauri\/tauri\.release\.conf\.json --target aarch64-apple-darwin/
  );
  expect(output).not.toMatch(/Notarizing macos|notarytool|stapler/);
  expect(output).toMatch(
    /Binary:\s+.*src-tauri\/target\/aarch64-apple-darwin\/release\/bppinstaller/
  );
  expect(output).toMatch(
    /Bundle:\s+.*src-tauri\/target\/aarch64-apple-darwin\/release\/bundle\/dmg/
  );
});

test('macOS production build stops at the first resource-signing failure', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    assert_file() { :; }
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
    }
    prepare_signed_macos_resource_zip() {
      printf 'resource-signing:start\\n'
      false
      printf 'resource-signing:continued\\n'
    }
    prepare_signed_macos_resource_binary() {
      printf 'trampoline-signing:started\\n'
    }
    failure_log="$(mktemp)"
    trap 'rm -f "$failure_log"' EXIT
    set +e
    (set -e; build_prod macos) >"$failure_log" 2>&1
    status="$?"
    set -e
    cat "$failure_log"
    printf 'exit:%s\\n' "$status"
  `);

  expect(output).toContain('resource-signing:start');
  expect(output).toContain('exit:1');
  expect(output).not.toContain('resource-signing:continued');
  expect(output).not.toContain('trampoline-signing:started');
  expect(output).not.toContain('Bundling macos installer');
});

test('macOS production build removes the entire bundle directory before rebundling', () => {
  const bundleDir = `${projectDir}/src-tauri/target/aarch64-apple-darwin/release/bundle`;
  const staleDir = `${bundleDir}/macos`;
  const staleFile = `${staleDir}/rw.test.BazaarPlusPlus_2.0.0_aarch64.dmg`;

  mkdirSync(staleDir, { recursive: true });
  writeFileSync(staleFile, 'stale dmg');

  try {
    const output = runShell(`
      set -euo pipefail
      source ./build.sh
      assert_file() { :; }
      prepare_signed_macos_resource_zip() { :; }
      prepare_signed_macos_resource_binary() { :; }
      invoke_step() {
        local label="$1"
        shift
        printf '%s|%s\\n' "$label" "$*"
      }
      build_prod macos
    `);

    expect(output).toMatch(
      /Removing stale macos bundle artifacts\|rm -rf .*src-tauri\/target\/aarch64-apple-darwin\/release\/bundle\n/
    );
  } finally {
    rmSync(bundleDir, { force: true, recursive: true });
  }
});

test('Windows production build keeps the default target layout', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    assert_file() { :; }
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
    }
    build_prod windows
  `);

  expect(output).toMatch(
    /Building windows app binary\|npm run tauri build -- --no-bundle --config .*src-tauri\/tauri\.windows\.conf\.json --config .*src-tauri\/tauri\.release\.conf\.json/
  );
  expect(output).not.toMatch(/aarch64-apple-darwin|universal-apple-darwin/);
  expect(output).toMatch(
    /Binary:\s+.*src-tauri\/target\/release\/bppinstaller\.exe/
  );
  expect(output).toMatch(
    /Bundle:\s+.*src-tauri\/target\/release\/bundle\/nsis/
  );
});

test('macOS production build requires the arm64 Rust target', () => {
  const output = runShell(`
    source ./build.sh
    set +e
    rustup() {
      printf '%s\\n' x86_64-apple-darwin
    }
    ensure_required_rust_targets macos >/tmp/bpp-build-test.out 2>/tmp/bpp-build-test.err
    status="$?"
    cat /tmp/bpp-build-test.out
    cat /tmp/bpp-build-test.err
    printf 'exit:%s\\n' "$status"
  `);

  expect(output).toMatch(/Missing Rust target: aarch64-apple-darwin/);
  expect(output).toMatch(/rustup target add aarch64-apple-darwin/);
  expect(output).toMatch(/exit:1/);
});

test('Windows with no extra Rust target does not invoke rustup', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    rustup() { printf 'unexpected rustup call\\n'; return 127; }
    ensure_required_rust_targets windows
    printf 'ok\\n'
  `);

  expect(output).toBe('ok\n');
});

test('macOS accepts an already installed required Rust target', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    rustup() { printf 'aarch64-apple-darwin\\n'; }
    ensure_required_rust_targets macos
    printf 'ok\\n'
  `);

  expect(output).toBe('ok\n');
});

test('dependency install reuses a valid local node_modules tree', () => {
  const root = mkdtempSync(`${projectDir}/.bpp-deps-test-`);
  const rootBash = toBashPath(root);
  mkdirSync(`${root}/node_modules/@tauri-apps/cli`, { recursive: true });
  mkdirSync(`${root}/node_modules/.bin`, { recursive: true });
  writeFileSync(`${root}/node_modules/.bin/tauri`, 'fixture');

  try {
    const output = runShell(`
      set -euo pipefail
      source ./build.sh
      SCRIPT_DIR='${rootBash}'
      npm() { [ "$1" = ls ]; }
      install_dependencies true
    `);
    expect(output).toContain('Reusing existing npm dependencies');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('release dependency install uses npm ci instead of reusing node_modules', () => {
  const root = mkdtempSync(`${projectDir}/.bpp-deps-test-`);
  const rootBash = toBashPath(root);
  mkdirSync(`${root}/node_modules/@tauri-apps/cli`, { recursive: true });
  mkdirSync(`${root}/node_modules/.bin`, { recursive: true });
  writeFileSync(`${root}/node_modules/.bin/tauri`, 'fixture');
  writeFileSync(`${root}/package-lock.json`, '{}');

  try {
    const output = runShell(`
      set -euo pipefail
      source ./build.sh
      SCRIPT_DIR='${rootBash}'
      npm() { [ "$1" = ls ]; }
      invoke_step() { local label="$1"; shift; printf '%s|%s\\n' "$label" "$*"; }
      install_dependencies false
    `);
    expect(output).toContain('Installing npm dependencies|npm ci');
    expect(output).not.toContain('Reusing existing npm dependencies');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('dependency install uses npm ci whenever package-lock.json exists', () => {
  const root = mkdtempSync(`${projectDir}/.bpp-deps-test-`);
  const rootBash = toBashPath(root);
  writeFileSync(`${root}/package-lock.json`, '{}');

  try {
    const output = runShell(`
      set -euo pipefail
      source ./build.sh
      SCRIPT_DIR='${rootBash}'
      invoke_step() { local label="$1"; shift; printf '%s|%s\\n' "$label" "$*"; }
      install_dependencies
    `);
    expect(output).toContain('Installing npm dependencies|npm ci');
    expect(output).not.toContain('npm install');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('dependency install fails clearly instead of updating an absent lockfile', () => {
  const root = mkdtempSync(`${projectDir}/.bpp-deps-test-`);
  const rootBash = toBashPath(root);

  try {
    const output = runShell(`
      source ./build.sh
      SCRIPT_DIR='${rootBash}'
      set +e
      install_dependencies 2>&1
      printf 'exit:%s\\n' "$?"
    `);
    expect(output).toContain('package-lock.json is required');
    expect(output).toContain('exit:1');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('macOS resource signing applies Developer ID timestamp only to loose Mach-O files', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    payload="$(mktemp -d)"
    trap 'rm -rf "$payload"' EXIT
    mkdir -p "$payload/BepInEx/plugins"
    touch "$payload/libdoorstop.dylib"
    touch "$payload/BepInEx/plugins/libe_sqlite3.dylib"
    mkdir -p "$payload/TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS"
    touch "$payload/TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox"
    touch "$payload/readme.txt"
    APPLE_SIGNING_IDENTITY='Developer ID Application: YANG Xinyu (9Z44S3N293)'
    export APPLE_SIGNING_IDENTITY
    file() {
      case "$1" in
        *.dylib|*/GfxPluginBppReplayVideoToolbox) printf '%s: Mach-O 64-bit dynamically linked shared library\\n' "$1" ;;
        *) printf '%s: ASCII text\\n' "$1" ;;
      esac
    }
    codesign() {
      if [ "$1" = "-dvvv" ]; then
        printf '%s\\n' 'Signature size=1' 'TeamIdentifier=9Z44S3N293' >&2
        return 0
      fi
      printf 'codesign|%s\\n' "$*"
    }
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
      "$@"
    }
    sign_macos_resource_binaries "$payload"
  `);

  expect(output).toContain(
    'codesign|--force --options runtime --timestamp --sign Developer ID Application: YANG Xinyu (9Z44S3N293)'
  );
  expect(output).toContain('libdoorstop.dylib');
  expect(output).toContain('BepInEx/plugins/libe_sqlite3.dylib');
  expect(output).not.toContain('GfxPluginBppReplayVideoToolbox');
  expect(output).not.toContain('readme.txt');
});

test('macOS replay recorder plugin is signed inside-out with the official team', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    payload="$(mktemp -d)"
    trap 'rm -rf "$payload"' EXIT
    bundle="$payload/TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle"
    mkdir -p "$bundle/Contents/MacOS"
    touch "$bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox"
    touch "$bundle/Contents/Info.plist"
    APPLE_SIGNING_IDENTITY='Developer ID Application: YANG Xinyu (9Z44S3N293)'
    export APPLE_SIGNING_IDENTITY
    signed=false
    file() {
      case "$1" in
        */GfxPluginBppReplayVideoToolbox) printf '%s: Mach-O 64-bit dynamically linked shared library arm64\\n' "$1" ;;
        *) printf '%s: ASCII text\\n' "$1" ;;
      esac
    }
    codesign() {
      if [ "$1" = "-dvvv" ]; then
        if [ "$signed" = true ]; then
          printf '%s\\n' 'Signature size=1' 'TeamIdentifier=9Z44S3N293' >&2
        else
          printf '%s\\n' 'Signature=adhoc' 'TeamIdentifier=not set' >&2
        fi
        return 0
      fi
      printf 'codesign|%s\\n' "$*"
      case "$*" in
        *'--sign '*) signed=true ;;
      esac
    }
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
      "$@"
    }
    sign_macos_resource_plugin_bundles "$payload"
  `);

  const executableIndex = output.indexOf(
    'Signing macOS resource binary TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox'
  );
  const bundleIndex = output.indexOf(
    'Signing macOS resource plugin bundle TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle'
  );
  const verifyIndex = output.indexOf(
    'Verifying macOS resource plugin bundle TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle'
  );
  expect(executableIndex).toBeGreaterThanOrEqual(0);
  expect(bundleIndex).toBeGreaterThan(executableIndex);
  expect(verifyIndex).toBeGreaterThan(bundleIndex);
  expect(output).toContain('codesign|--verify --deep --strict --verbose=2');
});

test('macOS resource zip treats TheBazaar.app as an overlay and signs its plugin inside-out', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    fixture="$(mktemp -d)"
    trap 'rm -rf "$fixture"' EXIT
    trace_file="$fixture/codesign.trace"
    resource_zip="$fixture/BepInEx.zip"
    touch "$resource_zip"
    APPLE_SIGNING_IDENTITY='Developer ID Application: YANG Xinyu (9Z44S3N293)'
    export APPLE_SIGNING_IDENTITY
    signed=false
    assert_command() { :; }
    assert_file() { :; }
    ditto() {
      local payload="\${4}"
      local bundle="$payload/$REPLAY_RECORDER_RELATIVE_BUNDLE"
      mkdir -p "$bundle/Contents/MacOS"
      touch "$bundle/Contents/Info.plist"
      touch "$bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox"
    }
    file() {
      case "$1" in
        */GfxPluginBppReplayVideoToolbox) printf '%s: Mach-O 64-bit dynamically linked shared library arm64\\n' "$1" ;;
        *) printf '%s: ASCII text\\n' "$1" ;;
      esac
    }
    codesign() {
      local target="\${!#}"
      printf 'codesign|%s\\n' "$*" >>"$trace_file"
      if [ "$1" = "-dvvv" ]; then
        if [ "$signed" = true ]; then
          printf '%s\\n' 'Signature size=1' 'TeamIdentifier=9Z44S3N293' >&2
        else
          printf '%s\\n' 'Signature=adhoc' 'TeamIdentifier=not set' >&2
        fi
        return 0
      fi
      if [[ "$target" == */TheBazaar.app ]]; then
        printf '%s: bundle format unrecognized, invalid, or unsuitable\\n' "$target" >&2
        return 1
      fi
      case "$*" in
        *'--sign '*GfxPluginBppReplayVideoToolbox*) signed=true ;;
      esac
    }
    create_zip_from_directory() {
      touch "$2" "$3"
    }
    set +e
    (prepare_signed_macos_resource_zip "$resource_zip") >"$fixture/build.log" 2>&1
    status="$?"
    set -e
    cat "$fixture/build.log"
    cat "$trace_file"
    printf 'exit:%s\\n' "$status"
  `);

  const inputCheckIndex = output.indexOf(
    'codesign|-dvvv',
    output.indexOf('GfxPluginBppReplayVideoToolbox.bundle')
  );
  const executableSignIndex = output.indexOf(
    'codesign|--force --options runtime --timestamp --sign Developer ID Application: YANG Xinyu (9Z44S3N293)'
  );
  const bundleSignIndex = output.lastIndexOf(
    'codesign|--force --options runtime --timestamp --sign Developer ID Application: YANG Xinyu (9Z44S3N293)'
  );
  expect(output).toContain('exit:0');
  expect(output).not.toContain(
    'Signing macOS resource app bundle TheBazaar.app'
  );
  expect(inputCheckIndex).toBeGreaterThanOrEqual(0);
  expect(executableSignIndex).toBeGreaterThan(inputCheckIndex);
  expect(bundleSignIndex).toBeGreaterThan(executableSignIndex);
});

test('macOS release rejects a replay recorder plugin signed by a local Developer ID', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    bundle="$(mktemp -d)/GfxPluginBppReplayVideoToolbox.bundle"
    mkdir -p "$bundle/Contents"
    codesign() {
      printf '%s\\n' 'Signature size=8995' 'TeamIdentifier=WRONGTEAM1' >&2
    }
    set +e
    (assert_ad_hoc_replay_recorder_input "$bundle") 2>&1
    printf 'exit:%s\\n' "$?"
  `);

  expect(output).toContain(
    'Replay recorder plugin input must be ad-hoc signed with no TeamIdentifier'
  );
  expect(output).toContain('exit:1');
});
test('macOS loose resource signing applies Developer ID timestamp to trampoline stub', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    payload="$(mktemp -d)"
    trap 'rm -rf "$payload"' EXIT
    stub="$payload/bpp_launcher"
    touch "$stub"
    APPLE_SIGNING_IDENTITY='Developer ID Application: YANG Xinyu (9Z44S3N293)'
    export APPLE_SIGNING_IDENTITY
    file() {
      printf '%s: Mach-O 64-bit executable arm64\\n' "$1"
    }
    codesign() {
      if [ "$1" = "-dvvv" ]; then
        printf '%s\\n' 'Signature size=1' 'TeamIdentifier=9Z44S3N293' >&2
        return 0
      fi
      printf 'codesign|%s\\n' "$*"
    }
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
      "$@"
    }
    prepare_signed_macos_resource_binary "$stub"
  `);

  expect(output).toContain('Signing macOS resource binary');
  expect(output).toContain(
    'codesign|--force --options runtime --timestamp --sign Developer ID Application: YANG Xinyu (9Z44S3N293)'
  );
  expect(output).toContain('bpp_launcher');
});

test('macOS Developer ID env loads from signing-secrets files', () => {
  withSigningSecretFiles(
    ({ dirBash }) => ({
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'KEYFROMFILE\n',
      'apple-api-key-path': `${dirBash}/AuthKey_KEYFROMFILE.p8\n`,
      'apple-signing-identity':
        'Developer ID Application: Example Builder (TEAMID1234)\n',
      'AuthKey_KEYFROMFILE.p8': 'private key'
    }),
    ({ dirBash }) => {
      const output = runShell(`
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
        export BPP_SIGNING_SECRETS_DIR="${dirBash}"
        source ./build.sh
        load_macos_developer_id_env >/tmp/bpp-apple-env-test.out
        cat /tmp/bpp-apple-env-test.out
        printf 'issuer=%s\\n' "$APPLE_API_ISSUER"
        printf 'key=%s\\n' "$APPLE_API_KEY"
        printf 'key_path=%s\\n' "$APPLE_API_KEY_PATH"
        printf 'identity=%s\\n' "$APPLE_SIGNING_IDENTITY"
      `);

      expect(output).toContain(
        'Loading APPLE_SIGNING_IDENTITY from signing-secrets'
      );
      expect(output).toContain('Loading APPLE_API_ISSUER from signing-secrets');
      expect(output).toContain('Loading APPLE_API_KEY from signing-secrets');
      expect(output).toContain(
        'Loading APPLE_API_KEY_PATH from signing-secrets'
      );
      expect(output).toContain('issuer=issuer-from-file');
      expect(output).toContain('key=KEYFROMFILE');
      expect(output).toMatch(
        /key_path=.*[/\\]signing-secrets[/\\]AuthKey_KEYFROMFILE\.p8/
      );
      expect(output).toContain(
        'identity=Developer ID Application: Example Builder (TEAMID1234)'
      );
    }
  );
});

test('macOS Developer ID env exports relative API key paths as absolute paths', () => {
  withSigningSecretFiles(
    ({ relBash }) => ({
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'RELKEY\n',
      'apple-api-key-path': `${relBash}/AuthKey_RELKEY.p8\n`,
      'apple-signing-identity':
        'Developer ID Application: Example Builder (TEAMID1234)\n',
      'AuthKey_RELKEY.p8': 'private key'
    }),
    ({ dirBash }) => {
      const output = runShell(`
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
        export BPP_SIGNING_SECRETS_DIR="${dirBash}"
        source ./build.sh
        load_macos_developer_id_env >/tmp/bpp-apple-env-test.out
        cat /tmp/bpp-apple-env-test.out
        printf 'key_path=%s\\n' "$APPLE_API_KEY_PATH"
      `);

      expect(output).toMatch(
        /key_path=.*[/\\]signing-secrets[/\\]AuthKey_RELKEY\.p8/
      );
    }
  );
});

test('macOS Developer ID env detects identity and infers API key path', () => {
  withSigningSecretFiles(
    {
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'AUTOKEY\n',
      'AuthKey_AUTOKEY.p8': 'private key'
    },
    ({ dirBash }) => {
      const output = runShell(`
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
        export BPP_SIGNING_SECRETS_DIR="${dirBash}"
        source ./build.sh
        security() {
          printf '%s\\n' '  1) ABC "Apple Development: dev@example.com (TEAMID1234)"'
          printf '%s\\n' '  2) DEF "Developer ID Application: Example Builder (TEAMID1234)"'
        }
        load_macos_developer_id_env >/tmp/bpp-apple-env-test.out
        cat /tmp/bpp-apple-env-test.out
        printf 'key_path=%s\\n' "$APPLE_API_KEY_PATH"
        printf 'identity=%s\\n' "$APPLE_SIGNING_IDENTITY"
      `);

      expect(output).toContain(
        'Auto-detected APPLE_SIGNING_IDENTITY from keychain'
      );
      expect(output).toContain(
        'Inferring APPLE_API_KEY_PATH from signing-secrets'
      );
      expect(output).toMatch(
        /key_path=.*[/\\]signing-secrets[/\\]AuthKey_AUTOKEY\.p8/
      );
      expect(output).toContain(
        'identity=Developer ID Application: Example Builder (TEAMID1234)'
      );
    }
  );
});

test('Windows upload uses installer and updater R2 paths under the version directory', () => {
  const bundleDir = `${projectDir}/src-tauri/target/release/bundle/nsis`;
  const installerFile = `${bundleDir}/BazaarPlusPlus_2.1.0_x64-setup.exe`;
  const signatureFile = `${installerFile}.sig`;

  mkdirSync(bundleDir, { recursive: true });
  writeFileSync(installerFile, 'installer');
  writeFileSync(signatureFile, 'signature');

  try {
    const output = runShell(`
      set -euo pipefail
      source ./build.sh
      assert_file() { :; }
      artifact_manifest_paths() {
        printf '%s\\n' '${installerFile}' '${installerFile}' '${signatureFile}'
      }
      invoke_step() {
        local label="$1"
        shift
        printf '%s|%s\\n' "$label" "$*"
      }
      upload_release_assets windows 2.1.0 windows-x86_64 https://bppinstaller.bazaarplusplus.com
    `);

    expect(output).toMatch(
      /Uploading BazaarPlusPlus_2\.1\.0_x64-setup\.exe to 2\.1\.0\/windows-x86_64\/installer\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\|wrangler_cli r2 object put bppinstaller\/2\.1\.0\/windows-x86_64\/installer\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe --file .*BazaarPlusPlus_2\.1\.0_x64-setup\.exe/
    );
    expect(output).toMatch(
      /Uploading BazaarPlusPlus_2\.1\.0_x64-setup\.exe to 2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\|wrangler_cli r2 object put bppinstaller\/2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe --file .*BazaarPlusPlus_2\.1\.0_x64-setup\.exe/
    );
    expect(output).toMatch(
      /Uploading BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig to 2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig\|wrangler_cli r2 object put bppinstaller\/2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig --file .*BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig/
    );
  } finally {
    rmSync(bundleDir, { force: true, recursive: true });
  }
});
