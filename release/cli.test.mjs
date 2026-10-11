import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test, vi } from 'vitest';
import { main, parseReleaseArgs } from '../release.mjs';
import {
  RELEASE_BASE_URL,
  WORKSPACE_ROOT,
  readProductVersion
} from './product.mjs';
import { RELEASE_PLATFORM_KEYS } from './release-platforms.mjs';
import { GAME_LIBS_LOCK_PATH } from './game-libs.mjs';
import { createArtifactManifest } from './artifact-manifest.mjs';
import { MemoryStore } from './test-support/memory-store.mjs';
import { writeArtifacts } from './test-support/artifact-fixture.mjs';
import { runFixtureGit } from '../scripts/test-support/git-fixture.mjs';

const roots = [];
afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

function fixture() {
  const workspaceRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-release-cli-')
  );
  roots.push(workspaceRoot);
  for (const file of [
    'VERSION',
    'README.md',
    'README_en.md',
    'release/payload.json',
    'release/generated/Payload.targets',
    GAME_LIBS_LOCK_PATH,
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json',
    ...[
      'package.json',
      'package-lock.json',
      'src-tauri/Cargo.toml',
      'src-tauri/Cargo.lock',
      'src-tauri/history-database-compatibility.json',
      'src-tauri/tauri.conf.json',
      'src-tauri/tauri.macos.conf.json',
      'src-tauri/tauri.windows.conf.json'
    ].map((file) => `bazaarplusplus-installer/${file}`)
  ]) {
    const target = path.join(workspaceRoot, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(WORKSPACE_ROOT, file), target);
  }
  return {
    workspaceRoot,
    log: vi.fn(),
    createStore: vi.fn(() => {
      throw new Error('this test must not reach R2');
    })
  };
}

const managed = '-p:ManagedPath=C:/Games/The Bazaar/Managed';
test.each(['prepare', 'build'])(
  '%s preserves MSBuild argument boundaries after --',
  (command) => {
    expect(
      parseReleaseArgs([command, '--platform', 'windows', '--', managed])
    ).toEqual({
      command,
      platform: 'windows',
      msbuildArgs: [managed],
      latest: false,
      allowUnverifiedMirror: false,
      withoutMainlandMirror: false
    });
  }
);

test.each([
  ['unknown'],
  ['check', '--unknown'],
  ['check', '--platform', 'macos'],
  ['sync', '--platform=windows'],
  ['promote', 'macos'],
  ['verify-mirror', 'windows'],
  ['prepare'],
  ['build', '--platform'],
  ['upload', '--platform', 'linux'],
  ['build', '--platform', 'macos', '--platform', 'windows'],
  ['build', '--platform', 'macos', 'extra'],
  ['build', '--platform', 'macos', managed],
  ['build', '--platform', 'macos', '--', '-p:Version=1.2.3'],
  ['prepare', '--platform', 'macos', '--', '-p:ManagedPath=game;Version=1.2.3'],
  ['prepare', '--platform', 'macos', '--', '-p:ManagedPath='],
  ['upload', '--platform', 'macos', '--', managed],
  ['check', '--', managed],
  ['verify-mirror', '--', managed],
  ['verify-mirror', '--latest', '--latest'],
  ['verify-mirror', '--allow-unverified-mirror'],
  ['mirror'],
  ['mirror-all'],
  ['mirror-all', '--windows-url', 'https://mirror.example/win'],
  ['mirror-all', '--macos-url', 'https://mirror.example/mac'],
  [
    'mirror-all',
    '--windows-url',
    'https://mirror.example/win',
    '--macos-url',
    'http://mirror.example/mac'
  ],
  ['mirror-all', '--platform', 'macos'],
  ['mirror-all', '--url', 'https://mirror.example/mac'],
  ['mirror-all', '--latest'],
  ['mirror-all', '--without-mainland-mirror'],
  [
    'mirror-all',
    '--windows-url',
    'https://mirror.example/win',
    '--windows-url',
    'https://mirror.example/win'
  ],
  ['promote', '--windows-url', 'https://mirror.example/win'],
  ['mirror', '--platform', 'macos'],
  ['mirror', '--url', 'https://mirror.example/mac'],
  ['mirror', '--platform', 'linux', '--url', 'https://mirror.example/mac'],
  ['mirror', '--platform', 'macos', '--url', 'http://mirror.example/mac'],
  ['mirror', '--platform', 'macos', '--url', 'not-a-url'],
  ['mirror', '--platform', 'macos', '--url', 'https://u:p@mirror.example/m'],
  [
    'mirror',
    '--platform',
    'macos',
    '--url',
    'https://mirror.example/mac',
    '--latest'
  ],
  [
    'mirror',
    '--platform',
    'macos',
    '--url',
    'https://mirror.example/mac',
    '--without-mainland-mirror'
  ],
  [
    'mirror',
    '--platform',
    'macos',
    '--url',
    'https://mirror.example/mac',
    '--',
    managed
  ],
  ['upload', '--platform', 'macos', '--url', 'https://mirror.example/mac'],
  ['promote', '--latest'],
  ['promote', '--platform', 'linux'],
  ['promote', '--platform'],
  ['promote', '--allow-unverified-mirror'],
  ['promote', '--without-mainland-mirror', '--without-mainland-mirror'],
  ['check', '--without-mainland-mirror'],
  ['assert-build-owner', '--'],
  ['--help', 'build']
])(
  'invalid invocation %j rejects before filesystem or release effects',
  async (...args) => {
    const effect = vi.fn();
    await expect(
      main(args, {
        workspaceRoot: '/missing-product-workspace',
        createStore: effect,
        log: effect
      })
    ).rejects.toThrow();
    expect(effect).not.toHaveBeenCalled();
  }
);

