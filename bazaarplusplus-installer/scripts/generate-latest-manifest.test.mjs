import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test, expect } from 'vitest';

import {
  buildLatestManifest,
  writeLatestManifest
} from './generate-latest-manifest.mjs';

function makeFragment(platform, version) {
  return {
    version,
    platform,
    url: `https://cdn.example.com/releases/${version}/${platform}/updater/asset.bin`,
    signature: `${platform}-signature`
  };
}

test('buildLatestManifest fills platforms and uses fallback notes/pub_date', () => {
  const fragments = [
    makeFragment('windows-x86_64', '3.2.1'),
    makeFragment('darwin-aarch64', '3.2.1')
  ];
  const now = new Date('2026-01-15T08:30:00.000Z');

  const latest = buildLatestManifest({
    version: '3.2.1',
    fragments,
    existingLatest: null,
    now
  });

  expect(latest).toEqual({
    version: '3.2.1',
    notes: 'Release 3.2.1',
    pub_date: '2026-01-15T08:30:00.000Z',
    platforms: {
      'windows-x86_64': {
        url: fragments[0].url,
        signature: fragments[0].signature
      },
      'darwin-aarch64': {
        url: fragments[1].url,
        signature: fragments[1].signature
      }
    }
  });
});

test('buildLatestManifest reuses notes and pub_date when version matches existing', () => {
  const latest = buildLatestManifest({
    version: '3.2.1',
    fragments: [makeFragment('windows-x86_64', '3.2.1')],
    existingLatest: {
      version: '3.2.1',
      notes: 'Hand-written release notes',
      pub_date: '2025-12-25T00:00:00.000Z'
    },
    now: new Date('2026-01-15T08:30:00.000Z')
  });

  expect(latest.notes).toBe('Hand-written release notes');
  expect(latest.pub_date).toBe('2025-12-25T00:00:00.000Z');
});

test('buildLatestManifest ignores existing notes from a different version', () => {
  const latest = buildLatestManifest({
    version: '3.2.1',
    fragments: [makeFragment('windows-x86_64', '3.2.1')],
    existingLatest: {
      version: '3.2.0',
      notes: 'Old notes',
      pub_date: '2025-11-01T00:00:00.000Z'
    },
    now: new Date('2026-01-15T08:30:00.000Z')
  });

  expect(latest.notes).toBe('Release 3.2.1');
  expect(latest.pub_date).toBe('2026-01-15T08:30:00.000Z');
});

test('buildLatestManifest throws when no fragments are present', () => {
  expect(() =>
    buildLatestManifest({
      version: '3.2.1',
      fragments: [],
      existingLatest: null
    })
  ).toThrow('No uploaded platform manifest fragments found for 3.2.1');
});

test('writeLatestManifest reads fragments from disk and reports missing platforms', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-latest-manifest-'));
  const winFragment = makeFragment('windows-x86_64', '3.2.1');
  fs.writeFileSync(
    path.join(root, 'windows-x86_64.json'),
    `${JSON.stringify(winFragment, null, 2)}\n`
  );
  const outputPath = path.join(root, 'latest.json');

  try {
    const result = writeLatestManifest({
      outputPath,
      version: '3.2.1',
      tempDir: root
    });

    expect(result.fragments.map((f) => f.platform)).toEqual(['windows-x86_64']);
    expect(result.missingPlatforms).toEqual(['darwin-aarch64']);

    const written = JSON.parse(fs.readFileSync(outputPath, 'utf8'));
    expect(written.platforms['windows-x86_64']).toEqual({
      url: winFragment.url,
      signature: winFragment.signature
    });
    expect(written.platforms['darwin-aarch64']).toBeUndefined();
  } finally {
    fs.rmSync(root, { force: true, recursive: true });
  }
});

test('writeLatestManifest skips fragments that mismatch the requested version', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-latest-manifest-'));
  const stale = makeFragment('windows-x86_64', '3.1.9');
  fs.writeFileSync(
    path.join(root, 'windows-x86_64.json'),
    `${JSON.stringify(stale, null, 2)}\n`
  );
  const fresh = makeFragment('darwin-aarch64', '3.2.1');
  fs.writeFileSync(
    path.join(root, 'darwin-aarch64.json'),
    `${JSON.stringify(fresh, null, 2)}\n`
  );
  const outputPath = path.join(root, 'latest.json');

  try {
    const result = writeLatestManifest({
      outputPath,
      version: '3.2.1',
      tempDir: root
    });

    expect(result.fragments.map((f) => f.platform)).toEqual(['darwin-aarch64']);
    expect(result.missingPlatforms).toEqual(['windows-x86_64']);
  } finally {
    fs.rmSync(root, { force: true, recursive: true });
  }
});
