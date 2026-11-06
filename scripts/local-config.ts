import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';

export const root = path.resolve(import.meta.dirname, '..');
const CONFIG = 'config.ini';
const KEYS = 'keys';
const STAGING = '.signing-';
const serverKeys = [
  'R2_PRESIGN_ACCESS_KEY_ID',
  'R2_PRESIGN_SECRET_ACCESS_KEY',
  'BUNDLE_SYNC_TOKEN',
  'BAZAARDB_DELIVERY_TOKEN'
];
const cloudflareKeys = ['CLOUDFLARE_API_TOKEN', 'CLOUDFLARE_ACCOUNT_ID'];
// The one owner of each config.ini section, in file order: its profile, how
// that profile delivers it, its keys and which of them doctor requires. Each
// section has exactly one delivery:
//   projection  copied to a project file, refreshed before the command; the
//               section's own text, then `projected` keys from other sections
//               so one credential lives in one place
//   inject      allowlisted keys enter the command environment
//   stage       values become files in a private BPP_SIGNING_SECRETS_DIR for
//               bundle.sh and never enter any environment
// Credentials are kept per trust domain, not per bucket: one Cloudflare
// operator token in [cloudflare] serves every R2 bucket and Wrangler; the S3
// pair each store needs is derived from it (release/r2-store.ts).
/** A key appended to a projection from its owning section, renamed to `as`. */
export interface ProjectedKey {
  from: string;
  key: string;
  as?: string;
}

/** One config.ini section's profile, delivery and keys; see the table below. */
export interface SectionSpec {
  profile: string;
  projection?: string;
  template?: string;
  absolute?: string[];
  projected?: ProjectedKey[];
  keys?: string[];
  inject?: string[];
  /** Staged file name to the key whose value it holds. */
  stage?: Record<string, string>;
  /** Keys doctor does not require; true for every key of the section. */
  optional?: true | string[];
  /** Doctor scope to the keys it requires. */
  required?: Record<string, string[]>;
  notes?: string[];
}

/** One parsed config.ini section: its normalized text and its values. */
export interface ConfigSection {
  body: string;
  values: Record<string, string>;
}

/** A parsed config.ini: the text before the first section, then each section. */
export interface ParsedConfig {
  preamble: string;
  sections: Map<string, ConfigSection>;
}

/** One managed projection file and whether its bytes differ on disk. */
export interface ProjectionChange {
  relative: string;
  bytes: Buffer;
  changed: boolean;
}

/** The signing inputs bundle.sh will see, as resolveSigning derives them. */
export interface ResolvedSigning {
  values: Record<string, string>;
  appleKeyPath: string;
  updaterKey: string;
}

// The .bpp-local.json receipt of the last refresh.
interface ProjectionState {
  configHome?: string;
  files?: Record<string, string>;
}

// The section table; [signing] always stages.
interface SectionTable extends Record<string, SectionSpec> {
  signing: SectionSpec & { stage: Record<string, string> };
}

