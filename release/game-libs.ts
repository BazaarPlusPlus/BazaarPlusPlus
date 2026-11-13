import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import {
  GAME_LIBS_BUCKET,
  captureSnapshot,
  fetchSnapshot,
  gameLibsStoreFromEnvironment,
  managedDirectoryDigest,
  previewBehindOnline,
  publishSnapshot,
  readGameVersion,
  steamCapture,
  steamChangeKind,
  steamStale
} from './game-assembly-snapshot.ts';
import type { SteamToolRunner } from './game-assembly-snapshot.ts';
import {
  gameStatusStoreFromEnvironment,
  publishAndroidGameStatus,
  publishGameStatus
} from './game-status.ts';
import type { SteamState } from './game-status.ts';
import {
  captureInteropSnapshot,
  checkGameLibsLock,
  fetchAndroidInput,
  fetchInteropSnapshot,
  publishInteropSnapshot,
  uploadApks
} from './interop-snapshot.ts';
import type { GenerateInteropOptions } from './interop-snapshot.ts';
import {
  inspectDownloadedApk,
  officialApkChanged,
  recordApkEtag
} from './official-apk.ts';
import type { ApkHeadFetch } from './official-apk.ts';
import { WORKSPACE_ROOT } from './product.ts';
import type { FetchImpl, ObjectStore } from './r2-store.ts';
import {
  GAME_LIBS_LOCK_PATH,
  isInteropPlatform,
  lockKey,
  managedEntries,
  readGameLibsLock,
  validateGameLibsLock
} from './snapshot-lock.ts';
import type {
  AndroidSource,
  ManagedPlatform,
  SnapshotChannel,
  SnapshotPlatform
} from './snapshot-lock.ts';

// The Game Assembly and Interop Snapshot CLI: argument parsing and dispatch.
// game-assembly-snapshot.ts, interop-snapshot.ts, official-apk.ts and
// game-status.ts hold what each verb does; snapshot-lock.ts owns the Snapshot
// Lock schema. bazaarplusplus-mod/scripts/game.sh and the snapshot workflows
// drive this module; build/ManagedPath.props and build/InteropPath.props read
// the lock file directly. No library module imports it.

function printLine(value: string): void {
  process.stdout.write(`${value}\n`);
}

const log = (message: string): boolean => process.stderr.write(`${message}\n`);

const usage = `Game Assembly and Interop Snapshot commands (driven by bazaarplusplus-mod/scripts/game.sh):
  bun release/game-libs.ts game-version <managed-dir>
  bun release/game-libs.ts digest <managed-dir>
  bun release/game-libs.ts snapshot --managed <dir> --buildid <id> --steam-branch <branch>
  bun release/game-libs.ts steam-stale --channel <online|staging|ptr>
                                             Print {buildid, updatedAt, platforms}: the platforms whose lock
                                             entry is behind Steam (needs STEAM_SNAPSHOT_LOGIN)
  bun release/game-libs.ts steam-capture --platform <macos|windows> --channel <online|staging|ptr>
                                             Capture a lock entry from its Steam depot (needs
                                             STEAM_SNAPSHOT_LOGIN, and STEAM_BETA_PASSWORDS for
                                             staging and ptr)
  bun release/game-libs.ts steam-change --previous <lock.json> --channel <online|staging|ptr>
                                             Print code, content or none: what the captures
                                             changed against that earlier lock
  bun release/game-libs.ts game-status --state <steam-stale.json>
                                             Write the public Game Status when the lock records
                                             that online build
  bun release/game-libs.ts android-game-status
                                             Write the public Android Game Status from the lock's
                                             android-publisher entry
  bun release/game-libs.ts snapshot-android --apk <base.apk> [--apk <split.apk> ...] [--apk-etag <etag>]
      [--interop <dir>] [--loader <zip> --loader-tag <tag>] [--patcher <tar.gz> --patcher-tag <tag>]
      [--cpp2il <file>] [--native-package <id>@<version> ...]
  bun release/game-libs.ts upload-apks --apk <base.apk> [--apk <split.apk> ...]
                                             Store APKs pulled from a device in the private bucket and
                                             print their sha256 values, comma-separated, base first
  bun release/game-libs.ts fetch-apks --sha256s <base>,<split>,...
                                             Fetch stored APKs by sha256 and print their local paths
  bun release/game-libs.ts publish --platform <macos|windows> --channel <online|staging|ptr>
  bun release/game-libs.ts publish --platform android --channel <publisher|play>
  bun release/game-libs.ts fetch --platform <p> --channel <c> [--steam-managed <dir>] [--allow-unlocked]
  bun release/game-libs.ts lock-check [--managed <dir>]
  bun release/game-libs.ts entries
  bun release/game-libs.ts behind-online --platform <macos|windows> --channel <staging|ptr>
                                             Print the online game version when this entry's
                                             Steam build is older than online's, else nothing
  bun release/game-libs.ts official-apk     Print changed or unchanged: the publisher's APK against
                                             the android-publisher entry's (size and ETag, no download)
  bun release/game-libs.ts downloaded-apk --apk <TheBazaar.apk> --headers <curl --dump-header file>
                                             Print {kind, size, sha256, etag, lockedSha256, lockedEtag,
                                             etagBranch}: kind is capture, etag or none
  bun release/game-libs.ts record-apk-etag --apk <TheBazaar.apk> --apk-etag <etag>
                                             Record the ETag the locked APK is now served with
snapshot-android records the base APK and its splits (a Google Play install)
under the entry its versionName names: android-play for a bare x.y.z, else
android-publisher. It reuses that entry's toolchain, or the other Android
entry's while it is empty, unless files are given, and generates interop with
the Patcher's Linux x64 CLI unless --interop names a directory generated from
the same build. fetch android <source> prints the interop directory and
unpacks the Loader Release beside it. publish always, and
fetch when no verified local copy exists, use the private bucket
${GAME_LIBS_BUCKET} through CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID (the
S3 pair is derived from the token).`;

