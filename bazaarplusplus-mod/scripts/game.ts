import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { cliMain as gameLibsMain } from '../../release/game-libs.ts';
import {
  MOD_ROOT,
  collectProps,
  dotnetEnv,
  err,
  hostPlatform,
  locateAppmanifest,
  managedPath,
  parseAcfBuildId,
  parseAcfSteamBranch,
  propValue,
  resolveLockManagedPath,
  runDotnet,
  steamManagedPath,
  step
} from './mod-env.ts';

const DECOMPILE_ALL = [
  'Assembly-CSharp',
  'BazaarGameClient',
  'BazaarGameShared',
  'BazaarBattleService',
  'TheBazaarRuntime',
  'FMODUnity'
];

function configHome(env: NodeJS.ProcessEnv = process.env): string {
  const home =
    env.BPP_CONFIG_HOME || path.join(os.homedir(), '.config', 'bazaarplusplus');
  if (!path.isAbsolute(home)) {
    throw new Error(`BPP_CONFIG_HOME must be absolute: ${home}`);
  }
  return home;
}

function configKeysDir(env: NodeJS.ProcessEnv = process.env): string {
  return path.join(configHome(env), 'keys');
}

function steamLoginFile(env: NodeJS.ProcessEnv = process.env): string {
  return path.join(configKeysDir(env), 'steam-snapshot-login.json');
}

