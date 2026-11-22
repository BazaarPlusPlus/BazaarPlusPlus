import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { expect, test, vi } from 'vite-plus/test';
import {
  MAINLAND_MIRROR,
  VERIFY_REMEDY,
  assertMainlandMirrors,
  checkMainlandMirror,
  mainlandMirrorUrl,
  pushMainlandMirror,
  verifyMainlandMirrors
} from './mirror.ts';
import type { MirrorCheckResult } from './mirror.ts';
import type { FetchImpl } from './r2-store.ts';
import { buildPlatformManifest, fragmentKey } from './manifest.ts';
import type {
  AndroidPlatformManifest,
  DesktopPlatformFragment
} from './manifest.ts';
import { MemoryStore } from './test-support/memory-store.ts';
import { uploadAndroid } from './test-support/android-fixture.ts';
import { mirrorNetwork } from './test-support/mirror-network.ts';

const baseUrl = 'https://cdn.bazaarplusplus.com/installer';
const sha256 = (bytes: Buffer) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const windowsInstaller = (version: string) =>
  `${baseUrl}/${version}/windows-x86_64/installer/BazaarPlusPlus_${version}_x64-setup.exe`;
const macInstaller = (version: string) =>
  `${baseUrl}/${version}/darwin-aarch64/installer/BazaarPlusPlus_${version}_aarch64.dmg`;
const installerBytes = (version: string, platform: string) =>
  Buffer.from(`${platform} installer ${version}\n`);

const served = (bytes: Buffer, headers: Record<string, string> = {}) =>
  new Response(new Uint8Array(bytes), {
    status: 200,
    headers: { 'content-length': String(bytes.length), ...headers }
  });

const expected = Buffer.from('BazaarPlusPlus installer bytes\n');
const check = (fetchImpl: FetchImpl) =>
  checkMainlandMirror({
    platform: 'windows-x86_64',
    url: 'https://mirror.example/win.exe',
    fileName: 'win.exe',
    artifact: { size: expected.length, sha256: sha256(expected) },
    fetchImpl
  });

test('a mirror is verified only when it serves the release bytes', async () => {
  expect(await check(async () => served(expected))).toMatchObject({
    outcome: 'verified',
    detail: `serves ${expected.length} bytes with sha256 ${sha256(expected)}`
  });
  const same = Buffer.from(expected);
  same[0] ^= 1;
  expect(await check(async () => served(same))).toMatchObject({
    outcome: 'missing-or-different',
    detail: `serves sha256 ${sha256(same)}, expected ${sha256(expected)}`
  });
  const shorter = expected.subarray(1);
  expect(
    await check(async () => new Response(new Uint8Array(shorter)))
  ).toMatchObject({
    outcome: 'missing-or-different',
    detail: `serves ${shorter.length} bytes, expected ${expected.length}`
  });
});

test('a Content-Length that differs from the artifact fails without reading the body', async () => {
  let pulled = false;
  let cancelled = false;
  const body = new ReadableStream<Uint8Array>(
    {
      pull(controller) {
        pulled = true;
        controller.enqueue(new Uint8Array(expected));
        controller.close();
      },
      cancel() {
        cancelled = true;
      }
    },
    { highWaterMark: 0 }
  );
  const result = await check(
    async () =>
      new Response(body, {
        status: 200,
        headers: { 'content-length': String(expected.length + 1) }
      })
  );
  expect(result).toMatchObject({
    outcome: 'missing-or-different',
    detail: `serves ${expected.length + 1} bytes, expected ${expected.length}`
  });
  expect(pulled).toBe(false);
  expect(cancelled).toBe(true);
});

test.each([
  [
    'a missing file',
    async () => new Response('', { status: 404 }),
    'missing-or-different',
    'HTTP 404'
  ],
  [
    'a server error',
    async () => new Response('', { status: 503 }),
    'unverifiable',
    'HTTP 503'
  ],
  [
    'a failed request',
    async (): Promise<Response> => {
      throw new Error('ECONNRESET');
    },
    'unverifiable',
    'request failed: ECONNRESET'
  ]
])('classifies %s', async (_, fetchImpl, outcome, detail) => {
  expect(await check(fetchImpl)).toMatchObject({ outcome, detail });
});

