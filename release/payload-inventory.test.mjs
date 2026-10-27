import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { expect, test } from 'vitest';
import {
  assertShippedPayloadPaths,
  platformInventory,
  readInventory,
  requiredPayloadPaths,
  validateInventory
} from './payload-inventory.mjs';
import { readProductVersion, WORKSPACE_ROOT } from './product.mjs';

test('inventory distinguishes shipped, runtime, retired, platform and directory ownership', () => {
  expect(() =>
    assertShippedPayloadPaths(
      [
        'BepInEx/core/BepInEx.dll',
        'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox'
      ],
      'macos'
    )
  ).not.toThrow();
  for (const file of [
    'BepInEx/config/BazaarPlusPlus.cfg',
    'BepInEx/plugins/ffmpeg',
    'BepInEx/plugins/Unknown.dll',
    'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle-foreign/file'
  ]) {
    expect(() => assertShippedPayloadPaths([file], 'macos')).toThrow(
      /Undeclared or forbidden/
    );
  }
  expect(() =>
    assertShippedPayloadPaths(
      ['BepInEx/plugins/libBppMacAudio.dylib'],
      'windows'
    )
  ).toThrow();
});

test('each platform installs only its own host entries', () => {
  const hosts = (platform) =>
    new Set(platformInventory(platform).map((entry) => entry.host));
  expect(hosts('macos')).toEqual(new Set(['bepinex']));
  expect(hosts('windows')).toEqual(new Set(['bepinex']));
  expect(hosts('android')).toEqual(new Set(['melonloader']));
  expect(() =>
    assertShippedPayloadPaths(
      [
        'MelonLoader/Mods/BazaarPlusPlus.Android.dll',
        'MelonLoader/UserLibs/MessagePack.dll',
        'lib/arm64-v8a/libe_sqlite3.so',
        'assets/LemonLoader/deployment/Mods/BazaarPlusPlus.Android.dll'
      ],
      'android'
    )
  ).not.toThrow();
  for (const [file, platform] of [
    ['BepInEx/plugins/BazaarPlusPlus.dll', 'android'],
    ['MelonLoader/Mods/BazaarPlusPlus.Android.dll', 'macos'],
    ['MelonLoader/UserData/BazaarPlusPlus.cfg', 'android'],
    ['MelonLoader/Latest.log', 'android']
  ])
    expect(() => assertShippedPayloadPaths([file], platform)).toThrow(
      /Undeclared or forbidden/
    );
  expect(requiredPayloadPaths('android')).toContain(
    'MelonLoader/Mods/BazaarPlusPlus.history-database.json'
  );
});

test.each([
  '/absolute',
  '../outside',
  'BepInEx/../other',
  'C:\\game',
  'BepInEx',
  'BazaarPlusPlusV6'
])('inventory rejects unsafe ownership %s', (name) => {
  expect(() =>
    validateInventory({
      schemaVersion: 1,
      files: [
        {
          path: name,
          host: 'bepinex',
          ownership: 'private',
          producer: 'managed',
          kind: 'directory'
        }
      ]
    })
  ).toThrow();
});

test.each([
  'BazaarPlusPlusV6/data',
  'BazaarPlusPlusV4/data',
  'BundleOutbox/queue',
  'Foreign/data'
])(
  'user data and unrelated roots cannot become installer-owned: %s',
  (name) => {
    expect(() =>
      validateInventory({
        schemaVersion: 1,
        files: [
          {
            path: name,
            host: 'bepinex',
            ownership: 'private',
            producer: 'managed'
          }
        ]
      })
    ).toThrow(/approved install roots/);
  }
);

