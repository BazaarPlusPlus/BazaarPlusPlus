import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

export const MOD_ROOT = path.resolve(import.meta.dirname, '..');
export const WORKSPACE_ROOT = path.resolve(MOD_ROOT, '..');

export type HostPlatform = 'macos' | 'windows';

const useColor = (): boolean =>
  Boolean(process.stderr.isTTY) && !process.env.NO_COLOR;

export function step(message: string): void {
  const line = useColor()
    ? `\x1b[0;36m== ${message} ==\x1b[0m\n`
    : `== ${message} ==\n`;
  process.stderr.write(line);
}

export function ok(message: string): void {
  const line = useColor() ? `\x1b[0;32m${message}\x1b[0m\n` : `${message}\n`;
  process.stderr.write(line);
}

export function err(message: string): void {
  const line = useColor() ? `\x1b[0;31m${message}\x1b[0m\n` : `${message}\n`;
  process.stderr.write(line);
}

export function dotnetEnv(
  env: NodeJS.ProcessEnv = process.env
): NodeJS.ProcessEnv {
  return {
    ...env,
    DOTNET_SYSTEM_NET_DISABLEIPV6: env.DOTNET_SYSTEM_NET_DISABLEIPV6 || '1'
  };
}

export function hostPlatform(
  platform: NodeJS.Platform = process.platform
): HostPlatform {
  if (platform === 'darwin') return 'macos';
  if (platform === 'win32') return 'windows';
  throw new Error(`Unsupported platform: ${platform}`);
}

export function collectProps(args: readonly string[]): string[] {
  const props: string[] = [];
  for (const arg of args) {
    if (arg.startsWith('-p:') || arg.startsWith('--property:')) {
      props.push(arg);
    } else {
      throw new Error(`Expected -p:Name=Value, got '${arg}'`);
    }
  }
  return props;
}

export function propValue(name: string, args: readonly string[]): string {
  const prefixA = `-p:${name}=`;
  const prefixB = `--property:${name}=`;
  let value = '';
  for (const arg of args) {
    if (arg.startsWith(prefixA)) value = arg.slice(prefixA.length);
    else if (arg.startsWith(prefixB)) value = arg.slice(prefixB.length);
  }
  return value;
}

export function desktopDataRoot(
  gameDir: string,
  platform: NodeJS.Platform = process.platform
): string {
  const old = path.join(gameDir, 'BazaarPlusPlusV6');
  const parent = path.dirname(gameDir);
  const base = path.basename(gameDir);
  const layout = `${path.basename(parent)}/${base}`;
  const isWinStable =
    platform === 'win32' && layout.toLowerCase() === 'game/buildx64';
  const isMacStable =
    platform === 'darwin' &&
    base === 'buildarm64' &&
    fs
      .statSync(path.join(gameDir, 'TheBazaar_ARM64.app'), {
        throwIfNoEntry: false
      })
      ?.isDirectory();
  if (isWinStable || isMacStable) {
    const stable = path.join(parent, 'BazaarPlusPlusV6');
    if (
      fs.statSync(stable, { throwIfNoEntry: false })?.isDirectory() ||
      (!fs.existsSync(stable) &&
        !fs.statSync(old, { throwIfNoEntry: false })?.isDirectory())
    ) {
      return stable;
    }
  }
  return old;
}

export function discoveredProp(
  name: string,
  modRoot = MOD_ROOT,
  env: NodeJS.ProcessEnv = process.env
): string {
  return execFileSync(
    'dotnet',
    ['msbuild', 'build/ManagedPath.props', '-nologo', `-getProperty:${name}`],
    {
      cwd: modRoot,
      env: dotnetEnv(env),
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'inherit']
    }
  ).replace(/[\r\n]+$/, '');
}

export function steamManagedPath(
  modRoot = MOD_ROOT,
  env: NodeJS.ProcessEnv = process.env
): string {
  return discoveredProp('BppSteamManagedPath', modRoot, env);
}

export function managedPath(
  args: readonly string[] = [],
  env: NodeJS.ProcessEnv = process.env,
  modRoot = MOD_ROOT
): string {
  const explicit = propValue('ManagedPath', args);
  if (explicit) return explicit;
  if (env.BPP_MANAGED_PATH) return env.BPP_MANAGED_PATH;
  return discoveredProp('ManagedPath', modRoot, env);
}

export function managedProps(
  args: readonly string[] = [],
  env: NodeJS.ProcessEnv = process.env
): string[] {
  if (env.BPP_MANAGED_PATH && !propValue('ManagedPath', args)) {
    return [`-p:ManagedPath=${env.BPP_MANAGED_PATH}`];
  }
  return [];
}

export function gameRoot(
  env: NodeJS.ProcessEnv = process.env,
  modRoot = MOD_ROOT
): string {
  if (env.BPP_GAME_ROOT) return env.BPP_GAME_ROOT;
  return discoveredProp('GamePath', modRoot, env);
}

export function locateAppmanifest(managedDir: string): string | null {
  let dir = path.resolve(managedDir);
  for (let i = 0; i < 10; i++) {
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
    const candidate = path.join(dir, 'appmanifest_1617400.acf');
    if (fs.statSync(candidate, { throwIfNoEntry: false })?.isFile()) {
      return candidate;
    }
  }
  return null;
}

export function parseAcfSteamBranch(acfText: string): string {
  const mounted = /"MountedConfig"\s*\{([\s\S]*?)^\t\}/m.exec(acfText);
  if (!mounted) return 'public';
  const beta = /"BetaKey"\s+"([^"]*)"/.exec(mounted[1]);
  return beta?.[1] || 'public';
}

export function parseAcfBuildId(acfText: string): string {
  const match = /"buildid"\s+"([^"]*)"/.exec(acfText);
  return match?.[1] ?? '';
}

export function runDotnet(
  args: readonly string[],
  options: { cwd?: string; env?: NodeJS.ProcessEnv } = {}
): number {
  const result = spawnSync('dotnet', args, {
    cwd: options.cwd ?? MOD_ROOT,
    env: dotnetEnv(options.env ?? process.env),
    stdio: 'inherit'
  });
  if (result.error) throw result.error;
  return result.status ?? 1;
}
