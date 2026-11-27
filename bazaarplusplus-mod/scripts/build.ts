import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { previewBehindOnline } from '../../release/game-assembly-snapshot.ts';
import {
  managedEntries,
  readGameLibsLock
} from '../../release/snapshot-lock.ts';
import type {
  ManagedPlatform,
  SnapshotChannel
} from '../../release/snapshot-lock.ts';
import { withBuildLock } from './build-lock.ts';
import {
  MOD_ROOT,
  WORKSPACE_ROOT,
  collectProps,
  dotnetEnv,
  err,
  gameRoot,
  hostPlatform,
  managedProps,
  ok,
  propValue,
  resolveLockManagedPath,
  runDotnet,
  step
} from './mod-env.ts';

const MAIN_PROJECT = 'src/BazaarPlusPlus/BazaarPlusPlus.csproj';

function repairMacosTrampoline(
  root: string,
  props: readonly string[],
  env: NodeJS.ProcessEnv
): number {
  const installerSource =
    propValue('BPPInstallerSourcePath', props) ||
    env.BPP_INSTALLER_SOURCE_PATH ||
    path.join(
      WORKSPACE_ROOT,
      'bazaarplusplus-installer',
      'src-tauri',
      'resources'
    );
  const stub =
    env.BPP_TRAMPOLINE_STUB ||
    path.join(installerSource, 'Trampoline', 'macos', 'bpp_launcher');
  const result = spawnSync(
    'bun',
    [
      path.join(
        WORKSPACE_ROOT,
        'bazaarplusplus-installer',
        'scripts',
        'headless.ts'
      ),
      'repair',
      '--game',
      root,
      '--stub',
      stub
    ],
    {
      cwd: MOD_ROOT,
      env: dotnetEnv(env),
      stdio: 'inherit'
    }
  );
  if (result.error) throw result.error;
  return result.status ?? 1;
}

function cmdBuild(args: readonly string[], env: NodeJS.ProcessEnv): number {
  let deploy = false;
  const fastArgs: string[] = [];
  const props: string[] = [];
  for (const arg of args) {
    if (arg === '--fast') {
      fastArgs.push('--no-restore');
    } else if (arg === '--deploy') {
      deploy = true;
    } else if (arg.startsWith('-p:') || arg.startsWith('--property:')) {
      props.push(arg);
    } else {
      throw new Error(
        `Expected --deploy, --fast, or -p:Name=Value, got '${arg}'`
      );
    }
  }
  props.push(...managedProps(props, env));

  let deployProps = ['-p:BppDeployToGame=false'];
  if (deploy) {
    const root = gameRoot(env);
    if (!root) {
      throw new Error(
        'The Bazaar install was not found. Set BPP_GAME_ROOT=/path/to/The Bazaar.'
      );
    }
    deployProps = ['-p:BppDeployToGame=true', `-p:GamePath=${root}`];
    if (hostPlatform() === 'macos') {
      const repairCode = repairMacosTrampoline(root, props, env);
      if (repairCode !== 0) return repairCode;
    }
  }

  return runDotnet(
    ['build', MAIN_PROJECT, ...fastArgs, ...props, ...deployProps],
    { env }
  );
}

async function cmdMatrix(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  const entries: Array<{
    platform: ManagedPlatform;
    channel: SnapshotChannel;
  }> = [];
  if (args.length > 0) {
    const platform = hostPlatform();
    for (const channel of args) {
      if (channel !== 'online' && channel !== 'staging' && channel !== 'ptr') {
        throw new Error(
          `Expected a channel (online, staging or ptr), got '${channel}'`
        );
      }
      entries.push({ platform, channel });
    }
  } else {
    for (const entry of managedEntries(readGameLibsLock(WORKSPACE_ROOT))) {
      entries.push({ platform: entry.platform, channel: entry.channel });
    }
    if (entries.length === 0) {
      throw new Error(
        "The lock has no captured entries. Run 'just mod::snapshot' on a machine with the game, publish it, and commit build/game-libs.lock.json."
      );
    }
  }

  const failed: string[] = [];
  const lock = readGameLibsLock(WORKSPACE_ROOT);
  for (const { platform, channel } of entries) {
    step(`Matrix build against ${platform} ${channel}`);
    const online = previewBehindOnline(lock, platform, channel);
    if (online) {
      const message = `Skipped ${platform} ${channel}: its snapshot is older than online ${online.gameVersion}.`;
      err(message);
      if (env.GITHUB_ACTIONS === 'true') {
        process.stdout.write(
          `::warning title=Preview channel skipped::${message}\n`
        );
      }
      continue;
    }

    let managed: string;
    try {
      managed = await resolveLockManagedPath(platform, channel, { env });
    } catch (error) {
      err(error instanceof Error ? error.message : String(error));
      failed.push(`${platform} ${channel} (snapshot unavailable)`);
      continue;
    }

    const code = runDotnet(
      ['build', MAIN_PROJECT, '-c', 'CompatCheck', `-p:ManagedPath=${managed}`],
      { env }
    );
    if (code !== 0) {
      failed.push(`${platform} ${channel} (${managed})`);
    }
  }

  if (failed.length > 0) {
    err('Matrix build failed against:');
    for (const entry of failed) {
      process.stderr.write(`  ${entry}\n`);
    }
    return 1;
  }

  ok('Matrix build passed for every captured lock entry.');
  return 0;
}

