/**
 * Signs and bundles the installer for the host platform. Internal to the product
 * coordinator: `just release::build <platform>` (`bun release/cli.ts build`) holds
 * the build lock, prepares the Payload, then runs this script with
 * `BPP_RELEASE_LOCK_TOKEN`. It lives outside `scripts/release/` because signing
 * is not a Payload input.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { assertBuildOwner } from '../../release/payload.ts';
import { writeDeterministicZip } from '../../release/payload-zip.ts';
import {
  replayRecorderBundlePath,
  resolveBuildPlatform,
  tauriPlatform
} from '../../release/release-platforms.ts';
import type { TauriBuildPlatform } from '../../release/release-platforms.ts';

export const OFFICIAL_APPLE_TEAM_ID = '9Z44S3N293';
export const MACOS_GAME_APP_OVERLAY = 'TheBazaar.app';
export const SIGNING_STAGE_COMMAND = 'bun scripts/workspace.ts run signing';

const DEFAULT_INSTALLER_ROOT = path.resolve(import.meta.dirname, '..');

export interface CommandRunOptions {
  cwd?: string;
  env?: NodeJS.ProcessEnv;
}

export interface BundleDeps {
  installerRoot?: string;
  env?: NodeJS.ProcessEnv;
  hostPlatform?: NodeJS.Platform;
  bunVersion?: string;
  log?: (message: string) => void;
  errorLog?: (message: string) => void;
  run?: (
    command: string,
    args: readonly string[],
    options?: CommandRunOptions
  ) => void;
  capture?: (
    command: string,
    args: readonly string[],
    options?: CommandRunOptions
  ) => string;
  commandExists?: (command: string, env: NodeJS.ProcessEnv) => boolean;
  assertOwner?: (installerRoot: string, env: NodeJS.ProcessEnv) => void;
  assertFileExists?: (filePath: string, label: string) => void;
  prepareSignedMacosZip?: (resourceZip: string, env: NodeJS.ProcessEnv) => void;
  prepareSignedMacosBinary?: (
    resourceBinary: string,
    env: NodeJS.ProcessEnv
  ) => void;
}

export interface ResolvedBundleDeps {
  installerRoot: string;
  env: NodeJS.ProcessEnv;
  hostPlatform: NodeJS.Platform;
  bunVersion: string;
  log: (message: string) => void;
  errorLog: (message: string) => void;
  run: (
    command: string,
    args: readonly string[],
    options?: CommandRunOptions
  ) => void;
  capture: (
    command: string,
    args: readonly string[],
    options?: CommandRunOptions
  ) => string;
  commandExists: (command: string, env: NodeJS.ProcessEnv) => boolean;
  assertOwner: (installerRoot: string, env: NodeJS.ProcessEnv) => void;
  assertFileExists: (filePath: string, label: string) => void;
  prepareSignedMacosZip?: (resourceZip: string, env: NodeJS.ProcessEnv) => void;
  prepareSignedMacosBinary?: (
    resourceBinary: string,
    env: NodeJS.ProcessEnv
  ) => void;
}

function defaultRun(
  command: string,
  args: readonly string[],
  options: CommandRunOptions = {}
): void {
  execFileSync(command, args, {
    cwd: options.cwd,
    env: options.env,
    stdio: 'inherit'
  });
}

function defaultCapture(
  command: string,
  args: readonly string[],
  options: CommandRunOptions = {}
): string {
  return execFileSync(command, args, {
    cwd: options.cwd,
    env: options.env,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe']
  });
}

function defaultCaptureCombined(
  command: string,
  args: readonly string[],
  options: CommandRunOptions = {}
): string {
  // codesign -dvvv writes details to stderr while exiting 0.
  const result = spawnSync(command, args, {
    cwd: options.cwd,
    env: options.env,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe']
  });
  const combined = `${result.stdout ?? ''}${result.stderr ?? ''}`;
  if (result.error || result.status !== 0) {
    throw new Error(
      result.error?.message ||
        combined.trim() ||
        `${command} ${args.join(' ')} failed (${result.status})`
    );
  }
  return combined;
}

function defaultCommandExists(
  command: string,
  env: NodeJS.ProcessEnv
): boolean {
  const pathEnv = env.PATH ?? process.env.PATH ?? '';
  const extensions =
    process.platform === 'win32'
      ? (env.PATHEXT ?? '.EXE;.CMD;.BAT;.COM').split(';')
      : [''];
  for (const dir of pathEnv.split(path.delimiter)) {
    if (!dir) continue;
    for (const ext of extensions) {
      const candidate = path.join(dir, `${command}${ext}`);
      if (fs.statSync(candidate, { throwIfNoEntry: false })?.isFile()) {
        return true;
      }
    }
  }
  return false;
}

export function assertFile(filePath: string, label: string): void {
  if (!fs.statSync(filePath, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(`Missing ${label}: ${filePath}`);
  }
}

function resolveDeps(deps: BundleDeps = {}): ResolvedBundleDeps {
  const env = deps.env ?? process.env;
  const capture = deps.capture ?? defaultCapture;
  return {
    installerRoot: deps.installerRoot ?? DEFAULT_INSTALLER_ROOT,
    env,
    hostPlatform: deps.hostPlatform ?? process.platform,
    bunVersion:
      deps.bunVersion ??
      (process.versions as Record<string, string | undefined>).bun ??
      capture('bun', ['--version'], { env }).trim(),
    log: deps.log ?? ((message) => console.log(message)),
    errorLog: deps.errorLog ?? ((message) => console.error(message)),
    run: deps.run ?? defaultRun,
    capture,
    commandExists: deps.commandExists ?? defaultCommandExists,
    assertOwner:
      deps.assertOwner ??
      ((installerRoot, currentEnv) =>
        assertBuildOwner(installerRoot, currentEnv.BPP_RELEASE_LOCK_TOKEN)),
    assertFileExists: deps.assertFileExists ?? assertFile,
    prepareSignedMacosZip: deps.prepareSignedMacosZip,
    prepareSignedMacosBinary: deps.prepareSignedMacosBinary
  };
}

function assertCommand(
  name: string,
  hint: string,
  deps: ResolvedBundleDeps
): void {
  if (!deps.commandExists(name, deps.env)) {
    throw new Error(hint ? `${name} not found. ${hint}` : `${name} not found.`);
  }
}

export function assertPinnedBunVersion(
  installerRoot: string,
  actualVersion: string
): void {
  const packageJson = JSON.parse(
    fs.readFileSync(path.join(installerRoot, 'package.json'), 'utf8')
  ) as { engines?: { bun?: string } };
  const expected = packageJson.engines?.bun;
  if (expected && actualVersion.trim() !== expected) {
    throw new Error(
      `Bun ${actualVersion} is unsupported; package.json requires ${expected}`
    );
  }
}

function trimTrailingNewlines(value: string): string {
  return value.replace(/[\r\n]+$/, '');
}

function signingSecretFile(
  file: string,
  env: NodeJS.ProcessEnv
): string | null {
  const dir = env.BPP_SIGNING_SECRETS_DIR;
  return dir ? path.join(dir, file) : null;
}

function invokeStep(
  label: string,
  command: string,
  args: readonly string[],
  deps: ResolvedBundleDeps,
  options: CommandRunOptions = {}
): void {
  deps.log(`==> ${label}`);
  deps.run(command, args, {
    cwd: options.cwd ?? deps.installerRoot,
    env: options.env ?? deps.env
  });
}

export function loadSecretEnv(
  name: string,
  file: string,
  depsInput: BundleDeps = {}
): string {
  const deps = resolveDeps(depsInput);
  const existing = deps.env[name];
  const filePath = signingSecretFile(file, deps.env);
  let value: string;
  if (existing) {
    deps.log(`==> Reusing existing ${name} from environment`);
    value = existing;
  } else if (
    filePath &&
    fs.statSync(filePath, { throwIfNoEntry: false })?.isFile()
  ) {
    deps.log(`==> Loading ${name} from BPP_SIGNING_SECRETS_DIR`);
    value = fs.readFileSync(filePath, 'utf8');
  } else {
    throw new Error(
      `Missing ${name}.\nSet ${name} or stage ${file} in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}).`
    );
  }
  value = trimTrailingNewlines(value);
  if (!value) {
    throw new Error(
      `Empty ${name}.\nSet ${name} or stage ${file} in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}).`
    );
  }
  deps.env[name] = value;
  return value;
}

export function loadUpdaterSigningEnv(depsInput: BundleDeps = {}): void {
  const deps = resolveDeps(depsInput);
  loadSecretEnv('TAURI_SIGNING_PRIVATE_KEY', 'tauri-updater.key', deps);
  // The updater key has no password; the Tauri signer still needs the variable set.
  deps.env.TAURI_SIGNING_PRIVATE_KEY_PASSWORD =
    deps.env.TAURI_SIGNING_PRIVATE_KEY_PASSWORD ?? '';
}

function detectDeveloperIdApplicationIdentities(
  deps: ResolvedBundleDeps
): string[] {
  let stdout = '';
  try {
    stdout = deps.capture(
      'security',
      ['find-identity', '-v', '-p', 'codesigning'],
      { cwd: deps.installerRoot, env: deps.env }
    );
  } catch {
    return [];
  }
  const found = new Set<string>();
  for (const line of stdout.split(/\r?\n/)) {
    const match = /"(Developer ID Application: [^"]+)"/.exec(line);
    if (match) found.add(match[1]);
  }
  return [...found].sort();
}

export function loadAppleSigningIdentityEnv(
  depsInput: BundleDeps = {}
): string {
  const deps = resolveDeps(depsInput);
  const existing = deps.env.APPLE_SIGNING_IDENTITY;
  const filePath = signingSecretFile('apple-signing-identity', deps.env);
  let value: string;

  if (existing) {
    deps.log('==> Reusing existing APPLE_SIGNING_IDENTITY from environment');
    value = existing;
  } else if (
    filePath &&
    fs.statSync(filePath, { throwIfNoEntry: false })?.isFile()
  ) {
    deps.log('==> Loading APPLE_SIGNING_IDENTITY from BPP_SIGNING_SECRETS_DIR');
    value = fs.readFileSync(filePath, 'utf8');
  } else {
    const identities = detectDeveloperIdApplicationIdentities(deps);
    if (identities.length === 1) {
      deps.log('==> Auto-detected APPLE_SIGNING_IDENTITY from keychain');
      value = identities[0];
    } else if (identities.length === 0) {
      throw new Error(
        `Missing APPLE_SIGNING_IDENTITY.\nSet APPLE_SIGNING_IDENTITY, stage apple-signing-identity in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}), or install a Developer ID Application certificate.`
      );
    } else {
      throw new Error(
        `Multiple Developer ID Application identities found.\nSet APPLE_SIGNING_IDENTITY or stage apple-signing-identity in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}) with the exact identity to use.\n${identities.join('\n')}`
      );
    }
  }

  value = trimTrailingNewlines(value);
  if (!value) {
    throw new Error(
      `Empty APPLE_SIGNING_IDENTITY.\nSet APPLE_SIGNING_IDENTITY or stage apple-signing-identity in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}).`
    );
  }
  deps.env.APPLE_SIGNING_IDENTITY = value;
  return value;
}

export function loadAppleApiKeyPathEnv(depsInput: BundleDeps = {}): string {
  const deps = resolveDeps(depsInput);
  const existing = deps.env.APPLE_API_KEY_PATH;
  const filePath = signingSecretFile('apple-api-key-path', deps.env);
  const apiKey = deps.env.APPLE_API_KEY ?? '';
  const inferredPath = signingSecretFile(`AuthKey_${apiKey}.p8`, deps.env);
  let value: string;

  if (existing) {
    deps.log('==> Reusing existing APPLE_API_KEY_PATH from environment');
    value = existing;
  } else if (
    filePath &&
    fs.statSync(filePath, { throwIfNoEntry: false })?.isFile()
  ) {
    deps.log('==> Loading APPLE_API_KEY_PATH from BPP_SIGNING_SECRETS_DIR');
    value = fs.readFileSync(filePath, 'utf8');
  } else if (
    inferredPath &&
    fs.statSync(inferredPath, { throwIfNoEntry: false })?.isFile()
  ) {
    deps.log('==> Inferring APPLE_API_KEY_PATH from BPP_SIGNING_SECRETS_DIR');
    value = inferredPath;
  } else {
    throw new Error(
      `Missing APPLE_API_KEY_PATH.\nSet APPLE_API_KEY_PATH, or stage apple-api-key-path or AuthKey_${apiKey}.p8 in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}).`
    );
  }

  value = trimTrailingNewlines(value);
  if (!value) {
    throw new Error(
      `Empty APPLE_API_KEY_PATH.\nSet APPLE_API_KEY_PATH or stage apple-api-key-path in BPP_SIGNING_SECRETS_DIR (${SIGNING_STAGE_COMMAND}).`
    );
  }
  if (!path.isAbsolute(value)) {
    throw new Error(`APPLE_API_KEY_PATH must be absolute: ${value}`);
  }
  deps.env.APPLE_API_KEY_PATH = value;
  deps.assertFileExists(value, 'Apple API key file');
  return value;
}

export function loadMacosDeveloperIdEnv(depsInput: BundleDeps = {}): void {
  const deps = resolveDeps(depsInput);
  loadAppleSigningIdentityEnv(deps);
  loadSecretEnv('APPLE_API_ISSUER', 'apple-api-issuer', deps);
  loadSecretEnv('APPLE_API_KEY', 'apple-api-key', deps);
  loadAppleApiKeyPathEnv(deps);
}

export function installDependencies(depsInput: BundleDeps = {}): void {
  const deps = resolveDeps(depsInput);
  const candidates = [deps.installerRoot, path.dirname(deps.installerRoot)];
  const lockDir = candidates.find((dir) =>
    fs.statSync(path.join(dir, 'bun.lock'), { throwIfNoEntry: false })?.isFile()
  );
  if (!lockDir) {
    throw new Error(
      'bun.lock is required for a reproducible dependency install; refusing an unlocked install.'
    );
  }
  invokeStep(
    'Installing Bun dependencies',
    'bun',
    ['install', '--frozen-lockfile', '--cwd', lockDir],
    deps
  );
}

export function ensureRequiredRustTargets(
  platform: TauriBuildPlatform,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  const required = tauriPlatform(platform).rustTarget;
  if (!required) return;
  const installed = deps
    .capture('rustup', ['target', 'list', '--installed'], {
      cwd: deps.installerRoot,
      env: deps.env
    })
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  if (!installed.includes(required)) {
    throw new Error(
      `Missing Rust target: ${required}\nRun: rustup target add ${required}`
    );
  }
}

function isMachoFile(filePath: string, deps: ResolvedBundleDeps): boolean {
  const output = deps.capture('file', [filePath], {
    cwd: deps.installerRoot,
    env: deps.env
  });
  return output.includes('Mach-O');
}

function codesignDetails(codePath: string, deps: ResolvedBundleDeps): string {
  if (deps.capture !== defaultCapture) {
    return deps.capture('codesign', ['-dvvv', codePath], {
      cwd: deps.installerRoot,
      env: deps.env
    });
  }
  return defaultCaptureCombined('codesign', ['-dvvv', codePath], {
    cwd: deps.installerRoot,
    env: deps.env
  });
}

export function assertOfficialCodesignTeamId(
  codePath: string,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  const details = codesignDetails(codePath, deps);
  const match = /^TeamIdentifier=(.+)$/m.exec(details);
  const actualTeamId = match?.[1]?.trim();
  if (actualTeamId !== OFFICIAL_APPLE_TEAM_ID) {
    throw new Error(
      `Official macOS release code must use TeamIdentifier=${OFFICIAL_APPLE_TEAM_ID}.\nFound TeamIdentifier=${actualTeamId ?? 'missing'} for ${codePath}`
    );
  }
}

export function assertAdHocReplayRecorderInput(
  pluginBundle: string,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  try {
    deps.run(
      'codesign',
      ['--verify', '--deep', '--strict', '--verbose=2', pluginBundle],
      { cwd: deps.installerRoot, env: deps.env }
    );
  } catch {
    throw new Error(
      `Replay recorder plugin input has an invalid ad-hoc signature: ${pluginBundle}`
    );
  }
  const details = codesignDetails(pluginBundle, deps);
  if (
    !/^Signature=adhoc$/m.test(details) ||
    !/^TeamIdentifier=not set$/m.test(details)
  ) {
    throw new Error(
      'Replay recorder plugin input must be ad-hoc signed with no TeamIdentifier.'
    );
  }
}

function signMacosResourceBinary(
  binaryPath: string,
  relativePath: string,
  deps: ResolvedBundleDeps
): void {
  const identity = deps.env.APPLE_SIGNING_IDENTITY;
  if (!identity) throw new Error('Missing APPLE_SIGNING_IDENTITY.');
  invokeStep(
    `Signing macOS resource binary ${relativePath}`,
    'codesign',
    [
      '--force',
      '--options',
      'runtime',
      '--timestamp',
      '--sign',
      identity,
      binaryPath
    ],
    deps
  );
}

function collectFiles(directory: string): string[] {
  const found: string[] = [];
  if (!fs.statSync(directory, { throwIfNoEntry: false })?.isDirectory()) {
    return found;
  }
  const walk = (dir: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile()) found.push(full);
    }
  };
  walk(directory);
  return found.sort();
}

function signMacosBundleInsideOut(
  payloadDir: string,
  bundle: string,
  kind: 'app' | 'plugin',
  deps: ResolvedBundleDeps
): void {
  const identity = deps.env.APPLE_SIGNING_IDENTITY;
  if (!identity) throw new Error('Missing APPLE_SIGNING_IDENTITY.');
  const relativePath = path
    .relative(payloadDir, bundle)
    .split(path.sep)
    .join('/');
  for (const binaryPath of collectFiles(path.join(bundle, 'Contents'))) {
    if (isMachoFile(binaryPath, deps)) {
      const rel = path
        .relative(payloadDir, binaryPath)
        .split(path.sep)
        .join('/');
      signMacosResourceBinary(binaryPath, rel, deps);
    }
  }
  invokeStep(
    `Signing macOS resource ${kind} bundle ${relativePath}`,
    'codesign',
    [
      '--force',
      '--options',
      'runtime',
      '--timestamp',
      '--sign',
      identity,
      bundle
    ],
    deps
  );
  invokeStep(
    `Verifying macOS resource ${kind} bundle ${relativePath}`,
    'codesign',
    ['--verify', '--deep', '--strict', '--verbose=2', bundle],
    deps
  );
}

function walkPayloadForSigning(payloadDir: string): {
  looseFiles: string[];
  pluginBundles: string[];
  appBundles: string[];
} {
  const looseFiles: string[] = [];
  const pluginBundles: string[] = [];
  const appBundles: string[] = [];
  const walk = (dir: string) => {
    for (const entry of fs
      .readdirSync(dir, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name))) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name.endsWith('.bundle')) {
          pluginBundles.push(full);
          continue;
        }
        if (entry.name.endsWith('.app')) {
          const rel = path.relative(payloadDir, full).split(path.sep).join('/');
          // TheBazaar.app is an install-path overlay, not a complete .app bundle.
          if (rel === MACOS_GAME_APP_OVERLAY) {
            walk(full);
          } else {
            appBundles.push(full);
          }
          continue;
        }
        walk(full);
      } else if (entry.isFile()) {
        looseFiles.push(full);
      }
    }
  };
  walk(payloadDir);
  return { looseFiles, pluginBundles, appBundles };
}

export function prepareSignedMacosResourceZip(
  resourceZip: string,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  assertCommand('ditto', 'Install macOS command line tools first.', deps);
  assertCommand('file', 'Install file first.', deps);
  assertCommand('codesign', 'Install Xcode command line tools first.', deps);
  assertCommand('plutil', 'Install macOS command line tools first.', deps);
  assertCommand('spctl', 'Install macOS command line tools first.', deps);
  assertCommand('xcrun', 'Install Xcode command line tools first.', deps);
  deps.assertFileExists(resourceZip, 'macOS resource zip');

  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bundle-sign-'));
  const payloadDir = path.join(tempDir, 'payload');
  const signedZip = path.join(tempDir, 'BepInEx.zip');
  const signedManifest = path.join(tempDir, 'BepInEx.zip.manifest.json');
  const resourceManifest = `${resourceZip}.manifest.json`;
  fs.mkdirSync(payloadDir, { recursive: true });

  try {
    const replayRecorderBundle = replayRecorderBundlePath('macos');
    invokeStep(
      'Extracting macOS resource zip for signing',
      'ditto',
      ['-x', '-k', resourceZip, payloadDir],
      deps
    );
    assertAdHocReplayRecorderInput(
      path.join(payloadDir, replayRecorderBundle),
      deps
    );

    const { looseFiles, pluginBundles, appBundles } =
      walkPayloadForSigning(payloadDir);
    for (const filePath of looseFiles) {
      if (isMachoFile(filePath, deps)) {
        const rel = path
          .relative(payloadDir, filePath)
          .split(path.sep)
          .join('/');
        signMacosResourceBinary(filePath, rel, deps);
      }
    }
    for (const pluginBundle of pluginBundles) {
      const rel = path
        .relative(payloadDir, pluginBundle)
        .split(path.sep)
        .join('/');
      if (rel === replayRecorderBundle) {
        assertAdHocReplayRecorderInput(pluginBundle, deps);
      }
      signMacosBundleInsideOut(payloadDir, pluginBundle, 'plugin', deps);
      if (rel === replayRecorderBundle) {
        assertOfficialCodesignTeamId(
          path.join(
            pluginBundle,
            'Contents/MacOS/GfxPluginBppReplayVideoToolbox'
          ),
          deps
        );
        assertOfficialCodesignTeamId(pluginBundle, deps);
      }
    }
    for (const appBundle of appBundles) {
      signMacosBundleInsideOut(payloadDir, appBundle, 'app', deps);
    }

    deps.log('==> Repacking signed macOS resource zip');
    writeDeterministicZip({
      sourceDir: payloadDir,
      outputPath: signedZip,
      manifestPath: signedManifest,
      platform: 'macos'
    });

    deps.log(
      '==> Replacing macOS resource zip and checksum manifest with signed copies'
    );
    fs.copyFileSync(signedZip, resourceZip);
    fs.copyFileSync(signedManifest, resourceManifest);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
}

export function prepareSignedMacosResourceBinary(
  resourceBinary: string,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  assertCommand('file', 'Install file first.', deps);
  assertCommand('codesign', 'Install Xcode command line tools first.', deps);
  deps.assertFileExists(resourceBinary, 'macOS resource binary');
  if (!isMachoFile(resourceBinary, deps)) {
    throw new Error(`macOS resource binary is not Mach-O: ${resourceBinary}`);
  }
  const resourcesRoot = path.join(deps.installerRoot, 'src-tauri', 'resources');
  const relative = path.relative(resourcesRoot, resourceBinary);
  const label = relative.startsWith('..')
    ? resourceBinary
    : relative.split(path.sep).join('/');
  signMacosResourceBinary(resourceBinary, label, deps);
  assertOfficialCodesignTeamId(resourceBinary, deps);
}

export function runReleasePrechecks(
  platform: TauriBuildPlatform,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  deps.log('==> Checking product build ownership');
  deps.assertOwner(deps.installerRoot, deps.env);
  invokeStep(
    'Running authoritative release verification',
    'bun',
    ['run', 'verify', '--release-platform', platform],
    deps
  );
}

export function buildProd(
  platform: TauriBuildPlatform,
  depsInput: BundleDeps = {}
): void {
  const deps = resolveDeps(depsInput);
  const spec = tauriPlatform(platform);
  const config = path.join(deps.installerRoot, spec.tauriConfig);
  const resourceZip = path.join(deps.installerRoot, spec.resourceZip);
  const bundleOutput = path.join(deps.installerRoot, spec.installerDir);
  const bundleCleanupPath = path.join(
    deps.installerRoot,
    spec.bundleCleanupDir
  );
  const releaseBinary = path.join(deps.installerRoot, spec.releaseBinary);
  const releaseConfig = path.join(
    deps.installerRoot,
    'src-tauri',
    'tauri.release.conf.json'
  );
  const macosTrampolineStub = path.join(
    deps.installerRoot,
    'src-tauri',
    'resources',
    'Trampoline',
    'macos',
    'bpp_launcher'
  );

  deps.assertFileExists(config, `${platform} Tauri config`);
  deps.assertFileExists(resourceZip, `${platform} resource zip`);
  deps.assertFileExists(releaseConfig, 'release Tauri config');

  if (
    fs.statSync(bundleCleanupPath, { throwIfNoEntry: false })?.isDirectory()
  ) {
    deps.log(`==> Removing stale ${platform} bundle artifacts`);
    fs.rmSync(bundleCleanupPath, { recursive: true, force: true });
  }

  const targetArgs = spec.rustTarget ? ['--target', spec.rustTarget] : [];
  const buildArgs = [
    'run',
    'tauri',
    'build',
    '--no-bundle',
    '--config',
    config,
    '--config',
    releaseConfig,
    ...targetArgs
  ];
  const bundleArgs = [
    'run',
    'tauri',
    'bundle',
    '--bundles',
    spec.bundleTargets,
    '--config',
    config,
    '--config',
    releaseConfig,
    ...targetArgs
  ];

  invokeStep(`Building ${platform} app binary`, 'bun', buildArgs, deps);

  if (platform === 'macos') {
    if (deps.prepareSignedMacosZip) {
      deps.prepareSignedMacosZip(resourceZip, deps.env);
    } else {
      prepareSignedMacosResourceZip(resourceZip, deps);
    }
    if (deps.prepareSignedMacosBinary) {
      deps.prepareSignedMacosBinary(macosTrampolineStub, deps.env);
    } else {
      prepareSignedMacosResourceBinary(macosTrampolineStub, deps);
    }
  }

  invokeStep(`Bundling ${platform} installer`, 'bun', bundleArgs, deps);

  deps.log('');
  deps.log('Build complete.');
  deps.log(`Binary:  ${releaseBinary}`);
  deps.log(`Bundle:  ${bundleOutput}`);
}

export function cliMain(
  argv: readonly string[],
  depsInput: BundleDeps = {}
): number {
  const deps = resolveDeps(depsInput);
  if (argv.length > 0) {
    deps.errorLog(
      'Error: bundle.ts takes no arguments; run just release::build <platform>.'
    );
    return 2;
  }
  if (!deps.env.BPP_RELEASE_LOCK_TOKEN) {
    deps.errorLog(
      'Error: bundle.ts runs inside the product build lock; run just release::build <platform>.'
    );
    return 1;
  }

  try {
    deps.assertOwner(deps.installerRoot, deps.env);
    assertCommand(
      'bun',
      'Install the Bun version in package.json first.',
      deps
    );
    assertPinnedBunVersion(deps.installerRoot, deps.bunVersion);
    assertCommand('cargo', 'Install Rust toolchain first.', deps);

    const platform = resolveBuildPlatform(deps.hostPlatform);
    if (!platform) {
      throw new Error(`Unsupported host platform: ${deps.hostPlatform}`);
    }
    if (platform === 'macos') {
      assertCommand(
        'rustup',
        'Install rustup first so the macOS Rust target can be managed.',
        deps
      );
    }

    installDependencies(deps);
    loadUpdaterSigningEnv(deps);
    if (platform === 'macos') {
      loadMacosDeveloperIdEnv(deps);
    }
    runReleasePrechecks(platform, deps);
    ensureRequiredRustTargets(platform, deps);
    buildProd(platform, deps);
    return 0;
  } catch (error) {
    deps.errorLog(
      `Error: ${error instanceof Error ? error.message : String(error)}`
    );
    return 1;
  }
}

if (import.meta.main) {
  process.exitCode = cliMain(process.argv.slice(2));
}
