import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import {
  SIGNING_STAGE_CONFIG_FILES,
  loadGithubSecrets
} from '../release/github-secrets.ts';

// Load after setup installs root dependencies; --help works on a fresh clone.
const require = createRequire(import.meta.url);
const toml = (): typeof import('smol-toml') => require('smol-toml');

export const root = path.resolve(import.meta.dirname, '..');
export const CONFIG = 'config.toml';
const KEYS = 'keys';
const STAGING = '.signing-';
const serverKeys = [
  'R2_PRESIGN_ACCESS_KEY_ID',
  'R2_PRESIGN_SECRET_ACCESS_KEY',
  'BUNDLE_SYNC_TOKEN',
  'BAZAARDB_DELIVERY_TOKEN'
];
const cloudflareKeys = ['CLOUDFLARE_API_TOKEN', 'CLOUDFLARE_ACCOUNT_ID'];
// Keep credentials in their owning sections. R2 derives its S3 credentials from the shared Cloudflare operator token.
export interface ProjectedKey {
  from: string;
  key: string;
  as?: string;
}

export interface SectionSpec {
  profile: string;
  projection?: string;
  absolute?: string[];
  projected?: ProjectedKey[];
  keys?: string[];
  inject?: string[];
  /** release/github-secrets.json items whose keys/ file the profile exports as is. */
  injectFiles?: string[];
  stage?: Record<string, string>;
  optional?: true | string[];
  required?: Record<string, string[]>;
}

export interface ConfigSection {
  values: Record<string, string>;
}

export interface ParsedConfig {
  sections: Map<string, ConfigSection>;
}

export interface ProjectionChange {
  relative: string;
  bytes: Buffer;
  changed: boolean;
}

export interface ResolvedSigning {
  values: Record<string, string>;
  appleKeyPath: string;
  updaterKey: string;
}

interface ProjectionState {
  configHome?: string;
  files?: Record<string, string>;
}

interface SectionTable extends Record<string, SectionSpec> {
  signing: SectionSpec & { stage: Record<string, string> };
}

export const sections: SectionTable = {
  server: {
    profile: 'server',
    projection: 'bazaarplusplus-server/.dev.vars',
    keys: serverKeys,
    projected: [{ from: 'bot', key: 'BOT_STATS_TOKEN' }],
    required: { server: serverKeys }
  },
  analyzer: {
    profile: 'analyzer',
    projection: 'bazaarplusplus-analyzer/.env',
    keys: [
      'BPP_DATA_ROOT',
      'BPP_MAX_RUN_SECONDS',
      'BPP_DUCKDB_MEMORY_LIMIT',
      'BPP_DUCKDB_THREADS'
    ],
    optional: true,
    // Every clone receives the same text, so a relative path would move.
    absolute: ['BPP_DATA_ROOT'],
    projected: [
      { from: 'server', key: 'BUNDLE_SYNC_TOKEN', as: 'BPP_BUNDLE_SYNC_TOKEN' },
      { from: 'cloudflare', key: 'CLOUDFLARE_API_TOKEN' },
      { from: 'cloudflare', key: 'CLOUDFLARE_ACCOUNT_ID' }
    ]
  },
  signing: {
    profile: 'signing',
    keys: [
      'APPLE_SIGNING_IDENTITY',
      'APPLE_API_ISSUER',
      'APPLE_API_KEY',
      'APPLE_API_KEY_PATH',
      'APPLE_CERTIFICATE_PASSWORD',
      'ANDROID_KEYSTORE_PASSWORD',
      'ANDROID_KEY_ALIAS'
    ],
    // Local Apple signing uses Keychain. APPLE_CERTIFICATE_PASSWORD only travels to GitHub with the p12.
    stage: { ...SIGNING_STAGE_CONFIG_FILES },
    optional: [
      'APPLE_API_KEY_PATH',
      'ANDROID_KEYSTORE_PASSWORD',
      'ANDROID_KEY_ALIAS'
    ]
  },
  machine: {
    profile: 'mod',
    inject: ['BPP_MANAGED_PATH', 'BPP_GAME_ROOT'],
    optional: true
  },
  game: {
    profile: 'game',
    inject: ['BPP_GAME_EMAIL', 'BPP_GAME_PASSWORD'],
    optional: true
  },
  cloudflare: {
    profile: 'release',
    keys: cloudflareKeys,
    inject: cloudflareKeys,
    injectFiles: ['MAINLAND_MIRROR_SSH_KEY'],
    required: { release: cloudflareKeys }
  },
  bot: {
    profile: 'bot',
    projection: 'bazaarplusplus-bot/.dev.vars',
    keys: ['BOT_STATS_TOKEN'],
    optional: true,
    projected: [
      { from: 'qqbot', key: 'QQ_BOT_APP_ID' },
      { from: 'qqbot', key: 'QQ_BOT_APP_SECRET' }
    ]
  },
  qqbot: {
    profile: 'qqbot',
    inject: ['QQ_BOT_APP_ID', 'QQ_BOT_APP_SECRET'],
    optional: true
  }
};
for (const [name, spec] of Object.entries(sections)) {
  if ([spec.projection, spec.inject, spec.stage].filter(Boolean).length !== 1)
    throw new Error(`[${name}] needs exactly one delivery`);
  for (const { from, key } of spec.projected || [])
    if (!sectionKeys(sections[from]).includes(key))
      throw new Error(`[${name}] projects an unknown key ${from} ${key}`);
}
export const profiles = Object.values(sections).map((spec) => spec.profile);
export function sectionKeys(spec: SectionSpec): string[] {
  return spec.keys || spec.inject || Object.values(spec.stage || {});
}
const keyMaterial = (name: string): boolean =>
  name === 'tauri-updater.key' ||
  name === 'tauri-updater.key.pub' ||
  name === 'android-release.p12' ||
  /^AuthKey_[A-Za-z0-9]+\.p8$/.test(name);