test.each([
  [
    'a BepInEx path on the MelonLoader host',
    'melonloader',
    'BepInEx/plugins/A.dll',
    'private',
    'managed'
  ],
  [
    'a MelonLoader path on the BepInEx host',
    'bepinex',
    'MelonLoader/Mods/A.dll',
    'private',
    'managed'
  ],
  [
    'an APK path the mod build claims',
    'melonloader',
    'lib/arm64-v8a/libe_sqlite3.so',
    'dependency',
    'managed'
  ],
  [
    'an injected file outside the APK',
    'melonloader',
    'MelonLoader/Mods/A.dll',
    'private',
    'apk-injection'
  ],
  [
    'injection on the BepInEx host',
    'bepinex',
    'BepInEx/plugins/A.dll',
    'private',
    'apk-injection'
  ],
  [
    'a foreign MelonLoader user data file',
    'melonloader',
    'MelonLoader/UserData/Other.cfg',
    'private',
    'runtime'
  ],
  [
    'an APK library BPP does not own outright',
    'melonloader',
    'lib/arm64-v8a/libil2cpp.so',
    'private',
    'apk-injection'
  ]
])(
  '%s is outside the approved install roots',
  (_, host, name, ownership, producer) => {
    expect(() =>
      validateInventory({
        schemaVersion: 1,
        files: [{ path: name, host, ownership, producer }]
      })
    ).toThrow(/approved install roots/);
  }
);

test.each([
  ['an unknown host', { host: 'mono' }],
  ['a missing host', { host: undefined }],
  ['a platform of another host', { platforms: ['android'] }],
  ['another schema version', null]
])('inventory rejects %s', (_, change) => {
  const inventory = structuredClone(readInventory());
  if (change) Object.assign(inventory.files[0], change);
  else inventory.schemaVersion = 2;
  expect(() => validateInventory(inventory)).toThrow();
});

test.each([
  ['the BepInEx host by default', [], 'macos'],
  [
    'the MelonLoader host for Android',
    ['-p:BppPayloadHost=melonloader'],
    'android'
  ]
])(
  'MSBuild collects %s from the generated projection',
  (_, properties, platform) => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-payload-host-'));
    try {
      const gate = path.join(root, 'Gate.proj');
      fs.writeFileSync(
        gate,
        `<Project><PropertyGroup><TargetDir>${xml(root)}/</TargetDir><HistoryDatabaseContractFile>history.json</HistoryDatabaseContractFile><FixedMacSqliteNativeFile>mac.dylib</FixedMacSqliteNativeFile><FixedWindowsSqliteNativeFile>win.dll</FixedWindowsSqliteNativeFile><IsMacHost>true</IsMacHost></PropertyGroup><Import Project="${xml(path.join(WORKSPACE_ROOT, 'release/generated/Payload.targets'))}" /></Project>`
      );
      const evaluated = JSON.parse(
        execFileSync(
          'dotnet',
          [
            'msbuild',
            gate,
            '-t:CollectPayloadFiles',
            '-getItem:BppPayloadFile',
            '-getProperty:BppPayloadPlatform',
            '-nologo',
            ...properties
          ],
          { encoding: 'utf8', stdio: 'pipe' }
        )
      );
      expect(evaluated.Properties.BppPayloadPlatform).toBe(platform);
      const collected = evaluated.Items.BppPayloadFile.map(
        (item) => item.PayloadPath
      ).sort();
      const expected = platformInventory(platform)
        .filter(
          (entry) =>
            !entry.optional &&
            [
              'managed',
              'history-contract',
              'sqlite-macos',
              'sqlite-windows'
            ].includes(entry.producer)
        )
        .map((entry) => entry.path)
        .sort();
      expect(collected).toEqual(expected);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  },
  30000
);

test('directory and descendant ownership cannot overlap', () => {
  const inventory = structuredClone(readInventory());
  inventory.files.push({
    path: 'BepInEx/core/Foreign.dll',
    host: 'bepinex',
    ownership: 'private',
    producer: 'managed'
  });
  expect(() => validateInventory(inventory)).toThrow(/Overlapping/);
});