test('the assertion names every failed mirror; only an explicit override downgrades it to a warning', () => {
  const results: MirrorCheckResult[] = [
    {
      platform: 'windows-x86_64',
      url: 'https://mirror.example/win',
      fileName: 'x.exe',
      outcome: 'verified',
      detail: 'serves 1 bytes'
    },
    {
      platform: 'darwin-aarch64',
      url: 'https://mirror.example/mac',
      fileName: 'y.dmg',
      outcome: 'unverifiable',
      detail: 'HTTP 503'
    }
  ];
  let message = '';
  try {
    assertMainlandMirrors(results);
  } catch (error) {
    if (error instanceof Error) message = error.message;
  }
  expect(message).toMatch(/--allow-unverified-mirror/);
  expect(message).toMatch(/mirror\.example\/mac \(HTTP 503\)/);
  expect(message).not.toContain('mirror.example/win');
  const log = vi.fn();
  expect(assertMainlandMirrors(results, { allowUnverified: true, log })).toBe(
    results
  );
  expect(log).toHaveBeenCalledTimes(1);
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(/WARNING.*mirror\.example\/mac \(HTTP 503\)/)
  );
});

const selfUpdate = (version: string, platform: string) => ({
  managedPayload: {
    url: `${baseUrl}/${version}/${platform}/managed/BazaarPlusPlus_${version}_${platform}-managed.zip`,
    size: 100,
    sha256: 'b'.repeat(64),
    signature: 'managed-sig'
  },
  selfUpdatable: false,
  historyDatabaseUserVersion: 1,
  nonManagedPayloadDigest: 'e'.repeat(64)
});

function fragment(version: string, platform: string) {
  const installer =
    platform === 'windows-x86_64'
      ? windowsInstaller(version)
      : macInstaller(version);
  const record = (url: string) => ({
    url,
    size: 100,
    sha256: 'b'.repeat(64)
  });
  const bytes = installerBytes(version, platform);
  return {
    schemaVersion: 1,
    version,
    platform,
    gitCommit: 'a'.repeat(40),
    installer: { url: installer, size: bytes.length, sha256: sha256(bytes) },
    updater: {
      ...record(`${baseUrl}/${version}/${platform}/updater/pkg`),
      signature: 'sig'
    },
    signatureFile: record(`${baseUrl}/${version}/${platform}/updater/pkg.sig`),
    ...selfUpdate(version, platform)
  };
}

test('a push stages <version>/<file> from the verified release artifact and leaves no key behind', async () => {
  const store = new MemoryStore();
  const uploaded = fragment('5.5.0', 'windows-x86_64');
  const bytes = installerBytes('5.5.0', 'windows-x86_64');
  store.objects.set(uploaded.installer.url.slice(baseUrl.length + 1), {
    bytes,
    etag: '"1"'
  });
  const network = mirrorNetwork(store, baseUrl);
  const push = (runRsync = network.runRsync) =>
    pushMainlandMirror({
      version: '5.5.0',
      fragment: uploaded as DesktopPlatformFragment,
      sshKey: '-----BEGIN OPENSSH PRIVATE KEY-----\nkey',
      fetchImpl: network.fetchImpl,
      runRsync
    });
  // The temporary directory holds the staging tree, the key and known_hosts.
  const workOf = (args: readonly string[]) =>
    path.dirname(path.dirname(args.at(-2) as string));
  await push();
  const [pushed] = network.pushes;
  expect(pushed.files).toEqual({
    '5.5.0/BazaarPlusPlus_5.5.0_x64-setup.exe': bytes
  });
  expect(pushed.args).toEqual([
    '-rt',
    '-e',
    expect.stringMatching(
      /^ssh -i \S+ -o UserKnownHostsFile=\S+ -o StrictHostKeyChecking=yes -o IdentitiesOnly=yes -o BatchMode=yes$/
    ),
    expect.stringMatching(/[/\\]5\.5\.0$/),
    'bpp-mirror@1.14.150.142:'
  ]);
  expect(pushed.key).toBe('-----BEGIN OPENSSH PRIVATE KEY-----\nkey\n');
  if (process.platform !== 'win32') expect(pushed.keyMode).toBe(0o600);
  expect(pushed.knownHosts).toBe(
    '1.14.150.142 ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAII5gPFM50Rx1AaeL5aic3VbTeYintL6QefTEUMX6c0Dj\n'
  );
  expect(
    network.served.get('5.5.0/BazaarPlusPlus_5.5.0_x64-setup.exe')
  ).toEqual(bytes);
  expect(fs.existsSync(workOf(pushed.args))).toBe(false);
  let failed: readonly string[] = [];
  await expect(
    push(async (args) => {
      failed = args;
      throw new Error('rsync failed');
    })
  ).rejects.toThrow('rsync failed');
  expect(fs.existsSync(workOf(failed))).toBe(false);
});