const digest = (bytes: Buffer): string =>
  createHash('sha256').update(bytes).digest('hex');

function realLocation(file: string): string {
  try {
    return fs.realpathSync(file);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
    // A dangling link is not a new directory.
    if (fs.lstatSync(file, { throwIfNoEntry: false }))
      throw new Error(`Dangling link: ${file}`);
    return path.join(realLocation(path.dirname(file)), path.basename(file));
  }
}

function inside(parent: string, child: string): boolean {
  const relative = path.relative(parent, child);
  return (
    relative === '' ||
    (!relative.startsWith(`..${path.sep}`) &&
      relative !== '..' &&
      !path.isAbsolute(relative))
  );
}

export function configHome(env: NodeJS.ProcessEnv = process.env): string {
  const directory =
    env.BPP_CONFIG_HOME || path.join(os.homedir(), '.config', 'bazaarplusplus');
  if (!path.isAbsolute(directory))
    throw new Error('BPP_CONFIG_HOME must be absolute');
  return realLocation(directory);
}

export function assertExternal(
  home: string,
  repositories: readonly string[] = [root]
): void {
  for (const repository of repositories) {
    if (inside(realLocation(repository), realLocation(home))) {
      throw new Error('BPP_CONFIG_HOME must be outside the checkout');
    }
  }
}

function privateDirectory(directory: string): void {
  fs.mkdirSync(directory, { recursive: true, mode: 0o700 });
  if (process.platform !== 'win32') fs.chmodSync(directory, 0o700);
}

function regularFile(file: string): fs.Stats | undefined {
  const stat = fs.lstatSync(file, { throwIfNoEntry: false });
  if (stat && !stat.isFile())
    throw new Error(`Expected a regular file: ${file}`);
  return stat;
}

function privateWrite(
  file: string,
  bytes: string | Buffer,
  exclusive = false
): void {
  regularFile(file);
  const temporary = exclusive ? file : `${file}.${randomUUID()}.tmp`;
  const fd = fs.openSync(temporary, 'wx', 0o600);
  try {
    if (process.platform !== 'win32') fs.fchmodSync(fd, 0o600);
    fs.writeFileSync(fd, bytes);
  } finally {
    fs.closeSync(fd);
  }
  if (!exclusive) {
    try {
      fs.renameSync(temporary, file);
    } finally {
      fs.rmSync(temporary, { force: true });
    }
  }
}

function ownedPath(directory: string, relative: string): string {
  const file = path.join(directory, relative);
  if (!inside(realLocation(directory), realLocation(file))) {
    throw new Error(`Configuration path escapes its directory: ${relative}`);
  }
  return file;
}

