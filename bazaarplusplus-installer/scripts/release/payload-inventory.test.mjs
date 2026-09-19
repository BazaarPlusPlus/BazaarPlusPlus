import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { expect, test } from 'vitest';
import {
  assertShippedPayloadPaths,
  readInventory,
  validateInventory,
  synchronizePayloadProjection
} from '../../../release/payload-inventory.mjs';
import {
  readProductVersion,
  WORKSPACE_ROOT
} from '../../../release/product.mjs';

test('the checked-in MSBuild projection matches the shared Payload Inventory', () => {
  expect(() =>
    synchronizePayloadProjection(WORKSPACE_ROOT, { check: true })
  ).not.toThrow();
});

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

test.each([
  '/absolute',
  '../outside',
  'BepInEx/../other',
  'C:\\game',
  'BepInEx',
  'BazaarPlusPlusV5'
])('inventory rejects unsafe ownership %s', (name) => {
  expect(() =>
    validateInventory({
      schemaVersion: 1,
      files: [
        {
          path: name,
          ownership: 'private',
          producer: 'managed',
          kind: 'directory'
        }
      ]
    })
  ).toThrow();
});

test.each([
  'BazaarPlusPlusV5/data',
  'BazaarPlusPlusV4/data',
  'BundleOutbox/queue',
  'Foreign/data'
])(
  'user data and unrelated roots cannot become installer-owned: %s',
  (name) => {
    expect(() =>
      validateInventory({
        schemaVersion: 1,
        files: [{ path: name, ownership: 'private', producer: 'managed' }]
      })
    ).toThrow(/approved install roots/);
  }
);

test('directory and descendant ownership cannot overlap', () => {
  const inventory = structuredClone(readInventory());
  inventory.files.push({
    path: 'BepInEx/core/Foreign.dll',
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

test('every published product assembly is declared in the shared inventory', () => {
  const assemblies = readInventory()
    .files.filter((entry) => entry.assembly)
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
    .sort();
  expect(assemblies).toEqual(projects);
});

test('the assembly gate reads compiled metadata and rejects a mixed-version Payload', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-assembly-gate-'));
  const version = readProductVersion();
  const xml = (value) =>
    value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
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
        throw new Error(error.stdout || error.message);
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