// Every option but workspaceRoot is a test seam; each default is what the
// command line uses.
export interface CliOptions {
  workspaceRoot?: string;
  /** Where the stores read CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID. */
  env?: NodeJS.ProcessEnv;
  /** The fetch the Game Status store sends its R2 requests with. */
  r2Fetch?: FetchImpl;
  /** The private store of publish, official-apk and snapshot-android. */
  gameLibsStore?: () => ObjectStore;
  /** The HEAD request official-apk sends to the APK publisher. */
  apkFetch?: ApkHeadFetch;
  /** Runs tools/SteamSnapshot for steam-stale and steam-capture. */
  runTool?: SteamToolRunner;
  /** Generates interop for snapshot-android without --interop. */
  generate?: (options: GenerateInteropOptions) => void;
}

export async function cliMain(
  argv: readonly string[],
  {
    workspaceRoot = WORKSPACE_ROOT,
    env = process.env,
    r2Fetch,
    gameLibsStore = () => gameLibsStoreFromEnvironment(env),
    apkFetch,
    runTool,
    generate
  }: CliOptions = {}
): Promise<number> {
  const [verb, ...rest] = argv;
  try {
    if (!verb || verb === '--help') {
      printLine(usage);
      return 0;
    }
    const { values, positionals } = parseArgs({
      args: rest,
      strict: true,
      allowPositionals: true,
      options: {
        managed: { type: 'string' },
        previous: { type: 'string' },
        state: { type: 'string' },
        buildid: { type: 'string' },
        'steam-branch': { type: 'string' },
        platform: { type: 'string' },
        channel: { type: 'string' },
        'steam-managed': { type: 'string' },
        'allow-unlocked': { type: 'boolean' },
        apk: { type: 'string', multiple: true },
        headers: { type: 'string' },
        interop: { type: 'string' },
        loader: { type: 'string' },
        'loader-tag': { type: 'string' },
        patcher: { type: 'string' },
        'patcher-tag': { type: 'string' },
        cpp2il: { type: 'string' },
        'native-package': { type: 'string', multiple: true },
        'apk-etag': { type: 'string' },
        sha256s: { type: 'string' }
      }
    });
    const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
    // The options that take one string value.
    type StringOption = {
      [Name in keyof typeof values]-?: (typeof values)[Name] extends
        | string
        | undefined
        ? Name
        : never;
    }[keyof typeof values];
    const required = (name: StringOption): string => {
      const value = values[name];
      if (!value) throw new Error(`${verb} requires --${name}\n${usage}`);
      return value;
    };
    const apks = values.apk ?? [];
    const oneApk = (): string => {
      if (apks.length !== 1)
        throw new Error(`${verb} takes one --apk\n${usage}`);
      return apks[0];
    };
    switch (verb) {
      case 'official-apk': {
        const result = await officialApkChanged({
          workspaceRoot,
          storeFactory: gameLibsStore,
          fetchImpl: apkFetch
        });
        log(
          `Official APK ${result.officialSize} bytes, ETag ${result.officialEtag}; locked ${result.lockedGameVersion} ${result.lockedSize} bytes, ETag ${result.lockedEtag}`
        );
        printLine(result.changed ? 'changed' : 'unchanged');
        return 0;
      }
      case 'downloaded-apk': {
        printLine(
          JSON.stringify(
            await inspectDownloadedApk({
              workspaceRoot,
              apk: oneApk(),
              headers: required('headers')
            })
          )
        );
        return 0;
      }
      case 'record-apk-etag': {
        const gameVersion = await recordApkEtag({
          workspaceRoot,
          apk: oneApk(),
          etag: required('apk-etag')
        });
        log(`Recorded the ETag of ${gameVersion} in ${GAME_LIBS_LOCK_PATH}`);
        return 0;
      }
      case 'game-version':
      case 'digest': {
        if (positionals.length !== 1)
          throw new Error(`${verb} takes one Managed directory\n${usage}`);
        printLine(
          verb === 'digest'
            ? managedDirectoryDigest(positionals[0])
            : readGameVersion(positionals[0])
        );
        return 0;
      }
      case 'snapshot': {
        const result = captureSnapshot({
          workspaceRoot,
          modRoot,
          managedPath: required('managed'),
          buildid: required('buildid'),
          steamBranch: required('steam-branch')
        });
        log(
          `Captured ${result.key} ${result.manifest.gameVersion} (buildid ${result.manifest.buildid}) into ${result.directory} and recorded it in ${GAME_LIBS_LOCK_PATH}`
        );
        printLine(result.directory);
        return 0;
      }
      case 'steam-stale': {
        printLine(
          JSON.stringify(
            steamStale({
              workspaceRoot,
              modRoot,
              // steamStale checks the channel.
              channel: required('channel') as SnapshotChannel,
              runTool
            })
          )
        );
        return 0;
      }
      case 'steam-capture': {
        // steamCapture checks the platform and channel.
        const result = steamCapture({
          workspaceRoot,
          modRoot,
          platform: required('platform') as SnapshotPlatform,
          channel: required('channel') as SnapshotChannel,
          runTool
        });
        log(
          `Captured ${result.key} ${result.manifest.gameVersion} (buildid ${result.manifest.buildid}) into ${result.directory} and recorded it in ${GAME_LIBS_LOCK_PATH}`
        );
        printLine(result.directory);
        return 0;
      }
      case 'steam-change': {
        printLine(
          steamChangeKind(
            validateGameLibsLock(
              JSON.parse(fs.readFileSync(required('previous'), 'utf8'))
            ),
            readGameLibsLock(workspaceRoot),
            // steamChangeKind checks the channel.
            required('channel') as SnapshotChannel
          )
        );
        return 0;
      }
      case 'game-status': {
        const status = await publishGameStatus({
          workspaceRoot,
          state: JSON.parse(
            fs.readFileSync(required('state'), 'utf8')
          ) as SteamState,
          store: gameStatusStoreFromEnvironment(env, { fetchImpl: r2Fetch })
        });
        log(
          status
            ? `Game Status: online build ${status.buildid}`
            : 'The lock does not record this online build yet, or Steam gave no update time; the Game Status stays as it is'
        );
        return 0;
      }
      case 'android-game-status': {
        const status = await publishAndroidGameStatus({
          workspaceRoot,
          store: gameStatusStoreFromEnvironment(env, { fetchImpl: r2Fetch })
        });
        log(
          status
            ? `Android Game Status: ${status.gameVersion}`
            : 'The lock has no android-publisher entry; the Android Game Status stays as it is'
        );
        return 0;
      }
      case 'snapshot-android': {
        if (apks.length === 0)
          throw new Error(`${verb} requires --apk\n${usage}`);
        const result = await captureInteropSnapshot({
          workspaceRoot,
          modRoot,
          apks,
          apkEtag: values['apk-etag'] ?? null,
          interop: values.interop ?? '',
          loader: values.loader ?? '',
          loaderTag: values['loader-tag'] ?? '',
          patcher: values.patcher ?? '',
          patcherTag: values['patcher-tag'] ?? '',
          cpp2il: values.cpp2il ?? '',
          nativePackages: values['native-package'] ?? [],
          storeFactory: gameLibsStore,
          generate,
          log
        });
        log(
          `Captured ${result.key} ${result.manifest.gameVersion} into ${result.directory} (loader ${result.loaderPath}) and recorded it in ${GAME_LIBS_LOCK_PATH}`
        );
        printLine(result.directory);
        return 0;
      }
      case 'upload-apks': {
        if (apks.length === 0)
          throw new Error(`${verb} requires --apk\n${usage}`);
        printLine((await uploadApks(apks, gameLibsStore(), log)).join(','));
        return 0;
      }
      case 'fetch-apks': {
        for (const sha256 of required('sha256s').split(','))
          printLine(
            await fetchAndroidInput({
              modRoot,
              kind: 'apk',
              sha256,
              store: gameLibsStore,
              log
            })
          );
        return 0;
      }
      case 'publish': {
        const platform = required('platform');
        const channel = required('channel');
        if (isInteropPlatform(platform)) {
          lockKey(platform, channel);
          await publishInteropSnapshot({
            workspaceRoot,
            modRoot,
            // lockKey checked the source.
            source: channel as AndroidSource,
            store: gameLibsStore(),
            log
          });
          return 0;
        }
        // Every platform that is not an interop one is a Managed one;
        // publishSnapshot checks the channel.
        await publishSnapshot({
          workspaceRoot,
          modRoot,
          platform: platform as ManagedPlatform,
          channel: channel as SnapshotChannel,
          store: gameLibsStore(),
          log
        });
        return 0;
      }
      case 'fetch': {
        if (isInteropPlatform(required('platform'))) {
          lockKey(required('platform'), required('channel'));
          const result = await fetchInteropSnapshot({
            workspaceRoot,
            modRoot,
            // lockKey checked the source.
            source: required('channel') as AndroidSource,
            storeFactory: gameLibsStore,
            log
          });
          log(`Loader Release ${result.loaderPath}`);
          printLine(result.interopPath);
          return 0;
        }
        // Every platform that is not an interop one is a Managed one;
        // fetchSnapshot checks the channel.
        const result = await fetchSnapshot({
          workspaceRoot,
          modRoot,
          platform: required('platform') as ManagedPlatform,
          channel: required('channel') as SnapshotChannel,
          steamManagedPath: values['steam-managed'] ?? '',
          allowUnlocked: values['allow-unlocked'] === true,
          log
        });
        printLine(result.managedPath);
        return 0;
      }
      case 'lock-check': {
        const result = checkGameLibsLock({
          workspaceRoot,
          managedPath: values.managed ?? ''
        });
        for (const warning of result.warnings) log(`WARNING: ${warning}`);
        if (result.verified)
          log(
            `Managed ${result.verified.gameVersion} matches lock ${result.verified.keys.join(', ')}`
          );
        for (const { key, gameVersion } of result.interop)
          log(`Interop ${gameVersion} matches lock ${key}`);
        return 0;
      }
      case 'entries': {
        for (const { platform, channel } of managedEntries(
          readGameLibsLock(workspaceRoot)
        ))
          printLine(`${platform} ${channel}`);
        return 0;
      }
      case 'behind-online': {
        const online = previewBehindOnline(
          readGameLibsLock(workspaceRoot),
          required('platform') as ManagedPlatform,
          required('channel') as SnapshotChannel
        );
        if (online) printLine(online.gameVersion);
        return 0;
      }
      default:
        throw new Error(`Unknown game-libs verb: ${verb}\n${usage}`);
    }
  } catch (error) {
    log(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

if (import.meta.main) {
  process.exitCode = await cliMain(process.argv.slice(2));
}
