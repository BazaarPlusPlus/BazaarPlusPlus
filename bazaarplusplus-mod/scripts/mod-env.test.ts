import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'vite-plus/test';
import type { TestContext } from 'vite-plus/test';
import {
  collectProps,
  desktopDataRoot,
  locateAppmanifest,
  parseAcfBuildId,
  parseAcfSteamBranch,
  propValue
} from './mod-env.ts';

function tempDir(t: TestContext): string {
  const dir = fs.realpathSync(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-mod-env-'))
  );
  t.onTestFinished(() => fs.rmSync(dir, { recursive: true, force: true }));
  return dir;
}

test('collectProps accepts -p: and --property: and rejects positional args', () => {
  assert.deepEqual(collectProps(['-p:Foo=1', '--property:Bar=baz=qux']), [
    '-p:Foo=1',
    '--property:Bar=baz=qux'
  ]);
  assert.throws(() => collectProps(['--fast']), /Expected -p:Name=Value/);
});

test('propValue returns the last value for a named MSBuild property', () => {
  const args = [
    '-p:ManagedPath=/first',
    '--property:ManagedPath=/second=with=equals',
    '-p:Other=x'
  ];
  assert.equal(propValue('ManagedPath', args), '/second=with=equals');
  assert.equal(propValue('Missing', args), '');
});

test('desktopDataRoot resolves stable parent layout for macOS and Windows Tempo builds', (t) => {
  const root = tempDir(t);
  const macBuild = path.join(root, 'game', 'buildarm64');
  fs.mkdirSync(path.join(macBuild, 'TheBazaar_ARM64.app'), { recursive: true });
  assert.equal(
    desktopDataRoot(macBuild, 'darwin'),
    path.join(root, 'game', 'BazaarPlusPlusV6')
  );

  // When only the legacy data dir exists inside buildarm64, stay on it.
  fs.mkdirSync(path.join(macBuild, 'BazaarPlusPlusV6'), { recursive: true });
  assert.equal(
    desktopDataRoot(macBuild, 'darwin'),
    path.join(macBuild, 'BazaarPlusPlusV6')
  );

  const winBuild = path.join(root, 'Game', 'buildx64');
  fs.mkdirSync(winBuild, { recursive: true });
  assert.equal(
    desktopDataRoot(winBuild, 'win32'),
    path.join(root, 'Game', 'BazaarPlusPlusV6')
  );
});

test('locateAppmanifest and ACF parsers read buildid and BetaKey', (t) => {
  const root = tempDir(t);
  const steamapps = path.join(root, 'steamapps');
  const managed = path.join(
    steamapps,
    'common',
    'The Bazaar',
    'TheBazaar_Data',
    'Managed'
  );
  fs.mkdirSync(managed, { recursive: true });
  const acfPath = path.join(steamapps, 'appmanifest_1617400.acf');
  const acf = `"AppState"\n{\n\t"appid"\t\t"1617400"\n\t"buildid"\t\t"22456789"\n\t"MountedConfig"\n\t{\n\t\t"BetaKey"\t\t"staging"\n\t}\n}\n`;
  fs.writeFileSync(acfPath, acf);

  assert.equal(locateAppmanifest(managed), acfPath);
  assert.equal(parseAcfBuildId(acf), '22456789');
  assert.equal(parseAcfSteamBranch(acf), 'staging');
  assert.equal(
    parseAcfSteamBranch(`"AppState"\n{\n\t"MountedConfig"\n\t{\n\t}\n}\n`),
    'public'
  );
});
