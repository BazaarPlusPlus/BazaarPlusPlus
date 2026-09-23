import { expect, test, vi } from 'vitest';
import {
  MIRROR_USER_AGENT,
  VERIFY_REMEDY,
  assertMainlandMirrors,
  checkMainlandMirror,
  classifyMirrorPage,
  fetchMirrorPage,
  verifyMainlandMirrors
} from './mirror.mjs';

const baseUrl = 'https://bppinstaller.bazaarplusplus.com';
const windowsInstaller = (version) =>
  `${baseUrl}/${version}/windows-x86_64/installer/BazaarPlusPlus_${version}_x64-setup.exe`;
const macInstaller = (version) =>
  `${baseUrl}/${version}/darwin-aarch64/installer/BazaarPlusPlus_${version}_aarch64.dmg`;
// Trimmed from the live share pages probed on 2026-09-23.
const presentPage = (fileName) =>
  `<!DOCTYPE html><html><head><title>${fileName} - 蓝奏云</title><meta name="description" content="文件大小：8.6 M"></head><body><div class="n_box_3fn">${fileName}</div></body></html>`;
const missingPage =
  '<!DOCTYPE html><html><head><title></title></head><body><div class="off">文件取消分享</div></body></html>';

test.each([
  [
    'a present share with the expected file',
    { status: 200, html: presentPage('BazaarPlusPlus_5.5.0_x64-setup.exe') },
    'verified'
  ],
  [
    'a cancelled share',
    { status: 200, html: missingPage },
    'missing-or-misnamed'
  ],
  [
    'a share serving another file',
    { status: 200, html: presentPage('BazaarPlusPlus_5.4.0_x64-setup.exe') },
    'missing-or-misnamed'
  ],
  [
    'a page whose title is not a share title',
    { status: 200, html: '<html><head><title>蓝奏云</title></head></html>' },
    'unverifiable'
  ],
  [
    'a page without a title',
    { status: 200, html: '<html><body>challenge</body></html>' },
    'unverifiable'
  ],
  ['a server error', { status: 503, html: '' }, 'unverifiable'],
  ['a failed request', { error: new Error('timeout') }, 'unverifiable']
])('classifies %s', (_, page, outcome) => {
  expect(
    classifyMirrorPage(page, 'BazaarPlusPlus_5.5.0_x64-setup.exe').outcome
  ).toBe(outcome);
});

test('title comparison decodes HTML entities', () => {
  expect(
    classifyMirrorPage(
      { status: 200, html: presentPage('A&amp;B_5.5.0.exe') },
      'A&B_5.5.0.exe'
    ).outcome
  ).toBe('verified');
});

test('a check probes the supplied address and reports the platform outcome', async () => {
  const probe = vi.fn(async () => ({
    status: 200,
    html: presentPage('BazaarPlusPlus_5.5.0_x64-setup.exe')
  }));
  const result = await checkMainlandMirror({
    platform: 'windows-x86_64',
    url: 'https://mirror.example/any-slug',
    fileName: 'BazaarPlusPlus_5.5.0_x64-setup.exe',
    probeMirror: probe
  });
  expect(result).toMatchObject({
    platform: 'windows-x86_64',
    url: 'https://mirror.example/any-slug',
    outcome: 'verified'
  });
  expect(probe).toHaveBeenCalledWith('https://mirror.example/any-slug');
});

