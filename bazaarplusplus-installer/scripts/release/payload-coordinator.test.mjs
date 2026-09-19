import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vitest';
import {
  preparePayload,
  payloadPaths,
  promotePayload,
  computePayloadInputs,
  verifyPayloadBuild,
  buildProduct,
  assertBuildOwner
} from '../../../release/payload.mjs';
import { preparePayloadZip } from './payload-zip.mjs';
import {
  readInventory,
  requiredPayloadPaths,
  synchronizePayloadProjection
} from '../../../release/payload-inventory.mjs';

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});
function write(file, bytes) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
}
function fixture() {
  const workspaceRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-payload-flow-')
  );
  roots.push(workspaceRoot);
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  const managedPath = path.join(workspaceRoot, 'game-managed');
  write(path.join(workspaceRoot, 'VERSION'), '5.5.0\n');
  write(
    path.join(workspaceRoot, 'release/payload.json'),
    JSON.stringify(readInventory())
  );
  synchronizePayloadProjection(workspaceRoot);
  write(
    path.join(workspaceRoot, 'bazaarplusplus-mod/src/Plugin.cs'),
    'class Plugin {}'
  );
  write(path.join(managedPath, 'Assembly-CSharp.dll'), 'game references');
  write(
    path.join(rootDir, 'src-tauri/history-database-compatibility.json'),
    JSON.stringify({ formatVersion: 1, supportedUserVersions: [2] })
  );
  write(
    path.join(rootDir, 'scripts/release/native-recorder-input.lock.json'),
    '{"test":"native receipt"}'
  );
  stage(rootDir, 'windows', 'old');
  write(
    path.join(payloadPaths(rootDir, 'windows').archive, 'old.txt'),
    'old archive'
  );
  write(
    path.join(payloadPaths(rootDir, 'macos').source, 'untouched'),
    'other platform'
  );
  return {
    workspaceRoot,
    rootDir,
    managedPath,
    platform: 'windows',
    resolveManaged: () => managedPath,
    ensureNative: () => {},
    verifyNative: () => {}
  };
}
function stage(rootDir, platform, contents) {
  const source = payloadPaths(rootDir, platform).source;
  for (const name of requiredPayloadPaths(platform))
    write(path.join(source, name), contents);
  write(
    path.join(source, 'BepInEx/plugins/BazaarPlusPlus.version'),
    '5.5.0.prod'
  );
  write(
    path.join(source, 'BepInEx/plugins/BazaarPlusPlus.history-database.json'),
    JSON.stringify({
      formatVersion: 1,
      historyDatabaseUserVersion: 2,
      historyRowSchemaVersion: 2
    })
  );
}

test('one preparation publishes a coherent Payload and does not touch another platform', () => {
  const data = fixture();
  const result = preparePayload({
    ...data,
    produce: ({ rootDir, platform }) => stage(rootDir, platform, 'new')
  });
  expect(result.productVersion).toBe('5.5.0');
  expect(result.inputDigest).toBe(computePayloadInputs(data).digest);
  expect(
    fs.readFileSync(
      path.join(
        payloadPaths(data.rootDir, 'windows').source,
        'BepInEx/plugins/BazaarPlusPlus.dll'
      ),
      'utf8'
    )
  ).toBe('new');
  expect(
    fs.existsSync(
      path.join(payloadPaths(data.rootDir, 'windows').archive, 'BepInEx.zip')
    )
  ).toBe(true);
  expect(
    fs.readFileSync(
      path.join(payloadPaths(data.rootDir, 'macos').source, 'untouched'),
      'utf8'
    )
  ).toBe('other platform');
});