test.each([[], ['--help'], ['-h']])(
  'help %j needs no product files or credentials',
  async (...args) => {
    const log = vi.fn();
    await main(args, { workspaceRoot: '/missing-product-workspace', log });
    expect(log).toHaveBeenCalledWith(
      expect.stringContaining('Product release commands')
    );
  }
);

test.each([
  ['macos', 'win32'],
  ['windows', 'darwin'],
  ['macos', 'linux']
])(
  'build %s rejects host %s before doing work',
  async (platform, hostPlatform) => {
    const effect = vi.fn();
    await expect(
      main(['build', '--platform', platform], {
        workspaceRoot: '/missing-product-workspace',
        hostPlatform,
        createStore: effect,
        log: effect
      })
    ).rejects.toThrow(`Build ${platform} on its native host`);
    expect(effect).not.toHaveBeenCalled();
  }
);

test('sync projects VERSION into both badges and toolchains, and check is read-only', async () => {
  const options = fixture();
  fs.writeFileSync(path.join(options.workspaceRoot, 'VERSION'), '6.2.1\n');
  await expect(main(['check'], options)).rejects.toThrow(/Version mismatch/);
  await main(['sync'], options);
  const files = fs
    .readdirSync(options.workspaceRoot, { recursive: true })
    .filter((file) =>
      fs.statSync(path.join(options.workspaceRoot, file)).isFile()
    );
  const snapshot = () =>
    files.map((file) =>
      fs.readFileSync(path.join(options.workspaceRoot, file), 'utf8')
    );
  const before = snapshot();
  await main(['check'], options);
  expect(snapshot()).toEqual(before);
  for (const file of ['README.md', 'README_en.md'])
    expect(
      fs.readFileSync(path.join(options.workspaceRoot, file), 'utf8')
    ).toContain('badge/version-6.2.1-');
  expect(options.createStore).not.toHaveBeenCalled();
});

test.each(['README.md', 'README_en.md'])(
  'check rejects a stale or missing %s badge',
  async (name) => {
    const options = fixture();
    const file = path.join(options.workspaceRoot, name);
    const before = fs.readFileSync(file, 'utf8');
    fs.writeFileSync(
      file,
      before.replace(/badge\/version-[^-]+-/, 'badge/version-0.0.1-')
    );
    await expect(main(['check'], options)).rejects.toThrow(/version badge/);
    fs.writeFileSync(
      file,
      before.replace(/badge\/version-[^-]+-/, 'badge/product-0.0.1-')
    );
    await expect(main(['check'], options)).rejects.toThrow(
      /Expected one product version badge/
    );
    await expect(main(['sync'], options)).rejects.toThrow(
      /Expected one product version badge/
    );
  }
);