test('the assertion names every failed share; only an explicit override downgrades it to a warning', () => {
  const results = [
    {
      platform: 'windows-x86_64',
      url: 'https://mirror.example/win',
      fileName: 'x.exe',
      outcome: 'verified',
      detail: 'share serves x.exe'
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
    message = error.message;
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

test('the page probe identifies itself as a desktop browser and reports transport failures as a result', async () => {
  const fetchImpl = vi.fn(
    async () => new Response(presentPage('a.exe'), { status: 200 })
  );
  const page = await fetchMirrorPage('https://mirror.example/win', {
    fetchImpl
  });
  expect(page.status).toBe(200);
  expect(page.html).toContain('a.exe - 蓝奏云');
  expect(fetchImpl.mock.calls[0][1]).toMatchObject({
    headers: { 'user-agent': MIRROR_USER_AGENT },
    redirect: 'follow'
  });
  const failed = await fetchMirrorPage('https://mirror.example/win', {
    fetchImpl: vi.fn().mockRejectedValue(new Error('ECONNRESET'))
  });
  expect(classifyMirrorPage(failed, 'a.exe')).toEqual({
    outcome: 'unverifiable',
    detail: 'request failed: ECONNRESET'
  });
});

function fragment(version, platform) {
  const installer =
    platform === 'windows-x86_64'
      ? windowsInstaller(version)
      : macInstaller(version);
  const record = (url) => ({ url, size: 100, sha256: 'b'.repeat(64) });
  return {
    schemaVersion: 1,
    version,
    platform,
    gitCommit: 'a'.repeat(40),
    installer: record(installer),
    updater: {
      ...record(`${baseUrl}/${version}/${platform}/updater/pkg`),
      signature: 'sig'
    },
    signatureFile: record(`${baseUrl}/${version}/${platform}/updater/pkg.sig`)
  };
}

function mirrorRecord(version, platform, url) {
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

// The public release origin plus the share pages, keyed by URL.
function publicOrigin(objects, shares) {
  return vi.fn(async (url) => {
    if (url in shares) {
      return new Response(
        shares[url] ? presentPage(shares[url]) : missingPage,
        { status: 200 }
      );
    }
    const object = objects[url];
    return object
      ? new Response(JSON.stringify(object), { status: 200 })
      : new Response('', { status: 404 });
  });
}

const uploaded = (version) => ({
  [`${baseUrl}/${version}/windows-x86_64/updater/platform-manifest.json`]:
    fragment(version, 'windows-x86_64'),
  [`${baseUrl}/${version}/darwin-aarch64/updater/platform-manifest.json`]:
    fragment(version, 'darwin-aarch64')
});
const recorded = (version) => ({
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
const shares = (version) => ({
  'https://mirror.example/win': `BazaarPlusPlus_${version}_x64-setup.exe`,
  'https://mirror.example/mac': `BazaarPlusPlus_${version}_aarch64.dmg`
});

test('before promotion the read-only check re-checks the recorded mirrors from the public origin', async () => {
  const fetchImpl = publicOrigin(
    { ...uploaded('5.5.0'), ...recorded('5.5.0') },
    shares('5.5.0')
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
      fetchImpl: publicOrigin(uploaded('5.5.0'), shares('5.5.0'))
    })
  ).rejects.toThrow(/has no mainland mirror recorded; run mirror first/);
});

test('the check rejects legacy fragments, records that do not match the installer, and shares that no longer serve it', async () => {
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
        shares('5.5.0')
      )
    })
  ).rejects.toThrow(/recorded for BazaarPlusPlus_5\.4\.0_x64-setup\.exe/);
  await expect(
    verifyMainlandMirrors({
      baseUrl,
      version: '5.5.0',
      fetchImpl: publicOrigin(
        { ...uploaded('5.5.0'), ...recorded('5.5.0') },
        { ...shares('5.5.0'), 'https://mirror.example/mac': null }
      )
    })
  ).rejects.toThrow(/Mainland mirror check failed[\s\S]*mirror\.example\/mac/);
});

function platformManifest(version, platform, mainlandUrl) {
  const installer =
    platform === 'windows-x86_64'
      ? windowsInstaller(version)
      : macInstaller(version);
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
        size: 100,
        sha256: 'b'.repeat(64),
        ...(mainlandUrl ? { mainlandUrl } : {})
      }
    }
  };
}

test('after promotion the check reads each published platform manifest, which may be at different versions', async () => {
  const published = {
    [`${baseUrl}/latest/windows-x86_64.json`]: platformManifest(
      '5.6.0',
      'windows-x86_64',
      'https://mirror.example/win'
    ),
    [`${baseUrl}/latest/darwin-aarch64.json`]: platformManifest(
      '5.5.0',
      'darwin-aarch64',
      'https://mirror.example/mac'
    )
  };
  const bothShares = {
    'https://mirror.example/win': 'BazaarPlusPlus_5.6.0_x64-setup.exe',
    'https://mirror.example/mac': 'BazaarPlusPlus_5.5.0_aarch64.dmg'
  };
  const verified = await verifyMainlandMirrors({
    baseUrl,
    latest: true,
    fetchImpl: publicOrigin(published, bothShares)
  });
  expect(verified.version).toBe('5.6.0 / 5.5.0');
  expect(verified.results.map((result) => result.fileName)).toEqual([
    'BazaarPlusPlus_5.6.0_x64-setup.exe',
    'BazaarPlusPlus_5.5.0_aarch64.dmg'
  ]);
  const one = await verifyMainlandMirrors({
    baseUrl,
    latest: true,
    platform: 'macos',
    fetchImpl: publicOrigin(published, bothShares)
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
  // A platform promoted without a mirror is reported as waived, and the
  // other platform is still checked.
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
        )
      },
      { 'https://mirror.example/win': 'BazaarPlusPlus_5.6.0_x64-setup.exe' }
    ),
    log
  });
  expect(waived.results.map((result) => result.outcome)).toEqual([
    'verified',
    'waived'
  ]);
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(/^darwin-aarch64: waived null \(promoted without/)
  );
});

test('a failed read-only check points at re-recording, not at the recording override flag', async () => {
  let message = '';
  try {
    await verifyMainlandMirrors({
      baseUrl,
      version: '5.5.0',
      fetchImpl: publicOrigin(
        { ...uploaded('5.5.0'), ...recorded('5.5.0') },
        { 'https://mirror.example/win': 'BazaarPlusPlus_5.5.0_x64-setup.exe' }
      )
    });
  } catch (error) {
    message = error.message;
  }
  expect(message).toContain(VERIFY_REMEDY);
  expect(message).not.toContain('--allow-unverified-mirror');
});

test('the pre-promotion check can be limited to one platform', async () => {
  const fetchImpl = publicOrigin(
    { ...uploaded('5.5.0'), ...recorded('5.5.0') },
    { 'https://mirror.example/win': 'BazaarPlusPlus_5.5.0_x64-setup.exe' }
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