export const sections: SectionTable = {
  server: {
    profile: 'server',
    projection: 'bazaarplusplus-server/.dev.vars',
    keys: serverKeys,
    required: { server: serverKeys }
  },
  analyzer: {
    profile: 'analyzer',
    projection: 'bazaarplusplus-analyzer/.env',
    // The analyzer owns its keys; bppanalyzer.config load_config reads them.
    template: 'bazaarplusplus-analyzer/.env.example',
    // Every clone receives the same text, so a relative path would move.
    absolute: ['BPP_DATA_ROOT'],
    // Appended to the projection from their owning sections.
    projected: [
      { from: 'server', key: 'BUNDLE_SYNC_TOKEN', as: 'BPP_BUNDLE_SYNC_TOKEN' },
      { from: 'cloudflare', key: 'CLOUDFLARE_API_TOKEN' },
      { from: 'cloudflare', key: 'CLOUDFLARE_ACCOUNT_ID' }
    ],
    required: {
      analyzer: ['BPP_DATA_ROOT', 'BPP_API_BASE_URL'],
      'analyzer publish': ['BPP_METRICS_R2_BUCKET']
    }
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
    // bundle.sh, and release/android-release.ts for the android-* files,
    // read each key from this file in BPP_SIGNING_SECRETS_DIR.
    // APPLE_CERTIFICATE_PASSWORD is not staged: local builds sign from the
    // Keychain; it only reaches GitHub with keys/developer-id.p12.
    stage: {
      'apple-signing-identity': 'APPLE_SIGNING_IDENTITY',
      'apple-api-issuer': 'APPLE_API_ISSUER',
      'apple-api-key': 'APPLE_API_KEY',
      'apple-api-key-path': 'APPLE_API_KEY_PATH',
      'android-keystore-password': 'ANDROID_KEYSTORE_PASSWORD',
      'android-key-alias': 'ANDROID_KEY_ALIAS'
    },
    optional: [
      'APPLE_API_KEY_PATH',
      'ANDROID_KEYSTORE_PASSWORD',
      'ANDROID_KEY_ALIAS'
    ],
    notes: [
      '# Key files live in keys/: tauri-updater.key, AuthKey_<APPLE_API_KEY>.p8,',
      '# developer-id.p12 (the Developer ID certificate export APPLE_CERTIFICATE_PASSWORD opens)',
      '# and android-release.p12 (the Android release keystore ANDROID_KEYSTORE_PASSWORD opens).',
      '# APPLE_API_KEY_PATH is optional; a relative path resolves against this directory.'
    ]
  },
  machine: {
    profile: 'mod',
    inject: ['BPP_MANAGED_PATH', 'BPP_GAME_ROOT'],
    optional: true,
    notes: ['# Optional overrides for nonstandard game installations.']
  },
  game: {
    profile: 'game',
    inject: ['BPP_GAME_EMAIL', 'BPP_GAME_PASSWORD'],
    optional: true,
    notes: [
      '# Optional local test account for just android::dev-login.',
      '# Stored outside the checkout; never synced to GitHub secrets.'
    ]
  },
  cloudflare: {
    profile: 'release',
    keys: cloudflareKeys,
    inject: cloudflareKeys,
    required: { release: cloudflareKeys },
    notes: [
      '# CLOUDFLARE_API_TOKEN is the one Account API Token: R2 read and write on every bucket, plus',
      '# what Wrangler needs. GitHub holds it as one repository secret, also given to Dependabot.'
    ]
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
// A section with a template file lists no keys of its own.
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

// Projected sections are read again by Wrangler's dotenv and python-dotenv,
// which disagree on escapes and on # inside unquoted values. A value is
// accepted only in the form all of them, and this parser, read the same way.
// Errors name the line and key, never the value: values are credentials.
function parseValue(raw: string, key: string, where: string): string {
  const value = raw.trimStart();
  const quote = value[0];
  if (quote === "'" || quote === '"') {
    const end = value.indexOf(quote, 1);
    if (end < 0)
      throw new Error(
        `Unterminated quote for ${key} on ${where}; values are single-line`
      );
    const inner = value.slice(1, end);
    if (!/^\s*(#.*)?$/.test(value.slice(end + 1)))
      throw new Error(
        `Unexpected text after the closing quote for ${key} on ${where}`
      );
    if (quote === '"' && inner.includes('\\'))
      throw new Error(
        `Backslash in a double-quoted value for ${key} on ${where}; use single quotes or leave it unquoted`
      );
    if (quote === "'" && (inner.includes('\\\\') || inner.endsWith('\\')))
      throw new Error(
        `Escaped backslash in a single-quoted value for ${key} on ${where}; dotenv readers disagree on it`
      );
    return inner;
  }
  if (quote === '`')
    throw new Error(
      `Backtick quotes are unsupported for ${key} on ${where}; use single quotes`
    );
  if (quote === '#')
    throw new Error(
      `Value for ${key} on ${where} starts with #; quote it or leave it empty`
    );
  const hash = value.indexOf('#');
  if (hash < 0) return value.trimEnd();
  if (!/\s/.test(value[hash - 1]))
    throw new Error(
      `Unquoted # in the value for ${key} on ${where}; quote the value`
    );
  return value.slice(0, hash).trimEnd();
}

// A section while it is read: its raw lines and its values.
interface OpenSection {
  lines: string[];
  values: Record<string, string>;
}

function parse(text: string, label: string, start?: string): ParsedConfig {
  const preamble: string[] = [];
  const found = new Map<string, OpenSection>();
  const open = (name: string): OpenSection => {
    const section: OpenSection = { lines: [], values: {} };
    found.set(name, section);
    return section;
  };
  let current = start ? open(start) : undefined;
  text
    .replace(/^\uFEFF/, '')
    .split(/\r?\n/)
    .forEach((line, index) => {
      const where = `${label} line ${index + 1}`;
      if (/[\r\0]/.test(line))
        throw new Error(`Carriage return or NUL on ${where}`);
      if (/^\s*\[/.test(line)) {
        const name = /^\s*\[([^\]]*)\]\s*$/.exec(line)?.[1];
        if (name === undefined)
          throw new Error(
            `Malformed section header on ${where}; put [name] alone on its line`
          );
        if (!Object.hasOwn(sections, name))
          throw new Error(
            `Unknown section header on ${where}; sections are ${Object.keys(sections).join(', ')}`
          );
        if (found.has(name)) throw new Error(`Duplicate [${name}] on ${where}`);
        current = open(name);
        return;
      }
      if (/^\s*(#.*)?$/.test(line)) {
        (current?.lines || preamble).push(line);
        return;
      }
      if (/^\s*;/.test(line)) throw new Error(`Use # for comments on ${where}`);
      const match = /^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$/.exec(
        line
      );
      if (!match || !current)
        throw new Error(`Expected KEY=value inside a section on ${where}`);
      const [, key, raw] = match;
      if (Object.hasOwn(current.values, key))
        throw new Error(`Duplicate ${key} on ${where}`);
      current.values[key] = parseValue(raw, key, where);
      current.lines.push(line);
    });
  const trim = (lines: string[]): string => {
    const text = lines
      .join('\n')
      .replace(/^\s*\n/, '')
      .trimEnd();
    return text ? `${text}\n` : '';
  };
  return {
    preamble: trim(preamble),
    sections: new Map(
      [...found].map(([name, { lines, values }]) => [
        name,
        { body: trim(lines), values }
      ])
    )
  };
}

// Returns the preamble and, per section, its normalized body and values.
export function parseConfig(text: string, label = CONFIG): ParsedConfig {
  return parse(text, label);
}

// One section's body, such as the analyzer's .env.example.
function parseSection(
  name: string,
  text: string,
  label: string
): ConfigSection | undefined {
  return parse(text, label, name).sections.get(name);
}

function renderConfig({ preamble, sections: found }: ParsedConfig): string {
  const parts = preamble ? [preamble] : [];
  for (const name of Object.keys(sections)) {
    const section = found.get(name);
    if (section) parts.push(`[${name}]\n${section.body}`);
  }
  return parts.join('\n');
}

const PROJECTED_MARKER = '# Projected from config.ini';

// The bytes a projection receives: the section's own text, then the keys it
// takes from other sections under a marker line, so a reader of the project
// file sees where to edit.
function projectionBody(config: ParsedConfig, name: string): string {
  const spec = sections[name];
  const section = config.sections.get(name);
  // projectionChanges calls this only for a section the config holds.
  if (!section) throw new Error(`Missing [${name}] in ${CONFIG}`);
  let body = section.body;
  const projected = spec.projected || [];
  if (projected.length === 0) return body;
  const owners = [...new Set(projected.map(({ from }) => `[${from}]`))];
  const lines = projected.map(({ from, key, as = key }) =>
    quoted(as, config.sections.get(from)?.values[key] ?? '')
  );
  if (body && !body.endsWith('\n')) body += '\n';
  return `${body}${PROJECTED_MARKER} ${owners.join(' and ')}; edit there.\n${lines.join('\n')}\n`;
}

export function readConfig(home: string): ParsedConfig {
  const file = ownedPath(home, CONFIG);
  if (!regularFile(file)) return { preamble: '', sections: new Map() };
  return parseConfig(fs.readFileSync(file, 'utf8'));
}

// Double quotes by default; single quotes carry a backslash literally.
// Anything else has no form every reader agrees on.
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

function templateConfig(repository: string): ParsedConfig {
  const lines = [
    '# BazaarPlusPlus local configuration. Parsed as data, never sourced.',
    '# Sections, value rules and what each one supplies: node scripts/workspace.ts --help',
    ''
  ];
  for (const [name, spec] of Object.entries(sections)) {
    lines.push(`[${name}]`, ...(spec.notes || []));
    if (spec.template) {
      const text = fs.readFileSync(
        path.join(repository, spec.template),
        'utf8'
      );
      parseSection(name, text, spec.template);
      lines.push(text.trimEnd());
    } else lines.push(...sectionKeys(spec).map((key) => `${key}=`));
    lines.push('');
  }
  return parse(lines.join('\n'), `${CONFIG} template`);
}

export function initializeConfig(home: string, repository = root): void {
  assertExternal(home, [repository]);
  privateDirectory(home);
  privateDirectory(path.join(home, KEYS));
  const file = ownedPath(home, CONFIG);
  if (!regularFile(file))
    privateWrite(file, renderConfig(templateConfig(repository)), true);
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
    if (!relative || !section) continue;
    for (const key of spec.absolute || []) {
      const value = section.values[key];
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

// A section's values as its profile sees them: exported values win for
// injected keys. Reads only; doctor relies on that.
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

// Doctor labels whose required keys are empty.
/** One doctor scope and the required keys it lacks. */
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

// The bundle.sh file values staged from [signing]. APPLE_API_KEY_PATH is
// staged absolute, defaulting to the keys/AuthKey_<APPLE_API_KEY>.p8 convention.
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

// What bundle.sh will see: the files of an exported BPP_SIGNING_SECRETS_DIR,
// else the staged [signing] and keys/, each overridden by an exported value.
// Mirrors bundle.sh load_apple_api_key_path_env: the key path falls back to
// AuthKey_<APPLE_API_KEY>.p8 in that directory, and a relative path is refused.
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

// Signing values reach bundle.sh as files in a private staging directory, so
// the environment of every earlier step (npm ci, payload builds) carries only
// its path. The staging directory exists only while `use` runs.
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

// Runs `use` with the environment a profile's command receives and returns
// its result. Projections are refreshed first; staged files are removed after.
export function withProfileEnvironment<T>(
  profile: string,
  home: string,
  use: (env: NodeJS.ProcessEnv) => T,
  env: NodeJS.ProcessEnv = process.env,
  repository = root
): T {
  assertExternal(home, [repository]);
  const found = Object.entries(sections).find(
    ([, spec]) => spec.profile === profile
  );
  if (!found)
    throw new Error(
      `Unknown profile; use ${profiles.slice(0, -1).join(', ')} or ${profiles.at(-1)}`
    );
  const [name, spec] = found;
  const config = readConfig(home);
  if (spec.projection) {
    if (!config.sections.has(name))
      throw new Error(`Missing [${name}] in ${CONFIG}; run just setup`);
    refreshProjections(home, repository);
    return use({ ...env });
  }
  if (spec.stage) {
    // An explicit directory is used as given, unmixed.
    if (env.BPP_SIGNING_SECRETS_DIR) return use({ ...env });
    return withStaging(home, config, env, use);
  }
  const result = { ...env };
  const values = config.sections.get(name)?.values || {};
  // The module-load check gives every other section exactly this delivery.
  const inject = spec.inject;
  if (!inject) throw new Error(`[${name}] needs exactly one delivery`);
  for (const key of inject) {
    if (!result[key] && values[key]) result[key] = values[key];
  }
  return use(result);
}