test('check and dispatch reject an updater endpoint outside the release origin before store creation', async () => {
  const options = fixture();
  const file = path.join(
    options.workspaceRoot,
    'bazaarplusplus-installer/src-tauri/tauri.conf.json'
  );
  const config = JSON.parse(fs.readFileSync(file, 'utf8'));
  config.plugins.updater.endpoints = ['https://wrong.example/latest.json'];
  fs.writeFileSync(file, JSON.stringify(config));
  await expect(main(['check'], options)).rejects.toThrow(
    /Tauri updater endpoints/
  );
  await expect(
    main(['upload', '--platform', 'macos'], options)
  ).rejects.toThrow(/Tauri updater endpoints/);
  expect(options.createStore).not.toHaveBeenCalled();
});

test('check catches stale Payload projection and platform overlays', async () => {
  const options = fixture();
  const projection = path.join(
    options.workspaceRoot,
    'release/generated/Payload.targets'
  );
  fs.appendFileSync(projection, '<!-- stale -->');
  await expect(main(['check'], options)).rejects.toThrow();
  await main(['sync'], options);
  const overlay = path.join(
    options.workspaceRoot,
    'bazaarplusplus-installer/src-tauri/tauri.windows.conf.json'
  );
  const config = JSON.parse(fs.readFileSync(overlay, 'utf8'));
  config.bundle.targets = ['msi'];
  fs.writeFileSync(overlay, JSON.stringify(config));
  await expect(main(['check'], options)).rejects.toThrow(
    /bundle targets drifted/
  );
});

// Canned public release origin: an uploaded fragment and mirror record per
// platform at `version`, the shared platform fixtures as the published
// manifests, and a share page serving each recorded installer.
function publicOrigin(version) {
  const files = new Map();
  const pages = new Map();
  for (const key of RELEASE_PLATFORM_KEYS) {
    const base = `${RELEASE_BASE_URL}/${version}/${key}`;
    const fileName = `BazaarPlusPlus_${version}_${key}.bin`;
    const record = (category, name) => ({
      url: `${base}/${category}/${name}`,
      size: 1,
      sha256: 'b'.repeat(64)
    });
    files.set(`${base}/updater/platform-manifest.json`, {
      schemaVersion: 1,
      version,
      platform: key,
      gitCommit: 'a'.repeat(40),
      installer: record('installer', fileName),
      updater: { ...record('updater', fileName), signature: 'signature' },
      signatureFile: record('updater', `${fileName}.sig`)
    });
    const mirror = `https://mirror.example/${key}-${version}`;
    files.set(`${base}/mirror/mainland.json`, {
      schemaVersion: 1,
      version,
      platform: key,
      url: mirror,
      fileName,
      verified: true
    });
    pages.set(mirror, fileName);
    const published = JSON.parse(
      fs.readFileSync(
        path.join(import.meta.dirname, 'fixtures', `latest/${key}.json`),
        'utf8'
      )
    );
    files.set(`${RELEASE_BASE_URL}/latest/${key}.json`, published);
    const { url, mainlandUrl } = published.downloads[key];
    pages.set(mainlandUrl, decodeURIComponent(url.split('/').at(-1)));
  }
  return vi.fn(async (url) => {
    if (files.has(url))
      return new Response(JSON.stringify(files.get(url)), { status: 200 });
    if (pages.has(url))
      return new Response(
        `<html><head><title>${pages.get(url)} - 蓝奏云</title></head></html>`,
        { status: 200 }
      );
    return new Response('', { status: 404 });
  });
}

test('verify-mirror is read-only: no store, no source alignment, VERSION only without --latest', async () => {
  const options = fixture();
  const file = path.join(
    options.workspaceRoot,
    'bazaarplusplus-installer/src-tauri/tauri.conf.json'
  );
  const config = JSON.parse(fs.readFileSync(file, 'utf8'));
  config.plugins.updater.endpoints = ['https://wrong.example/latest.json'];
  fs.writeFileSync(file, JSON.stringify(config));
  const version = readProductVersion(options.workspaceRoot);
  const fetch = publicOrigin(version);
  vi.stubGlobal('fetch', fetch);
  await main(['verify-mirror'], options);
  expect(options.log).toHaveBeenLastCalledWith(
    `Mainland mirror verified for ${version}`
  );
  fetch.mockClear();
  await main(['verify-mirror', '--platform', 'windows'], options);
  expect(fetch).not.toHaveBeenCalledWith(
    expect.stringContaining('/darwin-aarch64/'),
    expect.anything()
  );
  await main(['verify-mirror', '--latest'], options);
  expect(options.log).toHaveBeenLastCalledWith(
    'Mainland mirror verified for 3.1.2 / 3.1.1'
  );
  await main(['verify-mirror', '--latest', '--platform', 'macos'], options);
  expect(options.log).toHaveBeenLastCalledWith(
    'Mainland mirror verified for 3.1.1'
  );
  expect(options.createStore).not.toHaveBeenCalled();
});

