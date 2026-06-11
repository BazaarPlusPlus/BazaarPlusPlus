import { test, expect } from 'vitest';
import { execFileSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync
} from 'node:fs';

const projectDir = process.cwd();
const signingSecretsDir = `${projectDir}/signing-secrets`;

// Resolve a usable bash. On Windows, `bash` is frequently absent from the PATH
// that npm spawns with (PowerShell/cmd), so fall back to the Git for Windows
// install before giving up.
function resolveBashCommand() {
  if (process.platform !== 'win32') {
    return 'bash';
  }
  const candidates = [];
  if (process.env.BPP_BASH) {
    candidates.push(process.env.BPP_BASH);
  }
  try {
    const execPath = execFileSync('git', ['--exec-path'], {
      encoding: 'utf8'
    }).trim();
    const gitRoot = execPath.replace(/[/\\](mingw\d+|usr)[/\\].*$/i, '');
    if (gitRoot && gitRoot !== execPath) {
      candidates.push(`${gitRoot}/bin/bash.exe`);
    }
  } catch {
    // git not on PATH; fall back to the well-known install locations below.
  }
  candidates.push(
    'C:/Program Files/Git/bin/bash.exe',
    'C:/Program Files (x86)/Git/bin/bash.exe'
  );
  return (
    candidates.find((candidate) => candidate && existsSync(candidate)) ?? 'bash'
  );
}

const bashCommand = resolveBashCommand();

// Git Bash treats `E:\foo` as a relative path, so any path we hand to build.sh
// through a config file must be POSIX-style absolute (`/e/foo`) on Windows.
function toBashPath(p) {
  return p
    .replace(/\\/g, '/')
    .replace(/^([A-Za-z]):/, (_, drive) => `/${drive.toLowerCase()}`);
}

const signingSecretsDirBash =
  process.platform === 'win32'
    ? toBashPath(signingSecretsDir)
    : signingSecretsDir;

function runShell(script) {
  return execFileSync(bashCommand, ['-lc', script], {
    cwd: projectDir,
    encoding: 'utf8',
    timeout: 120000
  });
}

function withSigningSecretFiles(files, fn) {
  const backups = new Map();

  for (const name of Object.keys(files)) {
    const path = `${signingSecretsDir}/${name}`;
    backups.set(
      name,
      existsSync(path)
        ? { existed: true, content: readFileSync(path, 'utf8') }
        : { existed: false }
    );
  }

  mkdirSync(signingSecretsDir, { recursive: true });
  for (const [name, content] of Object.entries(files)) {
    writeFileSync(`${signingSecretsDir}/${name}`, content);
  }

  try {
    fn();
  } finally {
    for (const [name, backup] of backups.entries()) {
      const path = `${signingSecretsDir}/${name}`;
      if (backup.existed) {
        writeFileSync(path, backup.content);
      } else {
        rmSync(path, { force: true });
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
    /Building macos app binary\|npm run tauri build -- --no-bundle --config .*src-tauri\/tauri\.macos\.conf\.json --target aarch64-apple-darwin/
  );
  expect(output).toMatch(
    /Preparing signed macos resource zip\|.*src-tauri\/resources\/BepInExSource\/macos\/BepInEx\.zip/
  );
  expect(output).toMatch(
    /Preparing signed macos resource binary\|.*src-tauri\/resources\/Trampoline\/macos\/bpp_launcher/
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

test('macOS loose resource signing applies Developer ID timestamp to trampoline stub', () => {
  const output = runShell(`
    set -euo pipefail
    source ./build.sh
    payload="$(mktemp -d)"
    trap 'rm -rf "$payload"' EXIT
    stub="$payload/bpp_launcher"
    touch "$stub"
    APPLE_SIGNING_IDENTITY='Developer ID Application: Example Builder (TEAMID1234)'
    export APPLE_SIGNING_IDENTITY
    file() {
      printf '%s: Mach-O 64-bit executable arm64\\n' "$1"
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
    prepare_signed_macos_resource_binary "$stub"
  `);

  expect(output).toContain('Signing macOS resource binary');
  expect(output).toContain(
    'codesign|--force --options runtime --timestamp --sign Developer ID Application: Example Builder (TEAMID1234)'
  );
  expect(output).toContain('bpp_launcher');
});

test('macOS Developer ID env loads from signing-secrets files', () => {
  withSigningSecretFiles(
    {
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'KEYFROMFILE\n',
      'apple-api-key-path': `${signingSecretsDirBash}/AuthKey_KEYFROMFILE.p8\n`,
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
    {
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'RELKEY\n',
      'apple-api-key-path': 'signing-secrets/AuthKey_RELKEY.p8\n',
      'apple-signing-identity':
        'Developer ID Application: Example Builder (TEAMID1234)\n',
      'AuthKey_RELKEY.p8': 'private key'
    },
    () => {
      const output = runShell(`
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
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