// TOML parser errors can quote credentials. Only surface sanitized diagnostics.
export function parseConfig(text: string, label = CONFIG): ParsedConfig {
  let data: unknown;
  try {
    data = toml().parse(text);
  } catch {
    throw new Error(`Invalid TOML in ${label}`);
  }
  if (!isMapping(data))
    throw new Error(`${label} must contain a section mapping`);
  const found = new Map<string, ConfigSection>();
  for (const [name, raw] of Object.entries(data)) {
    if (!Object.hasOwn(sections, name))
      throw new Error(
        `Unknown section in ${label}; use ${Object.keys(sections).join(', ')}`
      );
    if (!isMapping(raw))
      throw new Error(`${label} ${name} must contain a key mapping`);
    const values: Record<string, string> = {};
    for (const [key, value] of Object.entries(raw)) {
      if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(key))
        throw new Error(`Invalid key in ${label} ${name}`);
      if (name === 'analyzer' && !sectionKeys(sections.analyzer).includes(key))
        throw new Error(`Unsupported analyzer setting: ${key}`);
      if (typeof value !== 'string' || /[\r\n\0]/.test(value))
        throw new Error(
          `${label} ${name}.${key} must be a single-line string; quote scalar values`
        );
      values[key] = value;
    }
    found.set(name, { values });
  }
  return { sections: found };
}

function isMapping(value: unknown): value is Record<string, unknown> {
  return (
    value !== null &&
    typeof value === 'object' &&
    (Object.getPrototypeOf(value) === Object.prototype ||
      Object.getPrototypeOf(value) === null)
  );
}

export function renderConfig({ sections: found }: ParsedConfig): string {
  return toml().stringify(
    Object.fromEntries(
      [...found].map(([name, section]) => [name, section.values])
    )
  );
}

const PROJECTED_MARKER = '# Projected from config.toml';

function projectionBody(config: ParsedConfig, name: string): string {
  const spec = sections[name];
  const section = config.sections.get(name);
  if (!section && !spec.optional)
    throw new Error(`Missing [${name}] in ${CONFIG}`);
  let body = Object.entries(section?.values || {})
    .map(([key, value]) => quoted(key, value))
    .join('\n');
  if (body) body += '\n';
  const projected = spec.projected || [];
  if (projected.length === 0) return body;
  const owners = [...new Set(projected.map(({ from }) => `[${from}]`))];
  const lines = projected.map(({ from, key, as = key }) =>
    quoted(as, config.sections.get(from)?.values[key] ?? '')
  );
  return `${body}${PROJECTED_MARKER} ${owners.join(' and ')}; edit there.\n${lines.join('\n')}\n`;
}

export function readConfig(home: string): ParsedConfig {
  const file = ownedPath(home, CONFIG);
  if (!regularFile(file)) return { sections: new Map() };
  return parseConfig(fs.readFileSync(file, 'utf8'));
}