test.each([
  [
    'package-lock root entry',
    'bazaarplusplus-installer/package-lock.json',
    (text) => {
      const lock = JSON.parse(text);
      lock.packages[''].version = '0.0.1';
      return `${JSON.stringify(lock, null, 2)}\n`;
    },
    /packageLockRootVersion=0\.0\.1/
  ],
  [
    'Cargo.lock root package',
    'bazaarplusplus-installer/src-tauri/Cargo.lock',
    (text) =>
      text.replace(
        /(\[\[package\]\]\r?\nname = "bppinstaller"\r?\nversion = ")[^"]+/,
        '$10.0.1'
      ),
    /cargoLockVersion=0\.0\.1/
  ]
])('check rejects a stale %s', async (_name, file, stale, error) => {
  const options = fixture();
  const target = path.join(options.workspaceRoot, file);
  const before = fs.readFileSync(target, 'utf8');
  fs.writeFileSync(target, stale(before));
  expect(fs.readFileSync(target, 'utf8')).not.toBe(before);
  await expect(main(['check'], options)).rejects.toThrow(/Version mismatch/);
  await expect(main(['check'], options)).rejects.toThrow(error);
  await main(['sync'], options);
  await main(['check'], options);
});

test('sync changes only the version line of tauri.conf.json', async () => {
  const options = fixture();
  const file = path.join(
    options.workspaceRoot,
    'bazaarplusplus-installer/src-tauri/tauri.conf.json'
  );
  const before = fs.readFileSync(file, 'utf8');
  const version = readProductVersion(options.workspaceRoot);
  fs.writeFileSync(path.join(options.workspaceRoot, 'VERSION'), '6.2.1\n');
  await main(['sync'], options);
  expect(fs.readFileSync(file, 'utf8')).toBe(
    before.replace(`"version": "${version}"`, '"version": "6.2.1"')
  );
});

// Release E2E anchor: the real coordinator runs upload, mirror-all and
// promote for a lockstep 3.1.1, then a single-platform 3.1.2, against an
// in-memory R2 store, from a temporary git checkout of the release inputs.
// The manifests it publishes must equal the shared fixtures byte for byte;
// BPP_UPDATE_GOLDENS=1 rewrites them from this run instead.
const goldenFiles = [
  'latest.json',
  'latest/darwin-aarch64.json',
  'latest/windows-x86_64.json'
];
const anchorMirrors = {
  'https://cauyxy.lanzout.com/bppwin311': 'BazaarPlusPlus_3.1.1_x64-setup.exe',
  'https://cauyxy.lanzout.com/bppmac311': 'BazaarPlusPlus_3.1.1_aarch64.dmg',
  'https://cauyxy.lanzout.com/bppwin312': 'BazaarPlusPlus_3.1.2_x64-setup.exe'
};

function commitFixture(cwd, message) {
  runFixtureGit(['add', '-A'], { cwd });
  runFixtureGit(
    [
      '-c',
      'user.name=Release Anchor',
      '-c',
      'user.email=release-anchor@example.test',
      'commit',
      '-qm',
      message
    ],
    { cwd }
  );
  return runFixtureGit(['rev-parse', 'HEAD'], { cwd }).trim();
}

