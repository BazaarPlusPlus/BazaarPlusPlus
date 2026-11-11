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
  recordMainlandMirrors,
  promotePlatform,
  promoteRelease
} from './publish.ts';
import { fetchMirrorPage, verifyMainlandMirrors } from './mirror.ts';
import { assertMirrorUrl } from './manifest.ts';
import { r2StoreFromEnvironment } from './r2-store.ts';
import { gameLibsStoreFromEnvironment } from './game-assembly-snapshot.ts';
import type { ManagedLockProvenance } from './game-assembly-snapshot.ts';
import {
  androidPaths,
  buildAndroidRelease,
  prepareAndroidPayload
} from './android-release.ts';
import {
  RELEASE_PLATFORMS,
  defaultTargetBuildPlatforms,
  releasePlatform,
  resolveBuildPlatform
} from './release-platforms.ts';
import type { BuildPlatform, TauriBuildPlatform } from './release-platforms.ts';
import type { ObjectStore } from './r2-store.ts';
import type { PayloadBuildRecord } from './payload.ts';

/** A release command; `help` prints the usage. */
export type ReleaseCommand =
  | 'sync'
  | 'check'
  | 'prepare'
  | 'build'
  | 'upload'
  | 'mirror'
  | 'mirror-all'
  | 'verify-mirror'
  | 'promote'
  | 'assert-build-owner';

/** A release command line after validation. */
export interface ReleaseInvocation {
  command: ReleaseCommand;
  platform: BuildPlatform | undefined;
  url: string | undefined;
  urls: Record<TauriBuildPlatform, string> | undefined;
  msbuildArgs: string[];
  latest: boolean;
  allowUnverifiedMirror: boolean;
  withoutMainlandMirror: boolean;
}

/** A request for the usage; it carries none of the invocation fields. */
export interface ReleaseHelp {
  command: 'help';
  platform?: undefined;
  url?: undefined;
  urls?: undefined;
  msbuildArgs?: undefined;
  latest?: undefined;
  allowUnverifiedMirror?: undefined;
  withoutMainlandMirror?: undefined;
}

/** What parseReleaseArgs returns. */
export type ReleaseArgs = ReleaseHelp | ReleaseInvocation;

/** The effects main reaches the release environment through. */
export interface ReleaseMainOptions {
  workspaceRoot?: string;
  hostPlatform?: string;
  createStore?: () => ObjectStore;
  createGameLibsStore?: typeof gameLibsStoreFromEnvironment;
  probeMirror?: typeof fetchMirrorPage;
  log?: (message: string) => void;
  now?: Date;
}

