import { test, expect } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { relative } from 'node:path';
import { runShell, toBashPath } from './test-support/shell.mjs';

const projectDir = process.cwd();

// Runs the callback against an ISOLATED, throwaway signing-secrets directory so
// tests never read, write, or restore the developer's real signing-secrets/.
// (The old backup/restore approach clobbered real keys whenever a run was
// interrupted before its finally block ran.) The temp dir lives under the
// project root with a `signing-secrets` leaf, so bundle.sh's INSTALLER_ROOT-relative
// resolution of a relative apple-api-key-path still lands inside the fixtures.
// `files` may be an object or a `(ctx) => object` builder that needs the paths;
// `fn` receives the same ctx { dir, dirBash, relBash }.
function withSigningSecretFiles(files, fn) {
  const base = mkdtempSync(
    `${projectDir}/.bpp-signing-test-'$(printf injected)-`
  );
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

test('macOS production build stops at the first resource-signing failure', () => {
  const output = runShell(`
    set -euo pipefail
    source ./scripts/bundle.sh
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

test('macOS production build requires the arm64 Rust target', () => {
  const output = runShell(`
    source ./scripts/bundle.sh
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

test('dependency install fails clearly instead of updating an absent lockfile', () => {
  const root = mkdtempSync(`${projectDir}/.bpp-deps-test-'$(printf injected)-`);
  const rootBash = toBashPath(root);

  try {
    const output = runShell(
      `
      source ./scripts/bundle.sh
      INSTALLER_ROOT="$BPP_TEST_INSTALLER_ROOT"
      set +e
      install_dependencies 2>&1
      printf 'exit:%s\\n' "$?"
    `,
      { BPP_TEST_INSTALLER_ROOT: rootBash }
    );
    expect(output).toContain('package-lock.json is required');
    expect(output).toContain('exit:1');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('macOS release rejects a replay recorder plugin signed by a local Developer ID', () => {
  const output = runShell(`
    set -euo pipefail
    source ./scripts/bundle.sh
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
test('macOS Developer ID env loads from signing-secrets files', () => {
  withSigningSecretFiles(
    ({ relBash }) => ({
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'KEYFROMFILE\n',
      'apple-api-key-path': `${relBash}/AuthKey_KEYFROMFILE.p8\n`,
      'apple-signing-identity':
        'Developer ID Application: Example Builder (TEAMID1234)\n',
      'AuthKey_KEYFROMFILE.p8': 'private key'
    }),
    ({ dirBash }) => {
      const output = runShell(
        `
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
        source ./scripts/bundle.sh
        load_macos_developer_id_env >/tmp/bpp-apple-env-test.out
        cat /tmp/bpp-apple-env-test.out
        printf 'issuer=%s\\n' "$APPLE_API_ISSUER"
        printf 'key=%s\\n' "$APPLE_API_KEY"
        printf 'key_path=%s\\n' "$APPLE_API_KEY_PATH"
        printf 'identity=%s\\n' "$APPLE_SIGNING_IDENTITY"
      `,
        { BPP_SIGNING_SECRETS_DIR: dirBash }
      );

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
      // A relative key path in signing-secrets is exported as absolute: a
      // POSIX root, or a drive root under Git Bash on Windows.
      expect(output).toMatch(
        /key_path=(?:\/|[A-Za-z]:[/\\]).*[/\\]signing-secrets[/\\]AuthKey_KEYFROMFILE\.p8/
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
      'AuthKey_AUTOKEY.p8': 'private key'
    },
    ({ dirBash }) => {
      const output = runShell(
        `
        set -euo pipefail
        unset APPLE_API_ISSUER APPLE_API_KEY APPLE_API_KEY_PATH APPLE_SIGNING_IDENTITY
        source ./scripts/bundle.sh
        security() {
          printf '%s\\n' '  1) ABC "Apple Development: dev@example.com (TEAMID1234)"'
          printf '%s\\n' '  2) DEF "Developer ID Application: Example Builder (TEAMID1234)"'
        }
        load_macos_developer_id_env >/tmp/bpp-apple-env-test.out
        cat /tmp/bpp-apple-env-test.out
        printf 'key_path=%s\\n' "$APPLE_API_KEY_PATH"
        printf 'identity=%s\\n' "$APPLE_SIGNING_IDENTITY"
      `,
        { BPP_SIGNING_SECRETS_DIR: dirBash }
      );

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

test('release prechecks require the product build lock before verification', () => {
  const output = runShell(`
    set -euo pipefail
    source ./scripts/bundle.sh
    invoke_step() {
      local label="$1"
      shift
      printf '%s|%s\\n' "$label" "$*"
    }
    run_release_prechecks windows
  `);
  expect(output).not.toContain('release.mjs check');
  expect(output).toContain('release.mjs assert-build-owner');
  expect(output).toContain('npm run verify -- --release-platform windows');
  expect(output).not.toContain('prepare:resources');
});

test('updater signing env requires the key and defaults the password to empty', () => {
  withSigningSecretFiles(
    { 'tauri-updater.key': 'updater-key\n\n' },
    ({ dirBash }) => {
      const output = runShell(
        `
      set -euo pipefail
      unset TAURI_SIGNING_PRIVATE_KEY TAURI_SIGNING_PRIVATE_KEY_PASSWORD
      source ./scripts/bundle.sh
      load_updater_signing_env
      printf 'key=[%s]\\n' "$TAURI_SIGNING_PRIVATE_KEY"
      printf 'password=[%s]\\n' "$TAURI_SIGNING_PRIVATE_KEY_PASSWORD"
      bash -c 'printf "exported=[%s]\\n" "$TAURI_SIGNING_PRIVATE_KEY_PASSWORD"'
    `,
        { BPP_SIGNING_SECRETS_DIR: dirBash }
      );
      expect(output).toContain(
        'Loading TAURI_SIGNING_PRIVATE_KEY from signing-secrets'
      );
      expect(output).toContain('key=[updater-key]');
      expect(output).toContain('password=[]');
      expect(output).toContain('exported=[]');
    }
  );

  withSigningSecretFiles({}, ({ dirBash }) => {
    const output = runShell(
      `
      unset TAURI_SIGNING_PRIVATE_KEY TAURI_SIGNING_PRIVATE_KEY_PASSWORD
      source ./scripts/bundle.sh
      set +e
      (load_updater_signing_env) 2>&1
      printf 'exit:%s\\n' "$?"
    `,
      { BPP_SIGNING_SECRETS_DIR: dirBash }
    );
    expect(output).toContain('Missing TAURI_SIGNING_PRIVATE_KEY');
    expect(output).toContain('exit:1');
  });
});

test('bundle.sh refuses to run outside the product build lock', () => {
  const output = runShell(`
    unset BPP_RELEASE_LOCK_TOKEN
    set +e
    bash ./scripts/bundle.sh 2>&1
    printf 'exit:%s\\n' "$?"
  `);
  expect(output).toContain('just release::build <platform>');
  expect(output).toContain('exit:1');
});