test('a push refuses a release artifact whose bytes differ from the fragment before it reaches rsync', async () => {
  const store = new MemoryStore();
  const uploaded = fragment(
    '5.5.0',
    'windows-x86_64'
  ) as DesktopPlatformFragment;
  store.objects.set(uploaded.installer.url.slice(baseUrl.length + 1), {
    bytes: installerBytes('5.4.0', 'windows-x86_64'),
    etag: '"1"'
  });
  const network = mirrorNetwork(store, baseUrl);
  const runRsync = vi.fn(network.runRsync);
  const push = () =>
    pushMainlandMirror({
      version: '5.5.0',
      fragment: uploaded,
      sshKey: 'key\n',
      fetchImpl: network.fetchImpl,
      runRsync
    });
  await expect(push()).rejects.toThrow(/the fragment records/);
  store.objects.clear();
  await expect(push()).rejects.toThrow(/responded with HTTP 404/);
  expect(runRsync).not.toHaveBeenCalled();
});

test('the mirror address is the version directory on the mirror host', () => {
  expect(mainlandMirrorUrl('6.4.4', 'BazaarPlusPlus_6.4.4_x64-setup.exe')).toBe(
    `${MAINLAND_MIRROR.baseUrl}/6.4.4/BazaarPlusPlus_6.4.4_x64-setup.exe`
  );
});

function mirrorRecord(version: string, platform: string, url: string) {
  return {
    schemaVersion: 1,
    version,
    platform,
    url,
    fileName:
      platform === 'windows-x86_64'
        ? `BazaarPlusPlus_${version}_x64-setup.exe`
        : `BazaarPlusPlus_${version}_aarch64.dmg`,
    verified: true
  };
}

function publicOrigin(
  objects: Record<string, unknown>,
  mirrors: Record<string, Buffer>
) {
  return vi.fn<FetchImpl>(async (url) => {
    if (url in mirrors) return served(mirrors[url]);
    const object = objects[url];
    return object
      ? new Response(JSON.stringify(object), { status: 200 })
      : new Response('', { status: 404 });
  });
}

const uploaded = (version: string) => ({
  [`${baseUrl}/${version}/windows-x86_64/updater/platform-manifest.json`]:
    fragment(version, 'windows-x86_64'),
  [`${baseUrl}/${version}/darwin-aarch64/updater/platform-manifest.json`]:
    fragment(version, 'darwin-aarch64')
});
const recorded = (version: string) => ({
  [`${baseUrl}/${version}/windows-x86_64/mirror/mainland.json`]: mirrorRecord(
    version,
    'windows-x86_64',
    'https://mirror.example/win'
  ),
  [`${baseUrl}/${version}/darwin-aarch64/mirror/mainland.json`]: mirrorRecord(
    version,
    'darwin-aarch64',
    'https://mirror.example/mac'
  )
});
const mirrors = (version: string) => ({
  'https://mirror.example/win': installerBytes(version, 'windows-x86_64'),
  'https://mirror.example/mac': installerBytes(version, 'darwin-aarch64')
});

const ANDROID = 'android-aarch64';
const androidApk = (version: string) =>
  `BazaarPlusPlus_${version}_${ANDROID}.apk`;

async function androidRelease(version: string) {
  const store = new MemoryStore();
  const { fragment, appKey } = await uploadAndroid(store, {
    version,
    gitCommit: 'a'.repeat(40),
    baseUrl
  });
  const manifest = (mainlandUrl?: string) => {
    const built = buildPlatformManifest({
      version,
      fragment
    }) as AndroidPlatformManifest;
    if (mainlandUrl) built.downloads[ANDROID].mainlandUrl = mainlandUrl;
    return built;
  };
  return { fragment, manifest, apk: store.objects.get(appKey)!.bytes };
}

test('before promotion the read-only check re-checks the recorded mirrors from the public origin', async () => {
  const fetchImpl = publicOrigin(
    { ...uploaded('5.5.0'), ...recorded('5.5.0') },
    mirrors('5.5.0')
  );
  const log = vi.fn();
  const verified = await verifyMainlandMirrors({
    baseUrl,
    version: '5.5.0',
    fetchImpl,
    log
  });
  expect(verified.version).toBe('5.5.0');
  expect(verified.results.map((result) => result.outcome)).toEqual([
    'verified',
    'verified'
  ]);
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(/^windows-x86_64: verified https:/)
  );
  await expect(
    verifyMainlandMirrors({ baseUrl, version: '5.6.0', fetchImpl })
  ).rejects.toThrow(/5\.6\.0 is not uploaded yet/);
  await expect(
    verifyMainlandMirrors({
      baseUrl,
      version: '5.5.0',
      fetchImpl: publicOrigin(uploaded('5.5.0'), mirrors('5.5.0'))
    })
  ).rejects.toThrow(/has no mainland mirror recorded; run mirror first/);
});

