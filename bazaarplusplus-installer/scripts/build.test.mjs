import { test, expect } from 'vitest';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

function runShell(script) {
  return execFileSync('bash', ['-lc', script], {
    cwd: projectDir,
    encoding: 'utf8'
  });
}

const projectDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const signingSecretsDir = `${projectDir}/signing-secrets`;

function withSigningSecretFiles(files, fn) {
  const backups = new Map();

  for (const name of Object.keys(files)) {
    const secretPath = path.join(signingSecretsDir, name);
    backups.set(
      name,
      existsSync(secretPath)
        ? { existed: true, content: readFileSync(secretPath, 'utf8') }
        : { existed: false }
    );
  }

  mkdirSync(signingSecretsDir, { recursive: true });
  for (const [name, content] of Object.entries(files)) {
    writeFileSync(path.join(signingSecretsDir, name), content);
  }

  try {
    fn();
  } finally {
    for (const [name, backup] of backups.entries()) {
      const secretPath = path.join(signingSecretsDir, name);
      if (backup.existed) {
        writeFileSync(secretPath, backup.content);
      } else {
        rmSync(secretPath, { force: true });
      }
    }
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
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
    }
    build_prod macos
  `);

  expect(output).toMatch(
    /Building macos app binary\|npm run tauri build -- --no-bundle --config .*src-tauri\/tauri\.macos\.conf\.json --target aarch64-apple-darwin/
  );
  expect(output).toMatch(
    /Preparing signed macos resource zip\|.*src-tauri\/resources\/BepInExSource\/macos\/BepInEx\.zip/
  );
  expect(output).toMatch(
    /Bundling macos installer\|npm run tauri bundle -- --bundles app,dmg --config .*src-tauri\/tauri\.macos\.conf\.json --target aarch64-apple-darwin/
  );
  expect(output).not.toMatch(/Notarizing macos|notarytool|stapler/);
  expect(output).toMatch(
    /Binary:\s+.*src-tauri\/target\/aarch64-apple-darwin\/release\/bppinstaller/
  );
  expect(output).toMatch(
    /Bundle:\s+.*src-tauri\/target\/aarch64-apple-darwin\/release\/bundle\/dmg/
  );
});

test('macOS production build removes the entire bundle directory before rebundling', () => {
  const bundleDir =
    path.join(projectDir, 'src-tauri', 'target', 'aarch64-apple-darwin', 'release', 'bundle');
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
    /Building windows app binary\|npm run tauri build -- --no-bundle --config .*src-tauri\/tauri\.windows\.conf\.json/
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

test('macOS resource signing applies Developer ID timestamp only to Mach-O files', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    payload="$(mktemp -d)"
    trap 'rm -rf "$payload"' EXIT
    mkdir -p "$payload/BepInEx/plugins"
    touch "$payload/libdoorstop.dylib"
    touch "$payload/BepInEx/plugins/libe_sqlite3.dylib"
    touch "$payload/readme.txt"
    APPLE_SIGNING_IDENTITY='Developer ID Application: Example Builder (TEAMID1234)'
    export APPLE_SIGNING_IDENTITY
    file() {
      case "$1" in
        *.dylib) printf '%s: Mach-O 64-bit dynamically linked shared library\\n' "$1" ;;
        *) printf '%s: ASCII text\\n' "$1" ;;
      esac
    }
    codesign() {
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
    'codesign|--force --options runtime --timestamp --sign Developer ID Application: Example Builder (TEAMID1234)'
  );
  expect(output).toContain('libdoorstop.dylib');
  expect(output).toContain('BepInEx/plugins/libe_sqlite3.dylib');
  expect(output).not.toContain('readme.txt');
});

test('macOS Developer ID env loads from signing-secrets files', () => {
  withSigningSecretFiles(
    {
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'KEYFROMFILE\n',
      'apple-api-key-path': `${signingSecretsDir}/AuthKey_KEYFROMFILE.p8\n`,
      'apple-signing-identity':
        'Developer ID Application: Example Builder (TEAMID1234)\n',
      'AuthKey_KEYFROMFILE.p8': 'private key'
    },
    () => {
      const output = runShell(`
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
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
      expect(output).toContain(
        `key_path=${signingSecretsDir}/AuthKey_KEYFROMFILE.p8`
      );
      expect(output).toContain(
        'identity=Developer ID Application: Example Builder (TEAMID1234)'
      );
    }
  );
});

test('macOS Developer ID env detects identity and infers API key path', () => {
  withSigningSecretFiles(
    {
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'AUTOKEY\n',
      'apple-api-key-path': '',
      'apple-signing-identity': '',
      'AuthKey_AUTOKEY.p8': 'private key'
    },
    () => {
      rmSync(`${signingSecretsDir}/apple-api-key-path`, { force: true });
      rmSync(`${signingSecretsDir}/apple-signing-identity`, { force: true });

      const output = runShell(`
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
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
      expect(output).toContain(
        `key_path=${signingSecretsDir}/AuthKey_AUTOKEY.p8`
      );
      expect(output).toContain(
        'identity=Developer ID Application: Example Builder (TEAMID1234)'
      );
    }
  );
});

test('Windows upload uses installer and updater R2 paths under the version directory', () => {
  const bundleDir =
    path.join(projectDir, 'src-tauri', 'target', 'release', 'bundle', 'nsis');
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
      invoke_step() {
        local label="$1"
        shift
        printf '%s|%s\\n' "$label" "$*"
      }
      upload_release_assets windows 2.1.0 windows-x86_64 https://bppinstaller.bazaarplusplus.com
    `);

    expect(output).toMatch(
      /Uploading BazaarPlusPlus_2\.1\.0_x64-setup\.exe to 2\.1\.0\/windows-x86_64\/installer\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\|npx wrangler r2 object put bppinstaller\/2\.1\.0\/windows-x86_64\/installer\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe --file .*BazaarPlusPlus_2\.1\.0_x64-setup\.exe/
    );
    expect(output).toMatch(
      /Uploading BazaarPlusPlus_2\.1\.0_x64-setup\.exe to 2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\|npx wrangler r2 object put bppinstaller\/2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe --file .*BazaarPlusPlus_2\.1\.0_x64-setup\.exe/
    );
    expect(output).toMatch(
      /Uploading BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig to 2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig\|npx wrangler r2 object put bppinstaller\/2\.1\.0\/windows-x86_64\/updater\/BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig --file .*BazaarPlusPlus_2\.1\.0_x64-setup\.exe\.sig/
    );
  } finally {
    rmSync(bundleDir, { force: true, recursive: true });
  }
});
