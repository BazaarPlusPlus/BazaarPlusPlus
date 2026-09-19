#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { WORKSPACE_ROOT, readProductVersion } from './release/product.mjs';
import { synchronizePayloadProjection } from './release/payload-inventory.mjs';
import {
  preparePayload,
  buildProduct,
  assertBuildOwner
} from './release/payload.mjs';
import { uploadPlatform, promoteRelease } from './release/publish.mjs';
import { r2StoreFromEnvironment } from './release/r2-store.mjs';
import {
  collectVersionSnapshot,
  assertVersionsAreAligned,
  synchronizeVersions
} from './bazaarplusplus-installer/scripts/release/version-sync.mjs';

const usage = `Product release commands (run from any directory):
  node release.mjs sync                         Project VERSION into toolchain files
  node release.mjs check                        Check product version and shared inventory
  node release.mjs prepare --platform macos      Build and validate one platform Payload
  node release.mjs build --platform macos        Build and sign the native installer
  node release.mjs upload --platform macos       Upload immutable platform artifacts
  node release.mjs promote                      Publish latest after both platforms are ready

Use windows on the Windows build host. Prepare/build accept -- -p:ManagedPath=<path>.
Only upload/promote access R2, and require BPP_R2_ACCOUNT_ID,
BPP_R2_ACCESS_KEY_ID and BPP_R2_SECRET_ACCESS_KEY. No command changes VERSION.
`;

export async function main(args) {
  const [command, ...rest] = args;
  if (command === 'assert-build-owner' && rest.length === 0) {
    assertBuildOwner(path.join(WORKSPACE_ROOT, 'bazaarplusplus-installer'));
    return;
  }
  if (!command || ['--help', '-h'].includes(command)) {
    console.log(usage);
    return;
  }
  if (
    !['sync', 'check', 'prepare', 'build', 'upload', 'promote'].includes(
      command
    )
  )
    throw new Error(usage);
  let platform;
  let msbuildArgs = [];
  for (let i = 0; i < rest.length; i++) {
    if (rest[i] === '--') {
      msbuildArgs = rest.slice(i + 1);
      break;
    }
    if (rest[i] === '--platform' && !platform) {
      platform = rest[++i];
      continue;
    }
    throw new Error(`Unknown release option: ${rest[i]}`);
  }
  const rootDir = path.join(WORKSPACE_ROOT, 'bazaarplusplus-installer');
  if (
    ['prepare', 'build', 'upload'].includes(command) &&
    !['macos', 'windows'].includes(platform)
  )
    throw new Error('--platform must be macos or windows');
  if (!['prepare', 'build'].includes(command) && msbuildArgs.length)
    throw new Error('MSBuild properties are only valid for prepare/build');
  if (['sync', 'check', 'promote'].includes(command) && platform)
    throw new Error(`${command} is a product-wide operation`);
  if (command === 'sync') {
    synchronizeVersions(rootDir);
    synchronizePayloadProjection();
    const version = readProductVersion();
    for (const name of ['README.md', 'README_en.md']) {
      const file = path.join(WORKSPACE_ROOT, name);
      const before = fs.readFileSync(file, 'utf8');
      fs.writeFileSync(
        file,
        before.replace(
          /img\.shields\.io\/badge\/version-[^-]+-/,
          `img.shields.io/badge/version-${version}-`
        )
      );
    }
    console.log(`Product projections synchronized to ${version}`);
    return;
  }
  assertVersionsAreAligned(collectVersionSnapshot(rootDir));
  synchronizePayloadProjection(WORKSPACE_ROOT, { check: true });
  if (command === 'check') {
    console.log(
      `Product ${readProductVersion()}: version and inventory aligned`
    );
    return;
  }
  if (command === 'prepare') {
    preparePayload({ workspaceRoot: WORKSPACE_ROOT, platform, msbuildArgs });
    console.log(`Prepared ${platform} Payload for ${readProductVersion()}`);
    return;
  }
  if (command === 'build') {
    const host =
      process.platform === 'darwin'
        ? 'macos'
        : process.platform === 'win32'
          ? 'windows'
          : null;
    if (platform !== host)
      throw new Error(`Build ${platform} on its native host`);
    buildProduct({
      workspaceRoot: WORKSPACE_ROOT,
      platform,
      msbuildArgs,
      bundle: ({ token }) => {
        execFileSync('bash', [path.join(rootDir, 'scripts/bundle.sh')], {
          cwd: rootDir,
          stdio: 'inherit',
          env: { ...process.env, BPP_RELEASE_LOCK_TOKEN: token }
        });
      }
    });
    return;
  }
  const config = JSON.parse(
    fs.readFileSync(path.join(rootDir, 'src-tauri/tauri.conf.json'), 'utf8')
  );
  const baseUrl = config.plugins.updater.endpoints[0].replace(
    /\/latest\.json$/,
    ''
  );
  const store = r2StoreFromEnvironment();
  if (command === 'upload') {
    await uploadPlatform({
      workspaceRoot: WORKSPACE_ROOT,
      platform,
      baseUrl,
      store
    });
    console.log(
      `Uploaded ${platform} ${readProductVersion()}. Run promote after both platforms are uploaded.`
    );
  } else {
    await promoteRelease({ version: readProductVersion(), baseUrl, store });
    console.log(`Published Product Release ${readProductVersion()}`);
  }
}

if (import.meta.main)
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
