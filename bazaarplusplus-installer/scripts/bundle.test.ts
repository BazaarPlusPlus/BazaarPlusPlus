import { expect, test } from 'vite-plus/test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  assertAdHocReplayRecorderInput,
  assertPinnedBunVersion,
  buildProd,
  cliMain,
  ensureRequiredRustTargets,
  installDependencies,
  loadMacosDeveloperIdEnv,
  loadUpdaterSigningEnv,
  runReleasePrechecks
} from './bundle.ts';

function withTempDir(
  files: Record<string, string> | ((dir: string) => Record<string, string>),
  fn: (dir: string) => void
): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bundle-test-'));
  try {
    const resolved = typeof files === 'function' ? files(dir) : files;
    for (const [name, content] of Object.entries(resolved)) {
      const target = path.join(dir, name);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, content);
    }
    fn(dir);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('macOS production build stops at the first resource-signing failure', () => {
  const events: string[] = [];
  expect(() =>
    buildProd('macos', {
      env: {},
      log: () => {},
      assertFileExists: () => {},
      run: (_cmd, args) => {
        events.push(`run:${args.join(' ')}`);
      },
      prepareSignedMacosZip: () => {
        events.push('resource-signing:start');
        throw new Error('codesign failed');
      },
      prepareSignedMacosBinary: () => {
        events.push('trampoline-signing:started');
      }
    })
  ).toThrow('codesign failed');

  expect(events).toContain('resource-signing:start');
  expect(events).not.toContain('trampoline-signing:started');
  expect(events.some((entry) => entry.includes('tauri bundle'))).toBe(false);
});

test('macOS production build requires the arm64 Rust target', () => {
  expect(() =>
    ensureRequiredRustTargets('macos', {
      env: {},
      capture: () => 'x86_64-apple-darwin\n'
    })
  ).toThrow(
    /Missing Rust target: aarch64-apple-darwin[\s\S]*rustup target add aarch64-apple-darwin/
  );

  expect(() =>
    ensureRequiredRustTargets('macos', {
      env: {},
      capture: () => 'x86_64-apple-darwin\naarch64-apple-darwin\n'
    })
  ).not.toThrow();
});

test('dependency install fails clearly instead of updating an absent lockfile', () => {
  withTempDir({}, (dir) => {
    const nested = path.join(dir, 'installer');
    fs.mkdirSync(nested, { recursive: true });
    expect(() =>
      installDependencies({
        installerRoot: nested,
        env: {},
        log: () => {},
        run: () => {
          throw new Error('should not invoke bun install');
        }
      })
    ).toThrow(/bun\.lock is required/);
  });
});

test('macOS release rejects a replay recorder plugin signed by a local Developer ID', () => {
  withTempDir({}, (dir) => {
    const bundle = path.join(dir, 'GfxPluginBppReplayVideoToolbox.bundle');
    fs.mkdirSync(path.join(bundle, 'Contents'), { recursive: true });
    expect(() =>
      assertAdHocReplayRecorderInput(bundle, {
        env: {},
        run: () => {},
        capture: () => 'Signature size=8995\nTeamIdentifier=WRONGTEAM1\n'
      })
    ).toThrow(
      'Replay recorder plugin input must be ad-hoc signed with no TeamIdentifier.'
    );
  });
});

test('macOS Developer ID env loads from BPP_SIGNING_SECRETS_DIR files', () => {
  withTempDir(
    (dir) => ({
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'KEYFROMFILE\n',
      'apple-api-key-path': `${path.join(dir, 'AuthKey_KEYFROMFILE.p8')}\n`,
      'apple-signing-identity':
        'Developer ID Application: Example Builder (TEAMID1234)\n',
      'AuthKey_KEYFROMFILE.p8': 'private key'
    }),
    (dir) => {
      const env: NodeJS.ProcessEnv = { BPP_SIGNING_SECRETS_DIR: dir };
      const logs: string[] = [];
      loadMacosDeveloperIdEnv({ env, log: (line) => logs.push(line) });

      expect(logs).toContain(
        '==> Loading APPLE_SIGNING_IDENTITY from BPP_SIGNING_SECRETS_DIR'
      );
      expect(logs).toContain(
        '==> Loading APPLE_API_ISSUER from BPP_SIGNING_SECRETS_DIR'
      );
      expect(logs).toContain(
        '==> Loading APPLE_API_KEY from BPP_SIGNING_SECRETS_DIR'
      );
      expect(logs).toContain(
        '==> Loading APPLE_API_KEY_PATH from BPP_SIGNING_SECRETS_DIR'
      );
      expect(env.APPLE_API_ISSUER).toBe('issuer-from-file');
      expect(env.APPLE_API_KEY).toBe('KEYFROMFILE');
      expect(env.APPLE_API_KEY_PATH).toBe(
        path.join(dir, 'AuthKey_KEYFROMFILE.p8')
      );
      expect(env.APPLE_SIGNING_IDENTITY).toBe(
        'Developer ID Application: Example Builder (TEAMID1234)'
      );
    }
  );
});