test('the check rejects legacy fragments, records that do not match the installer, and mirrors that no longer serve it', async () => {
  const legacy = {
    version: '5.4.0',
    platform: 'windows-x86_64',
    url: `${baseUrl}/5.4.0/windows-x86_64/updater/x.exe`,
    signature: 'sig'
  };
  await expect(
    verifyMainlandMirrors({
      baseUrl,
      version: '5.4.0',
      fetchImpl: publicOrigin(
        {
          [`${baseUrl}/5.4.0/windows-x86_64/updater/platform-manifest.json`]:
            legacy
        },
        {}
      )
    })
  ).rejects.toThrow(/Invalid windows-x86_64 platform fragment/);
  const stale = recorded('5.5.0');
  stale[`${baseUrl}/5.5.0/windows-x86_64/mirror/mainland.json`].fileName =
    'BazaarPlusPlus_5.4.0_x64-setup.exe';
  await expect(
    verifyMainlandMirrors({
      baseUrl,
      version: '5.5.0',
      fetchImpl: publicOrigin(
        { ...uploaded('5.5.0'), ...stale },
        mirrors('5.5.0')
      )
    })
  ).rejects.toThrow(/recorded for BazaarPlusPlus_5\.4\.0_x64-setup\.exe/);
  await expect(
    verifyMainlandMirrors({
      baseUrl,
      version: '5.5.0',
      fetchImpl: publicOrigin(
        { ...uploaded('5.5.0'), ...recorded('5.5.0') },
        {
          ...mirrors('5.5.0'),
          'https://mirror.example/mac': installerBytes(
            '5.4.0',
            'darwin-aarch64'
          )
        }
      )
    })
  ).rejects.toThrow(
    /Mainland mirror check failed[\s\S]*mirror\.example\/mac \(serves sha256/
  );
});

function platformManifest(
  version: string,
  platform: string,
  mainlandUrl?: string
) {
  const installer =
    platform === 'windows-x86_64'
      ? windowsInstaller(version)
      : macInstaller(version);
  const bytes = installerBytes(version, platform);
  return {
    version,
    gitCommit: 'a'.repeat(40),
    notes: `Release ${version}`,
    pub_date: '2026-09-23T00:00:00.000Z',
    platforms: {
      [platform]: {
        url: `${baseUrl}/${version}/${platform}/updater/pkg`,
        signature: 'sig'
      }
    },
    downloads: {
      [platform]: {
        url: installer,
        size: bytes.length,
        sha256: sha256(bytes),
        ...(mainlandUrl ? { mainlandUrl } : {}),
        ...selfUpdate(version, platform)
      }
    }
  };
}

test('after promotion the check reads each published platform manifest, which may be at different versions and on any mirror host', async () => {
  const android = await androidRelease('5.4.0');
  // 6.4.3 was promoted with its mirror on another server than MAINLAND_MIRROR.
  const otherHost = 'https://47.94.82.80/bazaarplusplus/5.5.0/mac.dmg';
  const published = {
    [`${baseUrl}/latest/windows-x86_64.json`]: platformManifest(
      '5.6.0',
      'windows-x86_64',
      'https://mirror.example/win'
    ),
    [`${baseUrl}/latest/darwin-aarch64.json`]: platformManifest(
      '5.5.0',
      'darwin-aarch64',
      otherHost
    ),
    [`${baseUrl}/latest/${ANDROID}.json`]: android.manifest(
      'https://mirror.example/apk'
    )
  };
  const allMirrors = {
    'https://mirror.example/win': installerBytes('5.6.0', 'windows-x86_64'),
    [otherHost]: installerBytes('5.5.0', 'darwin-aarch64'),
    'https://mirror.example/apk': android.apk
  };
  const verified = await verifyMainlandMirrors({
    baseUrl,
    latest: true,
    fetchImpl: publicOrigin(published, allMirrors)
  });
  expect(verified.version).toBe('5.6.0 / 5.5.0 / 5.4.0');
  expect(verified.results.map((result) => result.fileName)).toEqual([
    'BazaarPlusPlus_5.6.0_x64-setup.exe',
    'BazaarPlusPlus_5.5.0_aarch64.dmg',
    androidApk('5.4.0')
  ]);
  expect(verified.results.map((result) => result.outcome)).toEqual([
    'verified',
    'verified',
    'verified'
  ]);
  const one = await verifyMainlandMirrors({
    baseUrl,
    latest: true,
    platform: 'macos',
    fetchImpl: publicOrigin(published, allMirrors)
  });
  expect(one.version).toBe('5.5.0');
  expect(one.results.map((result) => result.platform)).toEqual([
    'darwin-aarch64'
  ]);
  await expect(
    verifyMainlandMirrors({
      baseUrl,
      latest: true,
      fetchImpl: publicOrigin(
        { [`${baseUrl}/latest.json`]: { version: '5.4.0', platforms: {} } },
        {}
      )
    })
  ).rejects.toThrow(/has no platform manifest yet/);
  const log = vi.fn();
  const waived = await verifyMainlandMirrors({
    baseUrl,
    latest: true,
    fetchImpl: publicOrigin(
      {
        ...published,
        [`${baseUrl}/latest/darwin-aarch64.json`]: platformManifest(
          '5.5.0',
          'darwin-aarch64'
        ),
        [`${baseUrl}/latest/${ANDROID}.json`]: android.manifest()
      },
      {
        'https://mirror.example/win': installerBytes('5.6.0', 'windows-x86_64')
      }
    ),
    log
  });
  expect(waived.results.map((result) => result.outcome)).toEqual([
    'verified',
    'waived',
    'waived'
  ]);
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(/^darwin-aarch64: waived null \(promoted without/)
  );
});

test('before promotion Android is checked only when named, against its App APK', async () => {
  const android = await androidRelease('5.6.0');
  const fetchImpl = publicOrigin(
    {
      ...uploaded('5.6.0'),
      ...recorded('5.6.0'),
      [`${baseUrl}/${fragmentKey('5.6.0', ANDROID)}`]: android.fragment,
      [`${baseUrl}/5.6.0/${ANDROID}/mirror/mainland.json`]: {
        schemaVersion: 1,
        version: '5.6.0',
        platform: ANDROID,
        url: 'https://mirror.example/apk',
        fileName: androidApk('5.6.0'),
        verified: true
      }
    },
    { ...mirrors('5.6.0'), 'https://mirror.example/apk': android.apk }
  );
  const lockstep = await verifyMainlandMirrors({
    baseUrl,
    version: '5.6.0',
    fetchImpl
  });
  expect(lockstep.results.map((result) => result.platform)).toEqual([
    'windows-x86_64',
    'darwin-aarch64'
  ]);
  const one = await verifyMainlandMirrors({
    baseUrl,
    version: '5.6.0',
    platform: 'android',
    fetchImpl
  });
  expect(one.results).toMatchObject([
    { platform: ANDROID, fileName: androidApk('5.6.0'), outcome: 'verified' }
  ]);
});

test('a failed read-only check points at the mirror server, not at the recording override flag', async () => {
  let message = '';
  try {
    await verifyMainlandMirrors({
      baseUrl,
      version: '5.5.0',
      fetchImpl: publicOrigin(
        { ...uploaded('5.5.0'), ...recorded('5.5.0') },
        {
          'https://mirror.example/win': installerBytes(
            '5.5.0',
            'windows-x86_64'
          )
        }
      )
    });
  } catch (error) {
    if (error instanceof Error) message = error.message;
  }
  expect(message).toContain(VERIFY_REMEDY);
  expect(message).toContain('mirror.example/mac (HTTP 404)');
  expect(message).not.toContain('--allow-unverified-mirror');
});

test('the pre-promotion check can be limited to one platform', async () => {
  const fetchImpl = publicOrigin(
    { ...uploaded('5.5.0'), ...recorded('5.5.0') },
    { 'https://mirror.example/win': installerBytes('5.5.0', 'windows-x86_64') }
  );
  const verified = await verifyMainlandMirrors({
    baseUrl,
    version: '5.5.0',
    platform: 'windows',
    fetchImpl
  });
  expect(verified.results.map((result) => result.platform)).toEqual([
    'windows-x86_64'
  ]);
  await expect(
    verifyMainlandMirrors({ baseUrl, version: '5.5.0', fetchImpl })
  ).rejects.toThrow(/mirror\.example\/mac/);
});