test.each(['producer', 'validation', 'changed-input', 'native-verification'])(
  'a %s failure retains the previous Payload',
  (failure) => {
    const data = fixture();
    expect(() =>
      preparePayload({
        ...data,
        produce: ({ rootDir, platform }) => {
          stage(rootDir, platform, 'partial');
          if (failure === 'producer') throw new Error('producer failed');
          if (failure === 'changed-input')
            write(
              path.join(data.workspaceRoot, 'bazaarplusplus-mod/src/Plugin.cs'),
              'changed'
            );
        },
        ...(failure === 'validation'
          ? {
              validate: () => {
                throw new Error('validation failed');
              }
            }
          : {}),
        ...(failure === 'native-verification'
          ? {
              verifyNative: () => {
                throw new Error('native invalid');
              }
            }
          : {})
      })
    ).toThrow();
    expect(
      fs.readFileSync(
        path.join(
          payloadPaths(data.rootDir, 'windows').source,
          'BepInEx/plugins/BazaarPlusPlus.dll'
        ),
        'utf8'
      )
    ).toBe('old');
    expect(
      fs.readFileSync(
        path.join(payloadPaths(data.rootDir, 'windows').archive, 'old.txt'),
        'utf8'
      )
    ).toBe('old archive');
  }
);

test('promotion restores all old destinations when a rename fails partway through', () => {
  const data = fixture();
  fs.mkdirSync(path.join(data.rootDir, 'src-tauri/target'), {
    recursive: true
  });
  const stageRoot = fs.mkdtempSync(
    path.join(data.rootDir, 'src-tauri/target/payload-stage-')
  );
  const stagedInstaller = path.join(stageRoot, 'installer');
  stage(stagedInstaller, 'windows', 'new');
  write(
    path.join(payloadPaths(stagedInstaller, 'windows').archive, 'new.txt'),
    'new archive'
  );
  write(
    path.join(
      stagedInstaller,
      'scripts/release/native-recorder-input.lock.json'
    ),
    'new lock'
  );
  let calls = 0;
  expect(() =>
    promotePayload({
      ...data,
      stageRoot,
      stagedInstaller,
      rename: (...args) => {
        if (++calls === 4) throw new Error('rename failed');
        fs.renameSync(...args);
      }
    })
  ).toThrow(/rename failed/);
  expect(
    fs.readFileSync(
      path.join(
        payloadPaths(data.rootDir, 'windows').source,
        'BepInEx/plugins/BazaarPlusPlus.dll'
      ),
      'utf8'
    )
  ).toBe('old');
  expect(
    fs.readFileSync(
      path.join(payloadPaths(data.rootDir, 'windows').archive, 'old.txt'),
      'utf8'
    )
  ).toBe('old archive');
  expect(fs.existsSync(payloadPaths(data.rootDir, 'windows').journal)).toBe(
    false
  );
});

test('caller properties cannot bypass the product or staging owner', () => {
  const data = fixture();
  for (const override of [
    '-p:BppVersion=1.0.0',
    '-p:BppDeployToGame=true',
    '-p:BPPInstallerSourcePath=/tmp',
    '--property:RemoteEmbeddedDataPrepared=true',
    '-p:BuildProjectReferences=false',
    '-p:SkipCompilerExecution=true',
    '-p:DirectoryBuildPropsPath=/tmp/a',
    '-p:DefineConstants=CUSTOM',
    '-p:ManagedPath=/tmp;SkipCompilerExecution=true'
  ]) {
    expect(() =>
      preparePayload({ ...data, produce: () => {}, msbuildArgs: [override] })
    ).toThrow(/Unsupported release build override/);
  }
});

test('a prepared Payload is sealed against same-version replacement and ZIP regeneration', () => {
  const data = fixture();
  preparePayload({
    ...data,
    produce: ({ rootDir, platform }) => stage(rootDir, platform, 'new')
  });
  expect(verifyPayloadBuild(data).schemaVersion).toBe(2);
  write(
    path.join(
      payloadPaths(data.rootDir, data.platform).source,
      'BepInEx/plugins/BazaarPlusPlus.dll'
    ),
    'substituted'
  );
  preparePayloadZip({ ...data, productVersion: '5.5.0' });
  expect(() => verifyPayloadBuild(data)).toThrow(/output changed/);
});

