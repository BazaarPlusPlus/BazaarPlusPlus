import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test, expect } from 'vitest';

import {
  buildPlatformFragment,
  writePlatformManifest
} from './generate-platform-manifest.mjs';

test('buildPlatformFragment composes the updater URL and trims signature', () => {
  const fragment = buildPlatformFragment({
    platformKey: 'darwin-aarch64',
    baseUrl: 'https://cdn.example.com/releases',
    version: '3.2.1',
    updaterFile: '/build/dist/BazaarPlusPlus_3.2.1_aarch64.app.tar.gz',
    updaterSignature: '  signature-bytes\n'
  });

  expect(fragment).toEqual({
    version: '3.2.1',
    platform: 'darwin-aarch64',
    url: 'https://cdn.example.com/releases/3.2.1/darwin-aarch64/updater/BazaarPlusPlus_3.2.1_aarch64.app.tar.gz',
    signature: 'signature-bytes'
  });
});

test('writePlatformManifest writes a JSON fragment with trailing newline', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-platform-manifest-'));
  const sigPath = path.join(root, 'updater.sig');
  fs.writeFileSync(sigPath, 'sig-data\n');

  const outputPath = path.join(root, 'platform.json');

  try {
    writePlatformManifest({
      outputPath,
      platformKey: 'windows-x86_64',
      baseUrl: 'https://cdn.example.com/releases',
      version: '3.2.1',
      updaterFile: '/build/dist/BazaarPlusPlus_3.2.1_x64-setup.exe',
      updaterSig: sigPath
    });

    const raw = fs.readFileSync(outputPath, 'utf8');
    expect(raw.endsWith('\n')).toBe(true);

    const parsed = JSON.parse(raw);
    expect(parsed).toEqual({
      version: '3.2.1',
      platform: 'windows-x86_64',
      url: 'https://cdn.example.com/releases/3.2.1/windows-x86_64/updater/BazaarPlusPlus_3.2.1_x64-setup.exe',
      signature: 'sig-data'
    });
  } finally {
    fs.rmSync(root, { force: true, recursive: true });
  }
});
