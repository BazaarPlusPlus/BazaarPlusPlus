#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';
import {
  RELEASE_BASE_URL,
  WORKSPACE_ROOT,
  readProductVersion
} from './product.ts';
import {
  synchronizeProductProjections,
  checkProductProjections
} from './projections.ts';
import {
  preparePayload,
  buildProduct,
  payloadPaths,
  assertBuildOwner,
  assertReleaseBuildArgs
} from './payload.ts';
import {
  uploadPlatform,
  uploadAndroidPlatform,
  recordMainlandMirror,
  promotePlatform,
  promoteRelease
} from './publish.ts';
import { verifyMainlandMirrors } from './mirror.ts';
import type { RunRsync } from './mirror.ts';
import { r2StoreFromEnvironment } from './r2-store.ts';
import type { FetchImpl } from './r2-store.ts';
import { gameLibsStoreFromEnvironment } from './game-assembly-snapshot.ts';
import type { ManagedLockProvenance } from './game-assembly-snapshot.ts';
import {
  androidPaths,
  buildAndroidRelease,
  prepareAndroidPayload
} from './android-release.ts';
import {
  RELEASE_PLATFORMS,
  releasePlatform,
  resolveBuildPlatform
} from './release-platforms.ts';
import type { BuildPlatform } from './release-platforms.ts';
import type { ObjectStore } from './r2-store.ts';
import type { PayloadBuildRecord } from './payload.ts';

export type ReleaseCommand =
  | 'sync'
  | 'check'
  | 'prepare'
  | 'build'
  | 'upload'
  | 'mirror'
  | 'verify-mirror'
  | 'promote'
  | 'assert-build-owner';

export interface ReleaseInvocation {
  command: ReleaseCommand;
  platform: BuildPlatform | undefined;
  msbuildArgs: string[];
  latest: boolean;
  allowUnverifiedMirror: boolean;
  withoutMainlandMirror: boolean;
}

export interface ReleaseHelp {
  command: 'help';
  platform?: undefined;
  msbuildArgs?: undefined;
  latest?: undefined;
  allowUnverifiedMirror?: undefined;
  withoutMainlandMirror?: undefined;
}

export type ReleaseArgs = ReleaseHelp | ReleaseInvocation;

export interface ReleaseMainOptions {
  workspaceRoot?: string;
  hostPlatform?: string;
  createStore?: () => ObjectStore;
  createGameLibsStore?: typeof gameLibsStoreFromEnvironment;
  /** Downloads the release artifact for mirror and checks every mirror. */
  fetchImpl?: FetchImpl;
  runRsync?: RunRsync;
  log?: (message: string) => void;
  now?: Date;
}

const usage = `Product release commands (run from any directory):
  bun release/cli.ts sync                         Project VERSION into toolchain files
  bun release/cli.ts check                        Check product source projections, mod/installer Data Root and schema agreement, the Snapshot Lock and release configuration
  bun release/cli.ts prepare --platform macos      Build and validate one platform Payload
  bun release/cli.ts build --platform macos        Build and sign the native installer
  bun release/cli.ts upload --platform macos       Upload immutable platform artifacts
  bun release/cli.ts prepare --platform android    Build the Android host and stage the Android Payload
  bun release/cli.ts build --platform android      Zip and sign the Managed Payload; build, align and sign
                                                    the Android App; pack the Interop Package and sign its
                                                    interop statement
  bun release/cli.ts upload --platform android     Stream the Android App, the Interop Package, the Managed
                                                    Payload zip and the fragment to R2
  bun release/cli.ts mirror --platform macos       Push the uploaded installer (android: the App APK) to the
                                                    mainland mirror, check the served bytes and record it
  bun release/cli.ts verify-mirror [--platform android]
                                                    Re-check the recorded mainland mirrors of VERSION
                                                    (Windows and macOS unless --platform names one)
  bun release/cli.ts verify-mirror --latest       Re-check the mainland mirrors of the published platform manifests
  bun release/cli.ts promote                      Publish Windows and macOS at VERSION and advance latest.json
  bun release/cli.ts promote --platform macos     Publish one platform's manifest; latest.json advances once
                                                    Windows and macOS are at the same version
  bun release/cli.ts promote --platform android   Publish the Android Platform Release Manifest after checking
                                                    the three uploaded artifacts and the App's signer; Android
                                                    never moves latest.json

Use windows on the Windows build host. Prepare/build accept -- -p:ManagedPath=<path>;
android accepts -- -p:InteropPath=<dir> -p:LemonLoaderPath=<dir> instead, and its
build needs the Android App's JDK and SDK build-tools, openssl, and the Android
release keystore in BPP_SIGNING_SECRETS_DIR.
mirror pushes over rsync with the SSH key in MAINLAND_MIRROR_SSH_KEY and refuses
to record a mirror whose served size or sha256 differs from the release artifact
unless --allow-unverified-mirror is passed; promote refuses a platform without a
recorded mirror unless --without-mainland-mirror is passed.
Only upload, mirror, promote and build --platform android access R2, and require
CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID (the S3 pair is derived from the
token); prepare --platform android reads the private snapshot store only when the
Interop Snapshot is not under game-libs/. No command changes VERSION.
`;