test('MSBuild evaluates the same product version as the release coordinator', () => {
  const version = execFileSync(
    'dotnet',
    [
      'msbuild',
      'src/BazaarPlusPlus/BazaarPlusPlus.csproj',
      '-getProperty:BppVersion'
    ],
    { cwd: path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod'), encoding: 'utf8' }
  ).trim();
  expect(version).toBe(readProductVersion());
}, 30000);

// The Android Host builds but is not shipped yet: the BepInEx-shaped inventory has
// no root for it until the Payload Inventory gains per-host roots.
const UNSHIPPED_PROJECTS = new Set(['BazaarPlusPlus.Android']);

test('every published product assembly is declared in the shared inventory', () => {
  const assemblies = (host) =>
    readInventory()
      .files.filter((entry) => entry.assembly && entry.host === host)
      .map((entry) => path.posix.basename(entry.path, '.dll'))
      .sort();
  const projects = fs
    .readdirSync(path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod/src'), {
      withFileTypes: true
    })
    .filter(
      (entry) =>
        entry.isDirectory() &&
        fs.existsSync(
          path.join(
            WORKSPACE_ROOT,
            'bazaarplusplus-mod/src',
            entry.name,
            `${entry.name}.csproj`
          )
        )
    )
    .map((entry) => entry.name)
    .filter((name) => !UNSHIPPED_PROJECTS.has(name))
    .sort();
  // The Updater ships beside the host assemblies with its own version, so it
  // is declared by its producer, not as a product assembly.
  const updaters = readInventory()
    .files.filter((entry) => entry.producer === 'updater')
    .map((entry) => path.posix.basename(entry.path, '.dll'));
  const declared = new Set([
    ...assemblies('bepinex'),
    ...assemblies('melonloader'),
    ...updaters
  ]);
  expect(projects.filter((project) => !declared.has(project))).toEqual([]);
  expect(
    assemblies('bepinex').filter((name) => !projects.includes(name))
  ).toEqual([]);
  // The Android host and Core projects land with #352 and #354; until then
  // the MelonLoader entries name the layout they will build into.
  expect(
    assemblies('melonloader').filter((name) => !projects.includes(name))
  ).toEqual(
    ['BazaarPlusPlus.Android', 'BazaarPlusPlus.Core'].filter(
      (name) => !projects.includes(name)
    )
  );
});

const xml = (value) => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');

test('the assembly gate reads compiled metadata and rejects a mixed-version Payload', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-assembly-gate-'));
  const version = readProductVersion();
  try {
    const project = path.join(root, 'Probe.csproj');
    fs.writeFileSync(
      project,
      '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Probe</AssemblyName></PropertyGroup></Project>'
    );
    const compile = (value) =>
      execFileSync(
        'dotnet',
        ['build', project, `-p:Version=${value}`, '--nologo', '-v:q'],
        { stdio: 'pipe' }
      );
    compile(version);
    const source = path.join(root, 'bin/Debug/net10.0/Probe.dll');
    const staged = path.join(root, 'payload');
    fs.mkdirSync(staged);
    const assemblies = readInventory().files.filter((entry) => entry.assembly);
    for (const entry of assemblies)
      fs.copyFileSync(
        source,
        path.join(staged, path.posix.basename(entry.path))
      );
    const gate = path.join(root, 'Gate.proj');
    fs.writeFileSync(
      gate,
      `<Project><PropertyGroup><BppVersion>${version}</BppVersion><TargetDir>${xml(staged)}/</TargetDir></PropertyGroup><Import Project="${xml(path.join(WORKSPACE_ROOT, 'release/generated/Payload.targets'))}" /></Project>`
    );
    const check = () => {
      try {
        return execFileSync(
          'dotnet',
          ['msbuild', gate, '-t:ValidatePayloadAssemblyVersions', '-nologo'],
          { encoding: 'utf8', stdio: 'pipe' }
        );
      } catch (error) {
        throw new Error(error.stdout || error.message, { cause: error });
      }
    };
    expect(check).not.toThrow();
    compile('0.0.1');
    fs.copyFileSync(
      source,
      path.join(staged, path.posix.basename(assemblies[0].path))
    );
    expect(check).toThrow(/expected/);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}, 60000);