// Single quotes preserve backslashes across the projection readers.
function quoted(key: string, value: string): string {
  if (!/[\\"\r\n\0]/.test(value)) return `${key}="${value}"`;
  if (
    !/['\r\n\0]/.test(value) &&
    !value.includes('\\\\') &&
    !value.endsWith('\\')
  )
    return `${key}='${value}'`;
  throw new Error(`Unsupported characters for ${key}`);
}

function templateConfig(): ParsedConfig {
  return {
    sections: new Map(
      Object.entries(sections)
        .filter(([, spec]) => !(spec.projection && spec.optional))
        .map(([name, spec]) => [
          name,
          {
            values: Object.fromEntries(
              sectionKeys(spec).map((key) => [key, ''])
            )
          }
        ])
    )
  };
}

export function initializeConfig(home: string, repository = root): void {
  assertExternal(home, [repository]);
  privateDirectory(home);
  privateDirectory(path.join(home, KEYS));
  const file = ownedPath(home, CONFIG);
  if (!regularFile(file))
    privateWrite(file, renderConfig(templateConfig()), true);
  else if (process.platform !== 'win32') fs.chmodSync(file, 0o600);
  readConfig(home);
  // Staging left behind by an interrupted signing command. A day's margin
  // spares a build still running from another clone.
  const stale = Date.now() - 24 * 60 * 60 * 1000;
  for (const entry of fs.readdirSync(home)) {
    const staging = path.join(home, entry);
    if (entry.startsWith(STAGING) && fs.statSync(staging).mtimeMs < stale)
      fs.rmSync(staging, { recursive: true, force: true });
  }
}

function projectionState(repository: string): ProjectionState {
  const file = path.join(repository, '.bpp-local.json');
  regularFile(file);
  if (!fs.existsSync(file)) return {};
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8')) as ProjectionState;
  } catch {
    throw new Error(
      'Invalid .bpp-local.json; inspect the local projection receipt'
    );
  }
}

export function projectionChanges(
  home: string,
  repository = root
): ProjectionChange[] {
  assertExternal(home, [repository]);
  const config = readConfig(home);
  const state = projectionState(repository);
  const changes: ProjectionChange[] = [];
  for (const [name, spec] of Object.entries(sections)) {
    const relative = spec.projection;
    const section = config.sections.get(name);
    if (!relative || (!section && !spec.optional)) continue;
    for (const key of spec.absolute || []) {
      const value = section?.values[key];
      if (value && !path.isAbsolute(value))
        throw new Error(`${key} must be absolute in [${name}]`);
    }
    const target = ownedPath(repository, relative);
    const bytes = Buffer.from(projectionBody(config, name));
    const previous = regularFile(target) ? fs.readFileSync(target) : null;
    if (
      previous &&
      !previous.equals(bytes) &&
      (state.configHome !== home ||
        state.files?.[relative] !== digest(previous))
    ) {
      throw new Error(
        `Local configuration conflict: ${relative}; preserve your edits in [${name}] before setup`
      );
    }
    changes.push({ relative, bytes, changed: !previous?.equals(bytes) });
  }
  return changes;
}

export function refreshProjections(
  home: string,
  repository = root
): ProjectionChange[] {
  const changes = projectionChanges(home, repository);
  for (const { relative, bytes, changed } of changes) {
    const target = path.join(repository, relative);
    if (changed) privateWrite(target, bytes);
    else if (process.platform !== 'win32') fs.chmodSync(target, 0o600);
  }
  privateWrite(
    path.join(repository, '.bpp-local.json'),
    `${JSON.stringify({ configHome: home, files: Object.fromEntries(changes.map(({ relative, bytes }) => [relative, digest(bytes)])) }, null, 2)}\n`
  );
  return changes;
}

export function resolveValues(
  config: ParsedConfig,
  name: string,
  env: NodeJS.ProcessEnv = process.env
): Record<string, string> {
  const spec = sections[name];
  const values: Record<string, string> = {
    ...config.sections.get(name)?.values
  };
  for (const key of spec.inject || []) if (env[key]) values[key] = env[key];
  return values;
}

export interface MissingValues {
  scope: string;
  missing: string[];
}

export function missingValues(
  config: ParsedConfig,
  env: NodeJS.ProcessEnv = process.env
): MissingValues[] {
  const rows: MissingValues[] = [];
  for (const [name, spec] of Object.entries(sections)) {
    const values = resolveValues(config, name, env);
    for (const [scope, keys] of Object.entries(spec.required || {}))
      rows.push({ scope, missing: keys.filter((key) => !values[key]?.trim()) });
  }
  return rows;
}

function stagedValues(
  home: string,
  config: ParsedConfig
): Record<string, string> {
  const values: Record<string, string> = {
    ...config.sections.get('signing')?.values
  };
  if (values.APPLE_API_KEY_PATH)
    values.APPLE_API_KEY_PATH = path.resolve(home, values.APPLE_API_KEY_PATH);
  else if (values.APPLE_API_KEY)
    values.APPLE_API_KEY_PATH = path.join(
      home,
      KEYS,
      `AuthKey_${values.APPLE_API_KEY}.p8`
    );
  return values;
}

// Keep the key path rules in sync with bundle.ts loadAppleApiKeyPathEnv.
export function resolveSigning(
  home: string,
  env: NodeJS.ProcessEnv = process.env
): ResolvedSigning {
  const explicit = env.BPP_SIGNING_SECRETS_DIR;
  const values: Record<string, string> = {};
  if (explicit) {
    for (const [file, key] of Object.entries(sections.signing.stage)) {
      const source = path.join(explicit, file);
      if (fs.existsSync(source))
        values[key] = fs.readFileSync(source, 'utf8').replace(/\n+$/, '');
    }
  } else Object.assign(values, stagedValues(home, readConfig(home)));
  for (const key of Object.values(sections.signing.stage))
    if (env[key]) values[key] = env[key];
  const directory = explicit || path.join(home, KEYS);
  const keyPath =
    values.APPLE_API_KEY_PATH ||
    (values.APPLE_API_KEY
      ? path.join(directory, `AuthKey_${values.APPLE_API_KEY}.p8`)
      : '');
  return {
    values,
    appleKeyPath: path.isAbsolute(keyPath) ? keyPath : '',
    updaterKey: path.join(directory, 'tauri-updater.key')
  };
}

const signals = ['SIGINT', 'SIGTERM', 'SIGHUP'];

// Stage signing inputs as private files so dependency installs and payload builds inherit only the directory path.
function withStaging<T>(
  home: string,
  config: ParsedConfig,
  env: NodeJS.ProcessEnv,
  use: (env: NodeJS.ProcessEnv) => T
): T {
  const values = stagedValues(home, config);
  // The command shares the terminal's signals; outlive them so staged files
  // are always removed. Handlers go in before any file exists.
  const hold = () => {};
  for (const signal of signals) process.on(signal, hold);
  let staging: string | undefined;
  try {
    staging = fs.mkdtempSync(path.join(home, STAGING));
    privateDirectory(staging);
    const keys = path.join(home, KEYS);
    for (const entry of fs.existsSync(keys) ? fs.readdirSync(keys) : []) {
      if (keyMaterial(entry) && regularFile(path.join(keys, entry)))
        privateWrite(
          path.join(staging, entry),
          fs.readFileSync(path.join(keys, entry)),
          true
        );
    }
    for (const [file, key] of Object.entries(sections.signing.stage))
      if (values[key])
        privateWrite(path.join(staging, file), values[key], true);
    return use({ ...env, BPP_SIGNING_SECRETS_DIR: staging });
  } finally {
    if (staging) fs.rmSync(staging, { recursive: true, force: true });
    for (const signal of signals) process.off(signal, hold);
  }
}

function resolveSingleProfile(profile: string): [string, SectionSpec] {
  const found = Object.entries(sections).find(
    ([, spec]) => spec.profile === profile
  );
  if (!found)
    throw new Error(
      `Unknown profile; use ${profiles.slice(0, -1).join(', ')} or ${profiles.at(-1)}`
    );
  return found;
}

export function withProfileEnvironment<T>(
  profile: string,
  home: string,
  use: (env: NodeJS.ProcessEnv) => T,
  env: NodeJS.ProcessEnv = process.env,
  repository = root
): T {
  assertExternal(home, [repository]);
  const requested = profile.split(',');
  const seen = new Set<string>();
  const resolved: [string, SectionSpec][] = [];
  for (const item of requested) {
    if (seen.has(item)) throw new Error(`Duplicate profile: ${item}`);
    seen.add(item);
    resolved.push(resolveSingleProfile(item));
  }
  const config = readConfig(home);
  const apply = (index: number, currentEnv: NodeJS.ProcessEnv): T => {
    if (index === resolved.length) return use(currentEnv);
    const [name, spec] = resolved[index];
    if (spec.projection) {
      if (!config.sections.has(name) && !spec.optional)
        throw new Error(`Missing [${name}] in ${CONFIG}; run just setup`);
      refreshProjections(home, repository);
      return apply(index + 1, { ...currentEnv });
    }
    if (spec.stage) {
      if (currentEnv.BPP_SIGNING_SECRETS_DIR)
        return apply(index + 1, { ...currentEnv });
      return withStaging(home, config, currentEnv, (stagedEnv) =>
        apply(index + 1, stagedEnv)
      );
    }
    const result = { ...currentEnv };
    const values = config.sections.get(name)?.values || {};
    const inject = spec.inject;
    if (!inject) throw new Error(`[${name}] needs exactly one delivery`);
    for (const key of inject) {
      if (!result[key] && values[key]) result[key] = values[key];
    }
    for (const fileKey of spec.injectFiles || []) {
      if (result[fileKey]) continue;
      const file = loadGithubSecrets(repository).items.find(
        (item) => item.name === fileKey
      )?.local.file;
      if (!file) throw new Error(`${fileKey} names no keys/ file`);
      const source = ownedPath(home, file);
      // Verbatim: OpenSSH rejects a private key without its final newline.
      if (regularFile(source))
        result[fileKey] = fs.readFileSync(source, 'utf8');
    }
    return apply(index + 1, result);
  };
  return apply(0, env);
}
