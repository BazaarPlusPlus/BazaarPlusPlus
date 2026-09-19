#!/usr/bin/env node
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';
import { RELEASE_BASE_URL, WORKSPACE_ROOT } from './release/product.mjs';
import {
  synchronizeProductProjections,
  checkProductProjections
} from './release/projections.mjs';
import {
  preparePayload,
  buildProduct,
  assertBuildOwner,
  assertReleaseBuildArgs
} from './release/payload.mjs';
import { uploadPlatform, promoteRelease } from './release/publish.mjs';
import { r2StoreFromEnvironment } from './release/r2-store.mjs';
import { resolveBuildPlatform } from './release/release-platforms.mjs';

const usage = `Product release commands (run from any directory):
  node release.mjs sync                         Project VERSION into toolchain files
  node release.mjs check                        Check product source projections and release configuration
  node release.mjs prepare --platform macos      Build and validate one platform Payload
  node release.mjs build --platform macos        Build and sign the native installer
  node release.mjs upload --platform macos       Upload immutable platform artifacts
  node release.mjs promote                      Publish latest after both platforms are ready

Use windows on the Windows build host. Prepare/build accept -- -p:ManagedPath=<path>.
Only upload/promote access R2, and require BPP_R2_ACCOUNT_ID,
BPP_R2_ACCESS_KEY_ID and BPP_R2_SECRET_ACCESS_KEY. No command changes VERSION.
`;

export function parseReleaseArgs(args) {
  const [command, ...rest] = args;
  if (!command || ['--help', '-h'].includes(command)) {
    if (rest.length) throw new Error('Help takes no additional arguments');
    return { command: 'help' };
  }
  if (
    ![
      'sync',
      'check',
      'prepare',
      'build',
      'upload',
      'promote',
      'assert-build-owner'
    ].includes(command)
  )
    throw new Error(usage);
  const { values, positionals, tokens } = parseArgs({
    args: rest,
    strict: true,
    allowPositionals: true,
    tokens: true,
    options: { platform: { type: 'string' } }
  });
  if (tokens.filter((token) => token.kind === 'option').length > 1)
    throw new Error('--platform may only be specified once');
  const separator = tokens.find((token) => token.kind === 'option-terminator');
  if (
    tokens.some(
      (token) =>
        token.kind === 'positional' &&
        (!separator || token.index < separator.index)
    )
  )
    throw new Error('MSBuild properties must follow --');
  const platform = values.platform;
  if (['prepare', 'build', 'upload'].includes(command)) {
    if (!['macos', 'windows'].includes(platform))
      throw new Error('--platform must be macos or windows');
  } else if (platform !== undefined) {
    throw new Error(`${command} is a product-wide operation`);
  }
  if (!['prepare', 'build'].includes(command) && positionals.length)
    throw new Error('MSBuild properties are only valid for prepare/build');
  assertReleaseBuildArgs(positionals);
  if (command === 'assert-build-owner' && rest.length)
    throw new Error('assert-build-owner takes no arguments');
  return { command, platform, msbuildArgs: positionals };
}

function bundleInstaller(rootDir, token) {
  execFileSync('bash', [path.join(rootDir, 'scripts/bundle.sh')], {
    cwd: rootDir,
    stdio: 'inherit',
    env: { ...process.env, BPP_RELEASE_LOCK_TOKEN: token }
  });
}

export async function main(
  args,
  {
    workspaceRoot = WORKSPACE_ROOT,
    hostPlatform = process.platform,
    prepare = preparePayload,
    build = buildProduct,
    createStore = r2StoreFromEnvironment,
    upload = uploadPlatform,
    promote = promoteRelease,
    bundle = bundleInstaller,
    log = console.log
  } = {}
) {
  const { command, platform, msbuildArgs } = parseReleaseArgs(args);
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  if (command === 'help') {
    log(usage);
    return;
  }
  // Internal protocol used by installer packaging while the coordinator holds
  // the shared Payload lock; it must not start another release operation.
  if (command === 'assert-build-owner') {
    assertBuildOwner(rootDir);
    return;
  }
  if (command === 'build' && platform !== resolveBuildPlatform(hostPlatform))
    throw new Error(`Build ${platform} on its native host`);
  if (command === 'sync') {
    const version = synchronizeProductProjections(workspaceRoot);
    log(`Product projections synchronized to ${version}`);
    return;
  }
  const version = checkProductProjections(workspaceRoot);
  if (command === 'check') {
    log(
      `Product ${version}: version, inventory, badges and release configuration aligned`
    );
    return;
  }
  if (command === 'prepare') {
    prepare({ workspaceRoot, platform, msbuildArgs });
    log(`Prepared ${platform} Payload for ${version}`);
    return;
  }
  if (command === 'build') {
    build({
      workspaceRoot,
      platform,
      msbuildArgs,
      bundle: ({ token }) => bundle(rootDir, token)
    });
    return;
  }
  const store = createStore();
  if (command === 'upload') {
    await upload({ workspaceRoot, platform, baseUrl: RELEASE_BASE_URL, store });
    log(
      `Uploaded ${platform} ${version}. Run promote after both platforms are uploaded.`
    );
  } else {
    await promote({
      version,
      baseUrl: RELEASE_BASE_URL,
      store
    });
    log(`Published Product Release ${version}`);
  }
}

if (import.meta.main)
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