function withSteamLogin(env: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  if (env.STEAM_SNAPSHOT_LOGIN) return env;
  const loginFile = steamLoginFile(env);
  if (!fs.statSync(loginFile, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(
      "No Steam login: run 'just mod::steam-login', or set STEAM_SNAPSHOT_LOGIN."
    );
  }
  const next: NodeJS.ProcessEnv = {
    ...env,
    STEAM_SNAPSHOT_LOGIN: fs.readFileSync(loginFile, 'utf8').trimEnd()
  };
  if (!next.STEAM_BETA_PASSWORDS) {
    const betaFile = path.join(configKeysDir(env), 'steam-beta-passwords.json');
    if (fs.statSync(betaFile, { throwIfNoEntry: false })?.isFile()) {
      next.STEAM_BETA_PASSWORDS = fs.readFileSync(betaFile, 'utf8').trimEnd();
    }
  }
  return next;
}

async function cmdDecompile(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  const [channel, ...rest] = args;
  let outRoot: string;
  if (channel === 'online') outRoot = 'decompiled';
  else if (channel === 'staging' || channel === 'ptr')
    outRoot = `decompiled-v${channel}`;
  else {
    throw new Error(
      'Usage: just mod::decompile <online|staging|ptr> [DllName ...]'
    );
  }

  const managed = await resolveLockManagedPath(hostPlatform(), channel, {
    allowUnlocked: true,
    env
  });
  const probe = spawnSync('ilspycmd', ['--version'], {
    env: dotnetEnv(env),
    stdio: 'ignore'
  });
  if ((probe.error as NodeJS.ErrnoException | undefined)?.code === 'ENOENT') {
    step('Installing ilspycmd');
    const installCode = runDotnet(['tool', 'install', '-g', 'ilspycmd'], {
      env
    });
    if (installCode !== 0) return installCode;
  }

  const dlls =
    rest.length === 0
      ? ['Assembly-CSharp']
      : rest[0] === 'all'
        ? DECOMPILE_ALL
        : rest;
  for (const dll of dlls) {
    step(`Decompiling ${dll} to ${outRoot}/${dll}`);
    const result = spawnSync(
      'ilspycmd',
      ['-p', '-o', path.join(outRoot, dll), path.join(managed, `${dll}.dll`)],
      {
        cwd: MOD_ROOT,
        env: { ...dotnetEnv(env), DOTNET_ROLL_FORWARD: 'Major' },
        stdio: 'inherit'
      }
    );
    if (result.error) throw result.error;
    if ((result.status ?? 1) !== 0) return result.status ?? 1;
  }
  return 0;
}

async function cmdSnapshot(env: NodeJS.ProcessEnv): Promise<number> {
  const managed = env.BPP_MANAGED_PATH || steamManagedPath(MOD_ROOT, env);
  if (
    !managed ||
    !fs
      .statSync(path.join(managed, 'Assembly-CSharp.dll'), {
        throwIfNoEntry: false
      })
      ?.isFile()
  ) {
    throw new Error(
      'No mounted game install found. Install The Bazaar through Steam, or set BPP_MANAGED_PATH=/absolute/path/to/Managed.'
    );
  }
  const acf = locateAppmanifest(managed);
  if (!acf) {
    throw new Error(
      `Could not find appmanifest_1617400.acf above '${managed}'; a snapshot is captured from a Steam install.`
    );
  }
  const acfText = fs.readFileSync(acf, 'utf8');
  const branch = parseAcfSteamBranch(acfText);
  const buildid = parseAcfBuildId(acfText);
  return gameLibsMain(
    [
      'snapshot',
      '--managed',
      managed,
      '--buildid',
      buildid,
      '--steam-branch',
      branch
    ],
    { env }
  );
}

async function cmdSnapshotAndroid(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  if (args.length < 1 || args[0].startsWith('--')) {
    throw new Error(
      'Usage: just mod::snapshot-android <base.apk> [<split.apk> ...] [--interop <dir>] [--loader <zip> --loader-tag <tag>] [--patcher <tar.gz> --patcher-tag <tag>] [--cpp2il <file>] [--native-package <id>@<version>]'
    );
  }
  const apkFlags: string[] = [];
  let index = 0;
  while (index < args.length && !args[index].startsWith('--')) {
    apkFlags.push('--apk', args[index]);
    index++;
  }
  return gameLibsMain(['snapshot-android', ...apkFlags, ...args.slice(index)], {
    env
  });
}

function cmdSteamLogin(env: NodeJS.ProcessEnv): number {
  return runDotnet(
    [
      'run',
      '--project',
      'tools/SteamSnapshot/SteamSnapshot.csproj',
      '-c',
      'Release',
      '--no-launch-profile',
      '-v',
      'q',
      '--',
      'login',
      '--out',
      steamLoginFile(env)
    ],
    { env }
  );
}

async function cmdSteamStale(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  if (args.length !== 1) {
    throw new Error('Usage: just mod::steam-stale <online|staging|ptr>');
  }
  return gameLibsMain(['steam-stale', '--channel', args[0]], {
    env: withSteamLogin(env)
  });
}

async function cmdSteamCapture(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  if (args.length !== 2) {
    throw new Error(
      'Usage: just mod::steam-capture <macos|windows> <online|staging|ptr>'
    );
  }
  return gameLibsMain(
    ['steam-capture', '--platform', args[0], '--channel', args[1]],
    { env: withSteamLogin(env) }
  );
}

async function cmdPublish(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  if (args.length !== 2) {
    throw new Error(
      'Usage: just mod::publish <macos|windows> <online|staging|ptr>, or android <publisher|play>'
    );
  }
  return gameLibsMain(
    ['publish', '--platform', args[0], '--channel', args[1]],
    { env }
  );
}

async function cmdFetch(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  if (args.length !== 2) {
    throw new Error(
      'Usage: just mod::fetch <macos|windows> <online|staging|ptr>, or android <publisher|play>'
    );
  }
  const resolved = await resolveLockManagedPath(args[0], args[1], { env });
  process.stdout.write(`${resolved}\n`);
  return 0;
}

async function cmdLockCheck(env: NodeJS.ProcessEnv): Promise<number> {
  const managed = managedPath([], env);
  return gameLibsMain(
    managed ? ['lock-check', '--managed', managed] : ['lock-check'],
    { env }
  );
}

async function cmdManagedPath(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): Promise<number> {
  const props = collectProps(args);
  const pinned =
    propValue('ManagedPath', props) ||
    (await resolveLockManagedPath(hostPlatform(), 'online', { env }));
  const real = fs.realpathSync(pinned).split(path.sep).join('/');
  process.stdout.write(`${real}\n`);
  return 0;
}

async function main(argv: readonly string[]): Promise<number> {
  const [command, ...rest] = argv;
  switch (command) {
    case 'decompile':
      return cmdDecompile(rest, process.env);
    case 'snapshot':
      return cmdSnapshot(process.env);
    case 'snapshot-android':
      return cmdSnapshotAndroid(rest, process.env);
    case 'steam-login':
      return cmdSteamLogin(process.env);
    case 'steam-stale':
      return cmdSteamStale(rest, process.env);
    case 'steam-capture':
      return cmdSteamCapture(rest, process.env);
    case 'publish':
      return cmdPublish(rest, process.env);
    case 'fetch':
      return cmdFetch(rest, process.env);
    case 'lock-check':
      return cmdLockCheck(process.env);
    case 'managed-path':
      return cmdManagedPath(rest, process.env);
    default:
      throw new Error(
        "Usage: bun scripts/game.ts <command> [args...]; see 'just --list mod'."
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