function runSeedGates(
  props: readonly string[],
  env: NodeJS.ProcessEnv
): number {
  step('Validating voice subtitle embedded seed');
  const voiceCode = runDotnet(
    [
      'test',
      'tests/RuntimeIntegration.Tests/RuntimeIntegration.Tests.csproj',
      '-c',
      'Release',
      '--filter',
      'TestKind=EmbeddedSeed',
      ...props
    ],
    { env }
  );
  if (voiceCode !== 0) return voiceCode;

  step('Validating live build recommendation embedded seed');
  return runDotnet(
    [
      'test',
      'tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj',
      '-c',
      'Release',
      '--filter',
      'TestKind=EmbeddedSeed',
      ...props
    ],
    { env }
  );
}

function fetchRemoteData(
  props: readonly string[],
  env: NodeJS.ProcessEnv
): number {
  const canonicalDirectory =
    propValue('RemoteEmbeddedDataDirectory', props) ||
    path.join(MOD_ROOT, 'src', 'BazaarPlusPlus', 'obj', 'remote-data');
  const canonicalParent = path.dirname(canonicalDirectory);
  fs.mkdirSync(canonicalParent, { recursive: true });
  const stagingDirectory = fs.mkdtempSync(
    path.join(canonicalParent, '.remote-data-stage.')
  );
  try {
    const fetchCode = runDotnet(
      [
        'msbuild',
        MAIN_PROJECT,
        '-t:FetchRemoteEmbeddedData',
        ...props,
        `-p:RemoteEmbeddedDataDirectory=${stagingDirectory}`,
        '-p:ForceRemoteEmbeddedDataRefresh=true'
      ],
      { env }
    );
    if (fetchCode !== 0) return fetchCode;

    const gateCode = runSeedGates(
      [
        ...props,
        `-p:RemoteEmbeddedDataDirectory=${stagingDirectory}`,
        '-p:RemoteEmbeddedDataPrepared=true'
      ],
      env
    );
    if (gateCode !== 0) return gateCode;

    return runDotnet(
      [
        'run',
        '--project',
        'build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj',
        '--no-launch-profile',
        '--',
        'promote',
        stagingDirectory,
        canonicalDirectory,
        'voice-lines.json',
        'builds.json'
      ],
      { env }
    );
  } finally {
    fs.rmSync(stagingDirectory, { recursive: true, force: true });
  }
}

function cmdFetchData(args: readonly string[], env: NodeJS.ProcessEnv): number {
  const props = collectProps(args);
  return fetchRemoteData(props, env);
}

function cmdProduce(args: readonly string[], env: NodeJS.ProcessEnv): number {
  const releaseArtifacts = env.BPP_RELEASE_ARTIFACTS;
  if (!releaseArtifacts) {
    throw new Error(
      'Run bun release/cli.ts prepare to build an isolated Payload'
    );
  }
  const props = collectProps(args);
  const installerSource = propValue('BPPInstallerSourcePath', props);
  if (
    !installerSource ||
    !fs.statSync(installerSource, { throwIfNoEntry: false })?.isDirectory()
  ) {
    throw new Error(
      `Installer resources not found at '${installerSource}'. Pass -p:BPPInstallerSourcePath=/absolute/path/to/resources.`
    );
  }
  if (!propValue('ManagedPath', props)) {
    throw new Error('produce requires -p:ManagedPath.');
  }

  const commonArgs = [...props, `-p:BppReleasePlatform=${hostPlatform()}`];
  const fetchCode = fetchRemoteData(commonArgs, env);
  if (fetchCode !== 0) return fetchCode;

  return runDotnet(
    [
      'build',
      MAIN_PROJECT,
      '-t:BuildAll',
      ...commonArgs,
      '-p:UseArtifactsOutput=true',
      `-p:ArtifactsPath=${releaseArtifacts}`,
      '-p:BuildProductionPackage=true',
      '-p:RemoteEmbeddedDataPrepared=true'
    ],
    { env }
  );
}

async function main(argv: readonly string[]): Promise<number> {
  const [command, ...rest] = argv;
  switch (command) {
    case 'build':
      return withBuildLock(WORKSPACE_ROOT, (env) => cmdBuild(rest, env));
    case 'matrix':
      return withBuildLock(WORKSPACE_ROOT, (env) => cmdMatrix(rest, env));
    case 'fetch-data':
      return cmdFetchData(rest, process.env);
    case 'produce':
      return cmdProduce(rest, process.env);
    default:
      throw new Error(
        "Usage: bun scripts/build.ts <build|matrix|fetch-data|produce> [args...]; see 'just --list mod'."
      );
  }
}

if (import.meta.main) {
  try {
    process.exitCode = await main(process.argv.slice(2));
  } catch (error) {
    err(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
