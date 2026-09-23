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

const roots = [];
afterEach(() => {
  vi.unstubAllEnvs();
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
    ...[
      'package.json',
      'package-lock.json',
      'src-tauri/Cargo.toml',
      'src-tauri/Cargo.lock',
      'src-tauri/tauri.conf.json',
      'src-tauri/tauri.macos.conf.json',
      'src-tauri/tauri.windows.conf.json'
    ].map((file) => `bazaarplusplus-installer/${file}`)
  ]) {
    const target = path.join(workspaceRoot, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(WORKSPACE_ROOT, file), target);
  }
  const options = {
    workspaceRoot,
    log: vi.fn(),
    prepare: vi.fn(),
    build: vi.fn(),
    bundle: vi.fn(),
    createStore: vi.fn(() => ({ offline: true })),
    upload: vi.fn(),
    mirror: vi.fn(),
    verifyMirror: vi.fn(async () => ({ version: '0.0.0', results: [] })),
    promote: vi.fn(),
    promoteOne: vi.fn(async () => ({
      platform: {},
      latest: null,
      advanced: false
    }))
  };
  return options;
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
        prepare: effect,
        build: effect,
        createStore: effect,
        upload: effect,
        mirror: effect,
        verifyMirror: effect,
        promote: effect,
        promoteOne: effect,
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
    const build = vi.fn();
    await expect(
      main(['build', '--platform', platform], {
        workspaceRoot: '/missing-product-workspace',
        hostPlatform,
        build
      })
    ).rejects.toThrow(`Build ${platform} on its native host`);
    expect(build).not.toHaveBeenCalled();
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

test('prepare and native build dispatch validated inputs and pass the shared lock token to bundling', async () => {
  const options = fixture();
  await main(['prepare', '--platform', 'windows', '--', managed], options);
  expect(options.prepare).toHaveBeenCalledWith({
    workspaceRoot: options.workspaceRoot,
    platform: 'windows',
    msbuildArgs: [managed]
  });
  options.build.mockImplementation(({ bundle }) =>
    bundle({ token: 'test-build-token' })
  );
  await main(['build', '--platform=windows', '--', managed], {
    ...options,
    hostPlatform: 'win32'
  });
  expect(options.bundle).toHaveBeenCalledWith(
    path.join(options.workspaceRoot, 'bazaarplusplus-installer'),
    'test-build-token'
  );
  expect(options.createStore).not.toHaveBeenCalled();
});

test('upload and promote receive the release-owned origin through an offline dispatch seam', async () => {
  const options = fixture();
  await main(['upload', '--platform', 'macos'], options);
  expect(options.upload).toHaveBeenCalledWith({
    workspaceRoot: options.workspaceRoot,
    platform: 'macos',
    baseUrl: RELEASE_BASE_URL,
    store: { offline: true }
  });
  const version = readProductVersion(options.workspaceRoot);
  await main(
    ['mirror', '--platform', 'macos', '--url', 'https://mirror.example/mac'],
    options
  );
  expect(options.mirror).toHaveBeenCalledWith({
    version,
    platform: 'macos',
    url: 'https://mirror.example/mac',
    store: { offline: true },
    probeMirror: expect.any(Function),
    allowUnverified: false,
    log: options.log
  });
  await main(
    [
      'mirror',
      '--platform=windows',
      '--url=https://mirror.example/win',
      '--allow-unverified-mirror'
    ],
    options
  );
  expect(options.mirror).toHaveBeenLastCalledWith(
    expect.objectContaining({
      platform: 'windows',
      url: 'https://mirror.example/win',
      allowUnverified: true
    })
  );
  await main(['promote'], options);
  expect(options.promote).toHaveBeenCalledWith({
    version,
    baseUrl: RELEASE_BASE_URL,
    store: { offline: true },
    withoutMainlandMirror: false,
    log: options.log
  });
  await main(['promote', '--without-mainland-mirror'], options);
  expect(options.promote).toHaveBeenLastCalledWith(
    expect.objectContaining({ withoutMainlandMirror: true })
  );
  await main(['promote', '--platform', 'macos'], options);
  expect(options.promoteOne).toHaveBeenCalledWith({
    version,
    platform: 'macos',
    baseUrl: RELEASE_BASE_URL,
    store: { offline: true },
    withoutMainlandMirror: false,
    log: options.log
  });
  expect(options.log).toHaveBeenLastCalledWith(
    expect.stringContaining('latest.json stays at the last lockstep release')
  );
  options.promoteOne.mockResolvedValueOnce({
    platform: {},
    latest: { version: '9.9.9' },
    advanced: true
  });
  await main(['promote', '--platform', 'macos'], options);
  expect(options.log).toHaveBeenLastCalledWith(
    expect.stringContaining('latest.json advanced')
  );
  options.promoteOne.mockResolvedValueOnce({
    platform: {},
    latest: { version: '9.9.9' },
    advanced: false
  });
  await main(['promote', '--platform', 'macos'], options);
  expect(options.log).toHaveBeenLastCalledWith(
    expect.stringContaining('latest.json already names 9.9.9')
  );
});

test('verify-mirror is read-only: no store, no source alignment, VERSION only without --latest', async () => {
  const options = fixture();
  const file = path.join(
    options.workspaceRoot,
    'bazaarplusplus-installer/src-tauri/tauri.conf.json'
  );
  const config = JSON.parse(fs.readFileSync(file, 'utf8'));
  config.plugins.updater.endpoints = ['https://wrong.example/latest.json'];
  fs.writeFileSync(file, JSON.stringify(config));
  await main(['verify-mirror'], options);
  expect(options.verifyMirror).toHaveBeenCalledWith({
    baseUrl: RELEASE_BASE_URL,
    version: readProductVersion(options.workspaceRoot),
    platform: undefined,
    latest: false,
    log: options.log
  });
  await main(['verify-mirror', '--latest'], options);
  expect(options.verifyMirror).toHaveBeenLastCalledWith(
    expect.objectContaining({ version: null, latest: true })
  );
  await main(['verify-mirror', '--platform', 'windows'], options);
  expect(options.verifyMirror).toHaveBeenLastCalledWith(
    expect.objectContaining({ platform: 'windows', latest: false })
  );
  await main(['verify-mirror', '--latest', '--platform', 'macos'], options);
  expect(options.verifyMirror).toHaveBeenLastCalledWith(
    expect.objectContaining({ platform: 'macos', latest: true })
  );
  expect(options.createStore).not.toHaveBeenCalled();
  expect(options.log).toHaveBeenCalledWith(
    'Mainland mirror verified for 0.0.0'
  );
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