export function parseReleaseArgs(args: readonly string[]): ReleaseArgs {
  const [command, ...rest] = args;
  if (!command || ['--help', '-h'].includes(command)) {
    if (rest.length) throw new Error('Help takes no additional arguments');
    return { command: 'help' };
  }
  const releaseCommand = (
    [
      'sync',
      'check',
      'prepare',
      'build',
      'upload',
      'mirror',
      'verify-mirror',
      'promote',
      'assert-build-owner'
    ] as const
  ).find((candidate) => candidate === command);
  if (!releaseCommand) throw new Error(usage);
  const { values, positionals, tokens } = parseArgs({
    args: rest,
    strict: true,
    allowPositionals: true,
    tokens: true,
    options: {
      platform: { type: 'string' },
      latest: { type: 'boolean' },
      'allow-unverified-mirror': { type: 'boolean' },
      'without-mainland-mirror': { type: 'boolean' }
    }
  });
  const seen = new Set<string>();
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
      `${command} takes no positional arguments; platforms are passed as --platform <macos|windows|android>`
    );
  if (
    tokens.some(
      (token) =>
        token.kind === 'positional' &&
        (!separator || token.index < separator.index)
    )
  )
    throw new Error('MSBuild properties must follow --');
  const platform = values.platform as BuildPlatform | undefined;
  const releasePlatforms = RELEASE_PLATFORMS.map(
    ({ buildPlatform }) => buildPlatform
  );
  const mustBe = (platforms: readonly BuildPlatform[]) =>
    `--platform must be ${platforms.slice(0, -1).join(', ')} or ${platforms.at(-1)}`;
  if (['prepare', 'build', 'upload'].includes(command)) {
    if (platform === undefined || !releasePlatforms.includes(platform))
      throw new Error(mustBe(releasePlatforms));
  } else if (command === 'mirror') {
    if (platform === undefined || !releasePlatforms.includes(platform))
      throw new Error(mustBe(releasePlatforms));
  } else if (command === 'verify-mirror') {
    if (platform !== undefined && !releasePlatforms.includes(platform))
      throw new Error(mustBe(releasePlatforms));
  } else if (command === 'promote') {
    if (platform !== undefined && !releasePlatforms.includes(platform))
      throw new Error(mustBe(releasePlatforms));
  } else if (platform !== undefined) {
    throw new Error(`${command} is a product-wide operation`);
  }
  if (values.latest && command !== 'verify-mirror')
    throw new Error('--latest is only valid for verify-mirror');
  if (values['allow-unverified-mirror'] && command !== 'mirror')
    throw new Error('--allow-unverified-mirror is only valid for mirror');
  if (values['without-mainland-mirror'] && command !== 'promote')
    throw new Error('--without-mainland-mirror is only valid for promote');
  assertReleaseBuildArgs(positionals, platform);
  if (command === 'assert-build-owner' && rest.length)
    throw new Error('assert-build-owner takes no arguments');
  return {
    command: releaseCommand,
    platform,
    msbuildArgs: positionals,
    latest: values.latest ?? false,
    allowUnverifiedMirror: values['allow-unverified-mirror'] ?? false,
    withoutMainlandMirror: values['without-mainland-mirror'] ?? false
  };
}

// Report non-online builds because the release ships to online users (ADR 0004).
function describeLockEntry({
  key,
  gameVersion,
  buildid,
  channel
}: ManagedLockProvenance): string {
  const entry = `lock entry ${key} (${gameVersion}, buildid ${buildid})`;
  return channel === 'online'
    ? entry
    : `${entry}; WARNING: the product compiles against the online entry once it is captured`;
}

function bundleInstaller(rootDir: string, token: string): void {
  execFileSync('bun', [path.join(rootDir, 'scripts/bundle.ts')], {
    cwd: rootDir,
    stdio: 'inherit',
    env: { ...process.env, BPP_RELEASE_LOCK_TOKEN: token }
  });
}