test('source changes invalidate a previously prepared Payload', () => {
  const data = fixture();
  preparePayload({
    ...data,
    produce: ({ rootDir, platform }) => stage(rootDir, platform, 'new')
  });
  write(
    path.join(data.workspaceRoot, 'bazaarplusplus-mod/src/Plugin.cs'),
    'changed'
  );
  expect(() => verifyPayloadBuild(data)).toThrow(/inputs changed/);
});

test('the product lock spans prepare, packaging and artifact recording', () => {
  const data = fixture();
  const options = {
    ...data,
    produce: ({ rootDir, platform }) => stage(rootDir, platform, 'new')
  };
  let recorded = false;
  buildProduct({
    ...options,
    captureIdentity: () => ({ commit: 'a', dirty: false }),
    bundle: ({ token }) => {
      expect(() => assertBuildOwner(data.rootDir, token)).not.toThrow();
      expect(() => assertBuildOwner(data.rootDir, 'foreign')).toThrow();
      expect(() => preparePayload(options)).toThrow(/already running/);
      expect(() => buildProduct(options)).toThrow(/already running/);
    },
    writeArtifact: () => {
      recorded = true;
      expect(() => preparePayload(options)).toThrow(/already running/);
    }
  });
  expect(recorded).toBe(true);
  expect(
    fs.existsSync(path.join(data.rootDir, 'src-tauri/target/payload.lock'))
  ).toBe(false);
});

test.each(['source', 'git', 'packaging'])(
  'a %s change or failure during packaging cannot produce a release receipt',
  (failure) => {
    const data = fixture();
    let generation = 0;
    let recorded = false;
    expect(() =>
      buildProduct({
        ...data,
        produce: ({ rootDir, platform }) => stage(rootDir, platform, 'new'),
        captureIdentity: () => ({ commit: generation }),
        bundle: () => {
          if (failure === 'source')
            write(
              path.join(data.workspaceRoot, 'bazaarplusplus-mod/src/Plugin.cs'),
              'changed'
            );
          if (failure === 'git') generation++;
          if (failure === 'packaging') throw new Error('packaging failed');
        },
        writeArtifact: () => {
          recorded = true;
        }
      })
    ).toThrow();
    expect(recorded).toBe(false);
    expect(
      fs.existsSync(path.join(data.rootDir, 'src-tauri/target/payload.lock'))
    ).toBe(false);
  }
);

test('an interrupted promotion is a packaging gate and is recovered before the next prepare', () => {
  const data = fixture();
  const stageRoot = path.join(
    data.rootDir,
    'src-tauri/target/payload-stage-interrupted'
  );
  fs.mkdirSync(stageRoot, { recursive: true });
  const paths = payloadPaths(data.rootDir, data.platform);
  const destinations = [
    paths.source,
    paths.archive,
    path.join(data.rootDir, 'scripts/release/native-recorder-input.lock.json')
  ];
  const replacements = destinations.map((destination, index) => ({
    destination,
    backup: path.join(stageRoot, `backup-${index}`),
    existed: true
  }));
  fs.renameSync(paths.source, replacements[0].backup);
  stage(data.rootDir, data.platform, 'interrupted');
  write(
    paths.journal,
    JSON.stringify({
      schemaVersion: 1,
      stageRoot,
      platform: data.platform,
      replacements
    })
  );
  expect(() => verifyPayloadBuild(data)).toThrow(/Interrupted/);
  expect(() =>
    preparePayload({
      ...data,
      produce: () => {
        throw new Error('stop after recovery');
      }
    })
  ).toThrow(/stop after recovery/);
  expect(
    fs.readFileSync(
      path.join(paths.source, 'BepInEx/plugins/BazaarPlusPlus.dll'),
      'utf8'
    )
  ).toBe('old');
  expect(fs.existsSync(paths.journal)).toBe(false);
});