const usage = `Product release commands (run from any directory):
  bun release/cli.ts sync                         Project VERSION into toolchain files
  bun release/cli.ts check                        Check product source projections, mod/installer Data Root and schema agreement, the Snapshot Lock and release configuration
  bun release/cli.ts prepare --platform macos      Build and validate one platform Payload
  bun release/cli.ts build --platform macos        Build and sign the native installer
  bun release/cli.ts upload --platform macos       Upload immutable platform artifacts
  bun release/cli.ts prepare --platform android    Build the Android host and stage the Android Mod Payload
  bun release/cli.ts build --platform android      Zip and sign the Managed Payload; build, align and sign
                                                    the Android App; pack the Interop Package and sign its
                                                    interop statement
  bun release/cli.ts upload --platform android     Stream the Android App, the Interop Package, the Managed
                                                    Payload zip and the fragment to R2
  bun release/cli.ts mirror-all --windows-url <share-url> --macos-url <share-url>
                                                    Verify both uploaded platforms and record both mainland mirrors
  bun release/cli.ts mirror --platform macos --url <share-url>
                                                    Verify and record the platform's mainland mirror page
  bun release/cli.ts verify-mirror [--platform macos]
                                                    Re-check the recorded mainland mirrors of VERSION
  bun release/cli.ts verify-mirror --latest       Re-check the mainland mirrors of the published platform manifests
  bun release/cli.ts promote                      Publish Windows and macOS at VERSION and advance latest.json
  bun release/cli.ts promote --platform macos     Publish one platform's manifest; latest.json advances once
                                                    Windows and macOS are at the same version
  bun release/cli.ts promote --platform android   Publish the Android Platform Release Manifest after checking
                                                    the three uploaded artifacts and the App's signer; Android
                                                    records no mainland mirror and never moves latest.json

Use windows on the Windows build host. Prepare/build accept -- -p:ManagedPath=<path>;
android accepts -- -p:InteropPath=<dir> -p:LemonLoaderPath=<dir> instead, and its
build needs the Android App's JDK and SDK build-tools, openssl, and the Android
release keystore in BPP_SIGNING_SECRETS_DIR.
mirror refuses a share page that does not serve the uploaded installer unless
--allow-unverified-mirror is passed; promote refuses a platform without a
recorded mirror unless --without-mainland-mirror is passed. mirror-all requires
both platforms uploaded at the same version and commit before recording either mirror.
Only upload, mirror, mirror-all, promote and build --platform android access R2,
and require CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID (the S3 pair is derived
from the token); prepare --platform android reads the private snapshot store only
when the Interop Snapshot is not under game-libs/. No command changes VERSION.
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
      'mirror-all',
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
      url: { type: 'string' },
      'windows-url': { type: 'string' },
      'macos-url': { type: 'string' },
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
  // A command-line value; the checks below reject any other platform name.
  const platform = values.platform as BuildPlatform | undefined;
  // Android has no mainland mirror yet (ADR 0010), so mirror and verify-mirror
  // take only the Tauri platforms.
  const tauriPlatforms: readonly BuildPlatform[] =
    defaultTargetBuildPlatforms();
  const releasePlatforms = RELEASE_PLATFORMS.map(
    ({ buildPlatform }) => buildPlatform
  );
  const mustBe = (platforms: readonly BuildPlatform[]) =>
    `--platform must be ${platforms.slice(0, -1).join(', ')} or ${platforms.at(-1)}`;
  if (['prepare', 'build', 'upload'].includes(command)) {
    if (platform === undefined || !releasePlatforms.includes(platform))
      throw new Error(mustBe(releasePlatforms));
  } else if (command === 'mirror') {
    if (platform === undefined || !tauriPlatforms.includes(platform))
      throw new Error(mustBe(tauriPlatforms));
  } else if (command === 'verify-mirror') {
    if (platform !== undefined && !tauriPlatforms.includes(platform))
      throw new Error(mustBe(tauriPlatforms));
  } else if (command === 'promote') {
    if (platform !== undefined && !releasePlatforms.includes(platform))
      throw new Error(mustBe(releasePlatforms));
  } else if (platform !== undefined) {
    throw new Error(`${command} is a product-wide operation`);
  }
  if (command === 'mirror') {
    if (!values.url) throw new Error('mirror requires --url <share-url>');
    assertMirrorUrl(values.url);
  } else if (values.url !== undefined) {
    throw new Error('--url is only valid for mirror');
  }
  let urls: Record<TauriBuildPlatform, string> | undefined;
  if (command === 'mirror-all') {
    const windows = values['windows-url'];
    const macos = values['macos-url'];
    if (!windows || !macos)
      throw new Error('mirror-all requires --windows-url and --macos-url');
    urls = { windows, macos };
    for (const url of Object.values(urls)) assertMirrorUrl(url);
  } else if (
    values['windows-url'] !== undefined ||
    values['macos-url'] !== undefined
  ) {
    throw new Error(
      '--windows-url and --macos-url are only valid for mirror-all'
    );
  }
  if (values.latest && command !== 'verify-mirror')
    throw new Error('--latest is only valid for verify-mirror');
  if (
    values['allow-unverified-mirror'] &&
    !['mirror', 'mirror-all'].includes(command)
  )
    throw new Error(
      '--allow-unverified-mirror is only valid for mirror or mirror-all'
    );
  if (values['without-mainland-mirror'] && command !== 'promote')
    throw new Error('--without-mainland-mirror is only valid for promote');
  if (
    values['without-mainland-mirror'] &&
    platform !== undefined &&
    releasePlatform(platform).kind !== 'tauri'
  )
    throw new Error(`${platform} has no mainland mirror to waive`);
  assertReleaseBuildArgs(positionals, platform);
  if (command === 'assert-build-owner' && rest.length)
    throw new Error('assert-build-owner takes no arguments');
  return {
    command: releaseCommand,
    platform,
    url: values.url,
    urls,
    msbuildArgs: positionals,
    latest: values.latest ?? false,
    allowUnverifiedMirror: values['allow-unverified-mirror'] ?? false,
    withoutMainlandMirror: values['without-mainland-mirror'] ?? false
  };
}

// The Snapshot Lock entry a sealed build record names: the product ships to
// online users, so another channel is reported, not hidden (ADR 0004).
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
  execFileSync('bash', [path.join(rootDir, 'scripts/bundle.sh')], {
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
    probeMirror = fetchMirrorPage,
    log = console.log,
    now = new Date()
  }: ReleaseMainOptions = {}
): Promise<void> {
  const {
    command,
    platform,
    url,
    urls,
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
    const record = await prepareAndroidPayload({
      workspaceRoot,
      msbuildArgs,
      gameLibsStore: createGameLibsStore,
      log
    });
    log(
      `Prepared android Payload for ${version} against lock entry ${record.lockEntry.key} (${record.lockEntry.gameVersion}, ${record.lockEntry.loader.tag})`
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
      `Built android ${version} against lock entry ${manifest.lockEntry.key} (${manifest.game.gameVersion}): Android App ${manifest.app.path}, Interop Package ${manifest.interop.path}, Managed Payload ${manifest.managedPayload.path} in ${androidPaths(workspaceRoot).root}`
    );
    return;
  }
  if (command === 'prepare') {
    // parseReleaseArgs requires a platform here, and android returned above.
    if (platform === undefined || platform === 'android')
      throw new Error(`prepare needs a desktop platform, got ${platform}`);
    const record = preparePayload({ workspaceRoot, platform, msbuildArgs });
    log(
      `Prepared ${platform} Payload for ${version} against ${describeLockEntry(record.lockEntry)}`
    );
    return;
  }
  if (command === 'build') {
    // parseReleaseArgs requires a platform here, and android returned above.
    if (platform === undefined || platform === 'android')
      throw new Error(`build needs a desktop platform, got ${platform}`);
    buildProduct({
      workspaceRoot,
      platform,
      msbuildArgs,
      bundle: ({ token }) => bundleInstaller(rootDir, token)
    });
    // buildProduct verified and sealed the record; read it back for the log.
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
      `Uploaded android ${version}. Run promote --platform android to publish it.`
    );
  } else if (command === 'upload') {
    // parseReleaseArgs requires a platform here, and android uploaded above.
    if (platform === undefined || platform === 'android')
      throw new Error(`upload needs a desktop platform, got ${platform}`);
    await uploadPlatform({
      workspaceRoot,
      platform,
      baseUrl: RELEASE_BASE_URL,
      store
    });
    log(
      `Uploaded ${platform} ${version}. Once both platforms are uploaded, run mirror-all, then promote. For a single-platform release, run mirror, then promote --platform ${platform}.`
    );
  } else if (command === 'mirror') {
    // parseReleaseArgs requires a Tauri platform and a share address here.
    if (platform === undefined || url === undefined)
      throw new Error('mirror needs --platform and --url');
    await recordMainlandMirror({
      version,
      platform,
      url,
      store,
      probeMirror,
      allowUnverified: allowUnverifiedMirror,
      log
    });
    log(`Recorded ${platform} mainland mirror for ${version}`);
  } else if (command === 'mirror-all') {
    // parseReleaseArgs requires both share addresses here.
    if (urls === undefined)
      throw new Error('mirror-all needs --windows-url and --macos-url');
    await recordMainlandMirrors({
      version,
      urls,
      baseUrl: RELEASE_BASE_URL,
      store,
      probeMirror,
      allowUnverified: allowUnverifiedMirror,
      log
    });
    log(`Recorded both mainland mirrors for ${version}. Ready to promote.`);
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