test('publish pipeline reproduces the shared fixtures', async () => {
  const { workspaceRoot } = fixture();
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  const store = new MemoryStore();
  const run = (args, extra = {}) =>
    main(args, {
      workspaceRoot,
      log: () => {},
      createStore: () => store,
      probeMirror: async (url) => ({
        status: 200,
        html: `<html><head><title>${anchorMirrors[url]} - 蓝奏云</title></head></html>`
      }),
      ...extra
    });
  const release = async (version) => {
    fs.writeFileSync(path.join(workspaceRoot, 'VERSION'), `${version}\n`);
    await run(['sync']);
    return commitFixture(workspaceRoot, `Release ${version}`);
  };
  const build = (platform, version, artifact) => {
    writeArtifacts(rootDir, platform, { version, ...artifact });
    createArtifactManifest({ rootDir, platform, version });
  };

  runFixtureGit(['init', '-q'], { cwd: workspaceRoot });
  const exclude = path.join(workspaceRoot, '.git/info/exclude');
  fs.mkdirSync(path.dirname(exclude), { recursive: true });
  fs.appendFileSync(
    exclude,
    'bazaarplusplus-installer/src-tauri/target/\nbazaarplusplus-installer/src-tauri/resources/\n'
  );

  const lockstepCommit = await release('3.1.1');
  build('windows', '3.1.1', {
    installer: 'windows installer 3.1.1\n',
    signature: 'windows-signature\n'
  });
  build('macos', '3.1.1', {
    installer: 'macos installer 3.1.1\n',
    updater: 'macos updater 3.1.1\n',
    signature: 'macos-signature\n'
  });
  await run(['upload', '--platform', 'windows']);
  await run(['upload', '--platform', 'macos']);
  await run([
    'mirror-all',
    '--windows-url',
    'https://cauyxy.lanzout.com/bppwin311',
    '--macos-url',
    'https://cauyxy.lanzout.com/bppmac311'
  ]);
  await run(['promote'], { now: new Date('2026-09-19T00:00:00.000Z') });

  const aheadCommit = await release('3.1.2');
  build('windows', '3.1.2', {
    installer: 'windows installer 3.1.2\n',
    signature: 'windows-signature-3.1.2\n'
  });
  await run(['upload', '--platform', 'windows']);
  await run([
    'mirror',
    '--platform',
    'windows',
    '--url',
    'https://cauyxy.lanzout.com/bppwin312'
  ]);
  await run(['promote', '--platform', 'windows'], {
    now: new Date('2026-09-20T00:00:00.000Z')
  });

  // The temporary checkout's commits depend on every copied file; the
  // fixtures keep synthetic commits instead.
  for (const file of goldenFiles) {
    const published = (await store.get(file)).bytes
      .toString('utf8')
      .replaceAll(lockstepCommit, 'a'.repeat(40))
      .replaceAll(aheadCommit, 'c'.repeat(40));
    const produced = `${JSON.stringify(JSON.parse(published), null, 2)}\n`;
    const golden = path.join(import.meta.dirname, 'fixtures', file);
    if (process.env.BPP_UPDATE_GOLDENS === '1')
      fs.writeFileSync(golden, produced);
    expect(produced, file).toBe(fs.readFileSync(golden, 'utf8'));
  }
});

test('internal ownership dispatch requires a live build lock and does not run product alignment', async () => {
  const workspaceRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-build-owner-')
  );
  roots.push(workspaceRoot);
  const file = path.join(
    workspaceRoot,
    'bazaarplusplus-installer/src-tauri/target/payload.lock'
  );
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(
    file,
    JSON.stringify({
      token: 'owned',
      kind: 'build',
      host: os.hostname(),
      pid: process.pid
    })
  );
  vi.stubEnv('BPP_RELEASE_LOCK_TOKEN', 'wrong');
  await expect(main(['assert-build-owner'], { workspaceRoot })).rejects.toThrow(
    /product build lock/
  );
  vi.stubEnv('BPP_RELEASE_LOCK_TOKEN', 'owned');
  await main(['assert-build-owner'], { workspaceRoot });
});

// The Snapshot Lock gate of `check` (ADR 0004). Failure modes: a lock missing
// one of the six keys fails; an entry with a malformed sha256 fails, and so
// does every command that runs source alignment, before any store is created;
// an empty entry warns; an entry captured more than staleAfterDays ago warns;
// warnings leave the command on its success path.
function editLock(options, mutate) {
  const file = path.join(options.workspaceRoot, GAME_LIBS_LOCK_PATH);
  const lock = JSON.parse(fs.readFileSync(file, 'utf8'));
  mutate(lock);
  fs.writeFileSync(file, `${JSON.stringify(lock, null, 2)}\n`);
}
const windowsOnlineEntry = {
  gameVersion: '1.0.12600-windows-x64-0a1b2c3d',
  sha256: 'b'.repeat(64),
  buildid: '25661999',
  steamBranch: 'public',
  capturedAt: '2026-09-01T00:00:00.000Z'
};

