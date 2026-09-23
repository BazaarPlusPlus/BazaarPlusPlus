#!/usr/bin/env node
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';
import {
  RELEASE_BASE_URL,
  WORKSPACE_ROOT,
  readProductVersion
} from './release/product.mjs';
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
import {
  uploadPlatform,
  recordMainlandMirror,
  promotePlatform,
  promoteRelease
} from './release/publish.mjs';
import { fetchMirrorPage, verifyMainlandMirrors } from './release/mirror.mjs';
import { assertMirrorUrl } from './release/manifest.mjs';
import { r2StoreFromEnvironment } from './release/r2-store.mjs';
import { resolveBuildPlatform } from './release/release-platforms.mjs';

const usage = `Product release commands (run from any directory):
  node release.mjs sync                         Project VERSION into toolchain files
  node release.mjs check                        Check product source projections and release configuration
  node release.mjs prepare --platform macos      Build and validate one platform Payload
  node release.mjs build --platform macos        Build and sign the native installer
  node release.mjs upload --platform macos       Upload immutable platform artifacts
  node release.mjs mirror --platform macos --url <share-url>
                                                Verify and record the platform's mainland mirror page
  node release.mjs verify-mirror [--platform macos]
                                                Re-check the recorded mainland mirrors of VERSION
  node release.mjs verify-mirror --latest       Re-check the mainland mirrors of the published platform manifests
  node release.mjs promote                      Publish every platform at VERSION and advance latest.json
  node release.mjs promote --platform macos     Publish one platform's manifest; latest.json advances once
                                                every platform is at the same version

Use windows on the Windows build host. Prepare/build accept -- -p:ManagedPath=<path>.
mirror refuses a share page that does not serve the uploaded installer unless
--allow-unverified-mirror is passed; promote refuses a platform without a
recorded mirror unless --without-mainland-mirror is passed. Only upload, mirror
and promote access R2, and require BPP_R2_ACCOUNT_ID, BPP_R2_ACCESS_KEY_ID and
BPP_R2_SECRET_ACCESS_KEY. No command changes VERSION.
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
      'mirror',
      'verify-mirror',
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
    options: {
      platform: { type: 'string' },
      url: { type: 'string' },
      latest: { type: 'boolean' },
      'allow-unverified-mirror': { type: 'boolean' },
      'without-mainland-mirror': { type: 'boolean' }
    }
  });
  const seen = new Set();
  for (const token of tokens) {
    if (token.kind !== 'option') continue;
    if (seen.has(token.name))
      throw new Error(`--${token.name} may only be specified once`);
    seen.add(token.name);
  }
  const separator = tokens.find((token) => token.kind === 'option-terminator');
  if (
    !['prepare', 'build'].includes(command) &&
    (separator || positionals.length)
  )
    throw new Error(
      `${command} takes no positional arguments; platforms are passed as --platform <macos|windows>`
    );
  if (
    tokens.some(
      (token) =>
        token.kind === 'positional' &&
        (!separator || token.index < separator.index)
    )
  )
    throw new Error('MSBuild properties must follow --');
  const platform = values.platform;
  if (['prepare', 'build', 'upload', 'mirror'].includes(command)) {
    if (!['macos', 'windows'].includes(platform))
      throw new Error('--platform must be macos or windows');
  } else if (['promote', 'verify-mirror'].includes(command)) {
    if (platform !== undefined && !['macos', 'windows'].includes(platform))
      throw new Error('--platform must be macos or windows');
  } else if (platform !== undefined) {
    throw new Error(`${command} is a product-wide operation`);
  }
  if (command === 'mirror') {
    if (!values.url) throw new Error('mirror requires --url <share-url>');
    assertMirrorUrl(values.url);
  } else if (values.url !== undefined) {
    throw new Error('--url is only valid for mirror');
  }
  if (values.latest && command !== 'verify-mirror')
    throw new Error('--latest is only valid for verify-mirror');
  if (values['allow-unverified-mirror'] && command !== 'mirror')
    throw new Error('--allow-unverified-mirror is only valid for mirror');
  if (values['without-mainland-mirror'] && command !== 'promote')
    throw new Error('--without-mainland-mirror is only valid for promote');
  assertReleaseBuildArgs(positionals);
  if (command === 'assert-build-owner' && rest.length)
    throw new Error('assert-build-owner takes no arguments');
  return {
    command,
    platform,
    url: values.url,
    msbuildArgs: positionals,
    latest: values.latest ?? false,
    allowUnverifiedMirror: values['allow-unverified-mirror'] ?? false,
    withoutMainlandMirror: values['without-mainland-mirror'] ?? false
  };
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
    mirror = recordMainlandMirror,
    verifyMirror = verifyMainlandMirrors,
    probeMirror = fetchMirrorPage,
    promote = promoteRelease,
    promoteOne = promotePlatform,
    bundle = bundleInstaller,
    log = console.log
  } = {}
) {
  const {
    command,
    platform,
    url,
    msbuildArgs,
    latest,
    allowUnverifiedMirror,
    withoutMainlandMirror
  } = parseReleaseArgs(args);
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
  // Read-only and credential-free: it checks the public release origin and
  // the mirror, so it must not depend on source alignment or R2 access.
  if (command === 'verify-mirror') {
    const verified = await verifyMirror({
      baseUrl: RELEASE_BASE_URL,
      version: latest ? null : readProductVersion(workspaceRoot),
      platform,
      latest,
      log
    });
    log(`Mainland mirror verified for ${verified.version}`);
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
      `Uploaded ${platform} ${version}. Record its mainland mirror, then promote after both platforms are ready.`
    );
  } else if (command === 'mirror') {
    await mirror({
      version,
      platform,
      url,
      store,
      probeMirror,
      allowUnverified: allowUnverifiedMirror,
      log
    });
    log(`Recorded ${platform} mainland mirror for ${version}`);
  } else if (platform) {
    const result = await promoteOne({
      version,
      platform,
      baseUrl: RELEASE_BASE_URL,
      store,
      withoutMainlandMirror,
      log
    });
    log(
      result.advanced
        ? `Published ${platform} ${version}; every platform is at ${version}, latest.json advanced`
        : result.latest
          ? `Published ${platform} ${version}; latest.json already names ${result.latest.version}`
          : `Published ${platform} ${version}; latest.json stays at the last lockstep release`
    );
  } else {
    await promote({
      version,
      baseUrl: RELEASE_BASE_URL,
      store,
      withoutMainlandMirror,
      log
    });
    log(`Published Product Release ${version}`);
  }
}

if (import.meta.main)
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