export async function main(
  args: readonly string[],
  {
    workspaceRoot = WORKSPACE_ROOT,
    hostPlatform = process.platform,
    createStore = r2StoreFromEnvironment,
    createGameLibsStore = gameLibsStoreFromEnvironment,
    fetchImpl = fetch,
    runRsync,
    log = console.log,
    now = new Date()
  }: ReleaseMainOptions = {}
): Promise<void> {
  const {
    command,
    platform,
    msbuildArgs,
    latest,
    allowUnverifiedMirror,
    withoutMainlandMirror
  } = parseReleaseArgs(args);
  const android = platform === 'android';
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
  if (
    command === 'build' &&
    !android &&
    platform !== resolveBuildPlatform(hostPlatform)
  )
    throw new Error(`Build ${platform} on its native host`);
  if (command === 'sync') {
    const version = synchronizeProductProjections(workspaceRoot);
    log(`Product projections synchronized to ${version}`);
    return;
  }
  // Read-only and credential-free: it checks the public release origin and
  // the mirror, so it must not depend on source alignment or R2 access.
  if (command === 'verify-mirror') {
    const verified = await verifyMainlandMirrors({
      baseUrl: RELEASE_BASE_URL,
      version: latest ? null : readProductVersion(workspaceRoot),
      platform,
      latest,
      fetchImpl,
      log
    });
    log(`Mainland mirror verified for ${verified.version}`);
    return;
  }
  const version = checkProductProjections(workspaceRoot, {
    warn: (message) => log(`WARNING: ${message}`),
    now
  });
  if (command === 'check') {
    log(
      `Product ${version}: version, inventory, badges, Data Root, history schema, Snapshot Lock and release configuration aligned`
    );
    return;
  }
  if (command === 'prepare' && android) {
    const records = await prepareAndroidPayload({
      workspaceRoot,
      msbuildArgs,
      gameLibsStore: createGameLibsStore,
      log
    });
    for (const { lockEntry } of Object.values(records))
      log(
        `Prepared android Payload for ${version} against lock entry ${lockEntry.key} (${lockEntry.gameVersion}, ${lockEntry.loader.tag})`
      );
    return;
  }
  if (command === 'build' && android) {
    const manifest = await buildAndroidRelease({
      workspaceRoot,
      msbuildArgs,
      gameLibsStore: createGameLibsStore,
      log
    });
    log(
      `Built android ${version}: Android App ${manifest.app.path} in ${androidPaths(workspaceRoot).root}`
    );
    for (const { lockEntry, game, interop, managedPayload } of Object.values(
      manifest.adaptations
    ))
      log(
        `  ${lockEntry.key} (${game.gameVersion}): Interop Package ${interop.path}, Managed Payload ${managedPayload.path}`
      );
    return;
  }
  if (command === 'prepare') {
    if (platform === undefined || platform === 'android')
      throw new Error(`prepare needs a desktop platform, got ${platform}`);
    const record = preparePayload({ workspaceRoot, platform, msbuildArgs });
    log(
      `Prepared ${platform} Payload for ${version} against ${describeLockEntry(record.lockEntry)}`
    );
    return;
  }
  if (command === 'build') {
    if (platform === undefined || platform === 'android')
      throw new Error(`build needs a desktop platform, got ${platform}`);
    buildProduct({
      workspaceRoot,
      platform,
      msbuildArgs,
      bundle: ({ token }) => bundleInstaller(rootDir, token)
    });
    const record = JSON.parse(
      fs.readFileSync(payloadPaths(rootDir, platform).buildRecord, 'utf8')
    ) as PayloadBuildRecord;
    log(
      `Built ${platform} ${version} against ${describeLockEntry(record.lockEntry)}`
    );
    return;
  }
  const store = createStore();
  if (command === 'upload' && android) {
    await uploadAndroidPlatform({
      workspaceRoot,
      baseUrl: RELEASE_BASE_URL,
      store,
      log
    });
    log(
      `Uploaded android ${version}. Run mirror --platform android, then promote --platform android.`
    );
  } else if (command === 'upload') {
    if (platform === undefined || platform === 'android')
      throw new Error(`upload needs a desktop platform, got ${platform}`);
    await uploadPlatform({
      workspaceRoot,
      platform,
      baseUrl: RELEASE_BASE_URL,
      store
    });
    log(
      `Uploaded ${platform} ${version}. Run mirror --platform ${platform}; once both platforms are mirrored, promote, or promote --platform ${platform} for a single-platform release.`
    );
  } else if (command === 'mirror') {
    if (platform === undefined) throw new Error('mirror needs --platform');
    const sshKey = process.env.MAINLAND_MIRROR_SSH_KEY;
    if (!sshKey?.trim())
      throw new Error(
        'mirror requires MAINLAND_MIRROR_SSH_KEY; release/github-secrets.json names its local source'
      );
    const record = await recordMainlandMirror({
      version,
      platform,
      store,
      sshKey,
      fetchImpl,
      runRsync,
      allowUnverified: allowUnverifiedMirror,
      log
    });
    log(
      `Recorded ${platform} mainland mirror for ${version}: ${record.url}${record.verified ? '' : ' (unverified)'}`
    );
  } else if (platform) {
    const result = await promotePlatform({
      version,
      platform,
      baseUrl: RELEASE_BASE_URL,
      store,
      withoutMainlandMirror,
      log,
      now
    });
    log(
      result.advanced
        ? `Published ${platform} ${version}; every lockstep platform is at ${version}, latest.json advanced`
        : releasePlatform(platform).kind !== 'tauri'
          ? `Published ${platform} ${version}; latest.json follows Windows and macOS only`
          : result.latest
            ? `Published ${platform} ${version}; latest.json already names ${result.latest.version}`
            : `Published ${platform} ${version}; latest.json stays at the last lockstep release`
    );
  } else {
    await promoteRelease({
      version,
      baseUrl: RELEASE_BASE_URL,
      store,
      withoutMainlandMirror,
      log,
      now
    });
    log(`Published Product Release ${version}`);
  }
}

if (import.meta.main)
  main(process.argv.slice(2)).catch((error: Error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