test('check fails on a Snapshot Lock missing a key or holding a malformed sha256', async () => {
  const missing = fixture();
  editLock(missing, (lock) => {
    delete lock.entries['windows-ptr'];
  });
  await expect(main(['check'], missing)).rejects.toThrow(
    /exactly the six keys/
  );
  const malformed = fixture();
  editLock(malformed, (lock) => {
    lock.entries['windows-online'] = {
      ...windowsOnlineEntry,
      sha256: 'not-a-digest'
    };
  });
  await expect(main(['check'], malformed)).rejects.toThrow(
    /Lock entry windows-online has an invalid sha256/
  );
  await expect(
    main(['upload', '--platform', 'windows'], malformed)
  ).rejects.toThrow(/invalid sha256/);
  expect(malformed.createStore).not.toHaveBeenCalled();
});

test('check warns on empty and stale Snapshot Lock entries and still passes', async () => {
  const options = fixture();
  editLock(options, (lock) => {
    for (const key of Object.keys(lock.entries)) lock.entries[key] = null;
    lock.entries['windows-online'] = windowsOnlineEntry;
    lock.entries['windows-staging'] = {
      gameVersion: '1.0.12575-staging-windows-x64-fecb8f8e',
      sha256: 'c'.repeat(64),
      buildid: '25661913',
      steamBranch: 'staging',
      capturedAt: '2026-10-01T00:00:00.000Z'
    };
  });
  await main(['check'], {
    ...options,
    now: new Date('2026-10-03T00:00:00.000Z')
  });
  const lines = options.log.mock.calls.map(([line]) => line);
  const warnings = lines.filter((line) => line.startsWith('WARNING: '));
  expect(warnings).toHaveLength(5);
  expect(warnings).toContainEqual(
    expect.stringMatching(/^WARNING: macos-online: no snapshot captured/)
  );
  expect(warnings).toContainEqual(
    expect.stringMatching(
      /^WARNING: windows-online: 1\.0\.12600-windows-x64-0a1b2c3d was captured 32 days ago \(staleAfterDays 30\)/
    )
  );
  expect(warnings.some((line) => line.includes('windows-staging'))).toBe(false);
  expect(lines.at(-1)).toMatch(
    /Snapshot Lock and release configuration aligned/
  );
});

test('check refuses 6.2.0 while the V5 import module exists and passes on 6.1.x', async () => {
  const options = fixture();
  const write = (version) =>
    fs.writeFileSync(
      path.join(options.workspaceRoot, 'VERSION'),
      `${version}\n`
    );
  fs.mkdirSync(
    path.join(
      options.workspaceRoot,
      'bazaarplusplus-installer/src-tauri/src/v5_import'
    ),
    { recursive: true }
  );
  write('6.1.9');
  await main(['sync'], options);
  await main(['check'], options);
  write('6.2.0');
  await main(['sync'], options);
  await expect(main(['check'], options)).rejects.toThrow(
    /bazaarplusplus-installer\/src-tauri\/src\/v5_import\/ must be deleted before 6\.2\.0 \(VERSION is 6\.2\.0\); see .*issues\/264 and bazaarplusplus-installer\/docs\/adr\/0008-legacy-data-roots-and-v5-import\.md/
  );
  expect(options.createStore).not.toHaveBeenCalled();
});

test.each([
  [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json'
  ],
  ['bazaarplusplus-installer/src-tauri/history-database-compatibility.json']
])('check fails when only %s renames the Data Root', async (file) => {
  const options = fixture();
  await main(['check'], options);
  const target = path.join(options.workspaceRoot, file);
  const contract = JSON.parse(fs.readFileSync(target, 'utf8'));
  fs.writeFileSync(
    target,
    JSON.stringify({ ...contract, dataRootDirectoryName: 'BazaarPlusPlusV0' })
  );
  await expect(main(['check'], options)).rejects.toThrow(
    /Data Root BazaarPlusPlusV\d[\s\S]*installer reads BazaarPlusPlusV\d/
  );
  expect(options.createStore).not.toHaveBeenCalled();
});