test('macOS Developer ID env detects identity and infers API key path', () => {
  withTempDir(
    {
      'apple-api-issuer': 'issuer-from-file\n',
      'apple-api-key': 'AUTOKEY\n',
      'AuthKey_AUTOKEY.p8': 'private key'
    },
    (dir) => {
      const env: NodeJS.ProcessEnv = { BPP_SIGNING_SECRETS_DIR: dir };
      const logs: string[] = [];
      loadMacosDeveloperIdEnv({
        env,
        log: (line) => logs.push(line),
        capture: () =>
          [
            '  1) ABC "Apple Development: dev@example.com (TEAMID1234)"',
            '  2) DEF "Developer ID Application: Example Builder (TEAMID1234)"'
          ].join('\n')
      });

      expect(logs).toContain(
        '==> Auto-detected APPLE_SIGNING_IDENTITY from keychain'
      );
      expect(logs).toContain(
        '==> Inferring APPLE_API_KEY_PATH from BPP_SIGNING_SECRETS_DIR'
      );
      expect(env.APPLE_API_KEY_PATH).toBe(path.join(dir, 'AuthKey_AUTOKEY.p8'));
      expect(env.APPLE_SIGNING_IDENTITY).toBe(
        'Developer ID Application: Example Builder (TEAMID1234)'
      );
    }
  );
});

test('release prechecks require the product build lock before verification', () => {
  const order: string[] = [];
  runReleasePrechecks('windows', {
    env: {},
    log: () => {},
    assertOwner: () => {
      order.push('assert-build-owner');
    },
    run: (command, args) => {
      order.push(`${command} ${args.join(' ')}`);
    }
  });
  expect(order).toEqual([
    'assert-build-owner',
    'bun run verify --release-platform windows'
  ]);
});

test('updater signing env requires the key and defaults the password to empty string', () => {
  withTempDir({ 'tauri-updater.key': 'updater-key\n\n' }, (dir) => {
    const env: NodeJS.ProcessEnv = { BPP_SIGNING_SECRETS_DIR: dir };
    const logs: string[] = [];
    loadUpdaterSigningEnv({ env, log: (line) => logs.push(line) });

    expect(logs).toContain(
      '==> Loading TAURI_SIGNING_PRIVATE_KEY from BPP_SIGNING_SECRETS_DIR'
    );
    expect(env.TAURI_SIGNING_PRIVATE_KEY).toBe('updater-key');
    expect(env.TAURI_SIGNING_PRIVATE_KEY_PASSWORD).toBe('');
  });

  withTempDir({}, (dir) => {
    expect(() =>
      loadUpdaterSigningEnv({
        env: { BPP_SIGNING_SECRETS_DIR: dir },
        log: () => {}
      })
    ).toThrow(/Missing TAURI_SIGNING_PRIVATE_KEY/);
  });
});

test('assertPinnedBunVersion enforces package.json engines.bun', () => {
  withTempDir({ 'package.json': '{"engines":{"bun":"1.4.4"}}' }, (dir) => {
    expect(() => assertPinnedBunVersion(dir, '1.4.4')).not.toThrow();
    expect(() => assertPinnedBunVersion(dir, '1.4.3')).toThrow(
      'Bun 1.4.3 is unsupported; package.json requires 1.4.4'
    );
  });
});

test('bundle.ts refuses to run outside the product build lock or with extra arguments', () => {
  const errors: string[] = [];
  expect(cliMain([], { env: {}, errorLog: (line) => errors.push(line) })).toBe(
    1
  );
  expect(errors.at(-1)).toContain('just release::build <platform>');

  expect(
    cliMain(['--extra'], {
      env: { BPP_RELEASE_LOCK_TOKEN: 'tok' },
      errorLog: (line) => errors.push(line)
    })
  ).toBe(2);
  expect(errors.at(-1)).toContain('takes no arguments');
});
