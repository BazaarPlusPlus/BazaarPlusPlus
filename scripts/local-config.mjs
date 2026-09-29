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
const releaseKeys = [
  'BPP_R2_ACCOUNT_ID',
  'BPP_R2_ACCESS_KEY_ID',
  'BPP_R2_SECRET_ACCESS_KEY'
];
// The one owner of each config.ini section, in file order: its profile, how
// that profile delivers it, its keys and which of them doctor requires. Each
// section has exactly one delivery:
//   projection  copied verbatim to a project file, refreshed before the command
//   inject      allowlisted keys enter the command environment
//   stage       values become files in a private BPP_SIGNING_SECRETS_DIR for
//               bundle.sh and never enter any environment
export const sections = {
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
    required: {
      analyzer: [
        'BPP_DATA_ROOT',
        'BPP_V5_API_BASE_URL',
        'BPP_BUNDLE_SYNC_TOKEN'
      ],
      'analyzer publish': [
        'BPP_METRICS_R2_ACCOUNT_ID',
        'BPP_METRICS_R2_BUCKET',
        'BPP_METRICS_R2_ACCESS_KEY_ID',
        'BPP_METRICS_R2_SECRET_ACCESS_KEY'
      ]
    }
  },
  release: {
    profile: 'release',
    inject: releaseKeys,
    required: { 'release upload': releaseKeys }
  },
  signing: {
    profile: 'signing',
    // bundle.sh reads each key from this file in BPP_SIGNING_SECRETS_DIR.
    stage: {
      'apple-signing-identity': 'APPLE_SIGNING_IDENTITY',
      'apple-api-issuer': 'APPLE_API_ISSUER',
      'apple-api-key': 'APPLE_API_KEY',
      'apple-api-key-path': 'APPLE_API_KEY_PATH',
      'tauri-updater.password': 'TAURI_SIGNING_PRIVATE_KEY_PASSWORD'
    },
    optional: ['APPLE_API_KEY_PATH', 'TAURI_SIGNING_PRIVATE_KEY_PASSWORD'],
    notes: [
      '# Key files live in keys/: tauri-updater.key and AuthKey_<APPLE_API_KEY>.p8.',
      '# APPLE_API_KEY_PATH is optional; a relative path resolves against this directory.'
    ]
  },
  machine: {
    profile: 'mod',
    inject: ['BPP_MANAGED_PATH', 'BPP_GAME_ROOT'],
    optional: true,
    notes: ['# Optional overrides for nonstandard game installations.']
  },
  cloudflare: {
    profile: 'cloudflare',
    inject: ['CLOUDFLARE_API_TOKEN', 'CLOUDFLARE_ACCOUNT_ID'],
    optional: true,
    notes: [
      '# Optional. Existing Wrangler login or exported CLI credentials also work.'
    ]
  }
};
for (const [name, spec] of Object.entries(sections)) {
  if ([spec.projection, spec.inject, spec.stage].filter(Boolean).length !== 1)
    throw new Error(`[${name}] needs exactly one delivery`);
}
export const profiles = Object.values(sections).map((spec) => spec.profile);
// A section with a template file lists no keys of its own.
export const sectionKeys = (spec) =>
  spec.keys || spec.inject || Object.values(spec.stage || {});
const keyMaterial = (name) =>
  name === 'tauri-updater.key' ||
  name === 'tauri-updater.key.pub' ||
  /^AuthKey_[A-Za-z0-9]+\.p8$/.test(name);
const legacyFiles = [
  'analyzer.env',
  'server.dev.vars',
  'release-r2.env',
  'machine.env',
  'cloudflare.env',
  'signing-secrets'
];
const digest = (bytes) => createHash('sha256').update(bytes).digest('hex');

function realLocation(file) {
  try {
    return fs.realpathSync(file);
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    // A dangling link is not a new directory.
    if (fs.lstatSync(file, { throwIfNoEntry: false }))
      throw new Error(`Dangling link: ${file}`);
    return path.join(realLocation(path.dirname(file)), path.basename(file));
  }
}

function inside(parent, child) {
  const relative = path.relative(parent, child);
  return (
    relative === '' ||
    (!relative.startsWith(`..${path.sep}`) &&
      relative !== '..' &&
      !path.isAbsolute(relative))
  );
}

export function configHome(env = process.env) {
  const directory =
    env.BPP_CONFIG_HOME || path.join(os.homedir(), '.config', 'bazaarplusplus');
  if (!path.isAbsolute(directory))
    throw new Error('BPP_CONFIG_HOME must be absolute');
  return realLocation(directory);
}

export function assertExternal(home, repositories = [root]) {
  for (const repository of repositories) {
    if (inside(realLocation(repository), realLocation(home))) {
      throw new Error(
        'BPP_CONFIG_HOME must be outside the source and destination checkouts'
      );
    }
  }
}

function privateDirectory(directory) {
  fs.mkdirSync(directory, { recursive: true, mode: 0o700 });
  if (process.platform !== 'win32') fs.chmodSync(directory, 0o700);
}

function regularFile(file) {
  const stat = fs.lstatSync(file, { throwIfNoEntry: false });
  if (stat && !stat.isFile())
    throw new Error(`Expected a regular file: ${file}`);
  return stat;
}

function privateWrite(file, bytes, exclusive = false) {
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

function ownedPath(directory, relative) {
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
function parseValue(raw, key, where) {
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

function parse(text, label, start) {
  const preamble = [];
  const found = new Map();
  const open = (name) => {
    const section = { lines: [], values: {} };
    found.set(name, section);
    return section;
  };
  let current = start && open(start);
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
  const trim = (lines) => {
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
export function parseConfig(text, label = CONFIG) {
  return parse(text, label);
}

// One section's body, such as a project file from an old checkout.
function parseSection(name, text, label) {
  return parse(text, label, name).sections.get(name);
}

function renderConfig({ preamble, sections: found }) {
  const parts = preamble ? [preamble] : [];
  for (const name of Object.keys(sections))
    if (found.has(name)) parts.push(`[${name}]\n${found.get(name).body}`);
  return parts.join('\n');
}

function assertCurrentLayout(home) {
  if (fs.existsSync(path.join(home, CONFIG))) return;
  const legacy = legacyFiles.filter((name) =>
    fs.existsSync(path.join(home, name))
  );
  if (legacy.length)
    throw new Error(
      `Legacy configuration files in ${home} (${legacy.join(', ')}): move their values into ${CONFIG} and key files into ${KEYS}/, then move the old files aside`
    );
}

export function readConfig(home) {
  assertCurrentLayout(home);
  const file = ownedPath(home, CONFIG);
  if (!regularFile(file)) return { preamble: '', sections: new Map() };
  return parseConfig(fs.readFileSync(file, 'utf8'));
}

// Double quotes keep what earlier imports wrote; single quotes carry a
// backslash literally. Anything else has no form every reader agrees on.
function quoted(key, value) {
  if (!/[\\"\r\n\0]/.test(value)) return `${key}="${value}"`;
  if (
    !/['\r\n\0]/.test(value) &&
    !value.includes('\\\\') &&
    !value.endsWith('\\')
  )
    return `${key}='${value}'`;
  throw new Error(`Unsupported characters for ${key}`);
}

// Parsing rejects duplicate keys first, so at most one line matches.
function setValue(body, key, value) {
  const line = quoted(key, value);
  const pattern = new RegExp(`^\\s*(?:export\\s+)?${key}\\s*=.*$`, 'm');
  return pattern.test(body)
    ? body.replace(pattern, () => line)
    : `${body}${line}\n`;
}

function templateConfig(repository) {
  const lines = [
    '# BazaarPlusPlus local configuration. Parsed as data, never sourced.',
    '# Sections, value rules and what each one supplies: node scripts/workspace.mjs --help',
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

const unset = (section) =>
  Object.values(section.values).every((value) => !value.trim());

export function importCheckout(source, home, repository = root) {
  source = fs.realpathSync(source);
  assertExternal(home, [repository, source]);
  const config = fs.existsSync(path.join(home, CONFIG))
    ? readConfig(home)
    : (assertCurrentLayout(home), templateConfig(repository));
  const conflict = (name) => {
    throw new Error(
      `Import conflict: ${name}; existing configuration was preserved`
    );
  };
  const template = templateConfig(repository).sections;
  const imported = [];
  for (const [name, spec] of Object.entries(sections)) {
    if (!spec.projection) continue;
    const file = path.join(source, spec.projection);
    if (!regularFile(file)) continue;
    let body = fs.readFileSync(file, 'utf8');
    const { values } = parseSection(name, body, file);
    // Keep the meaning of a path relative to the old project file.
    for (const key of spec.absolute || []) {
      if (values[key] && !path.isAbsolute(values[key])) {
        const absolute = path.resolve(path.dirname(file), values[key]);
        body = setValue(body, key, absolute.replaceAll('\\', '/'));
      }
    }
    const section = parseSection(name, body, file);
    const existing = config.sections.get(name);
    if (
      existing &&
      existing.body !== section.body &&
      existing.body !== template.get(name)?.body &&
      !unset(existing)
    )
      conflict(`[${name}]`);
    config.sections.set(name, section);
    imported.push(`[${name}]`);
  }
  const keys = new Map();
  const signing = path.join(source, 'bazaarplusplus-installer/signing-secrets');
  if (fs.existsSync(signing)) {
    if (fs.lstatSync(signing).isSymbolicLink())
      throw new Error(
        'Import signing-secrets from its owning checkout, not a symlink'
      );
    const staged = sections.signing.stage;
    const values = {};
    for (const entry of fs.readdirSync(signing).sort()) {
      const file = path.join(signing, entry);
      regularFile(file);
      if (keyMaterial(entry)) keys.set(entry, fs.readFileSync(file));
      else if (Object.hasOwn(staged, entry))
        values[staged[entry]] = fs.readFileSync(file, 'utf8').trim();
      else throw new Error(`Unknown signing-secrets entry: ${entry}`);
    }
    if (values.APPLE_API_KEY_PATH) {
      const oldPath = path.resolve(
        source,
        'bazaarplusplus-installer',
        values.APPLE_API_KEY_PATH
      );
      if (!inside(signing, oldPath) || !fs.existsSync(oldPath)) {
        throw new Error(
          'Apple API key must exist inside the source signing-secrets directory before import'
        );
      }
      const relative = path.relative(signing, oldPath);
      // keys/AuthKey_<APPLE_API_KEY>.p8 is found by convention.
      values.APPLE_API_KEY_PATH =
        relative === `AuthKey_${values.APPLE_API_KEY}.p8`
          ? ''
          : `${KEYS}/${relative.replaceAll('\\', '/')}`;
    }
    const existing = config.sections.get('signing');
    let body = existing?.body || '';
    const current = existing?.values || {};
    for (const key of Object.values(staged)) {
      const value = values[key];
      if (!value || current[key] === value) continue;
      if (current[key]?.trim()) conflict(`[signing] ${key}`);
      body = setValue(body, key, value);
      imported.push(`[signing] ${key}`);
    }
    config.sections.set('signing', parseSection('signing', body, CONFIG));
  }
  // Preflight every write before creating any file. A differing credential
  // is a conflict, never an invitation to guess which identity should win.
  for (const [name, bytes] of keys) {
    const target = ownedPath(home, `${KEYS}/${name}`);
    if (regularFile(target) && !fs.readFileSync(target).equals(bytes))
      conflict(`${KEYS}/${name}`);
    imported.push(`${KEYS}/${name}`);
  }
  const text = renderConfig(config);
  parseConfig(text);
  privateDirectory(home);
  privateDirectory(path.join(home, KEYS));
  for (const [name, bytes] of keys) {
    const target = ownedPath(home, `${KEYS}/${name}`);
    if (!fs.existsSync(target)) privateWrite(target, bytes, true);
    else if (process.platform !== 'win32') fs.chmodSync(target, 0o600);
  }
  privateWrite(ownedPath(home, CONFIG), text);
  return imported;
}

export function initializeConfig(home, repository = root) {
  assertExternal(home, [repository]);
  assertCurrentLayout(home);
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

function projectionState(repository) {
  const file = path.join(repository, '.bpp-local.json');
  regularFile(file);
  if (!fs.existsSync(file)) return {};
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    throw new Error(
      'Invalid .bpp-local.json; inspect the local projection receipt'
    );
  }
}

export function projectionChanges(home, repository = root) {
  assertExternal(home, [repository]);
  const config = readConfig(home);
  const state = projectionState(repository);
  const changes = [];
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
    const bytes = Buffer.from(section.body);
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

export function refreshProjections(home, repository = root) {
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
export function resolveValues(config, name, env = process.env) {
  const spec = sections[name];
  const values = { ...config.sections.get(name)?.values };
  for (const key of spec.inject || []) if (env[key]) values[key] = env[key];
  return values;
}

// Doctor labels whose required keys are empty.
export function missingValues(config, env = process.env) {
  const rows = [];
  for (const [name, spec] of Object.entries(sections)) {
    const values = resolveValues(config, name, env);
    for (const [scope, keys] of Object.entries(spec.required || {}))
      rows.push({ scope, missing: keys.filter((key) => !values[key]?.trim()) });
  }
  return rows;
}

// The bundle.sh file values staged from [signing]. APPLE_API_KEY_PATH is
// staged absolute, defaulting to the keys/AuthKey_<APPLE_API_KEY>.p8 convention.
function stagedValues(home, config) {
  const values = { ...config.sections.get('signing')?.values };
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
// AuthKey_<APPLE_API_KEY>.p8 in that directory, and a relative path resolves
// against the installer.
export function resolveSigning(home, env = process.env, repository = root) {
  const explicit = env.BPP_SIGNING_SECRETS_DIR;
  const values = {};
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
    appleKeyPath:
      keyPath && path.resolve(repository, 'bazaarplusplus-installer', keyPath),
    updaterKey: path.join(directory, 'tauri-updater.key')
  };
}

const signals = ['SIGINT', 'SIGTERM', 'SIGHUP'];

// Signing values reach bundle.sh as files in a private staging directory, so
// the environment of every earlier step (npm ci, payload builds) carries only
// its path. The staging directory exists only while `use` runs.
function withStaging(home, config, env, use) {
  const values = stagedValues(home, config);
  // The command shares the terminal's signals; outlive them so staged files
  // are always removed. Handlers go in before any file exists.
  const hold = () => {};
  for (const signal of signals) process.on(signal, hold);
  let staging;
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
export function withProfileEnvironment(
  profile,
  home,
  use,
  env = process.env,
  repository = root
) {
  assertExternal(home, [repository]);
  const [name, spec] =
    Object.entries(sections).find(([, spec]) => spec.profile === profile) || [];
  if (!spec)
    throw new Error(
      `Unknown profile; use ${profiles.slice(0, -1).join(', ')} or ${profiles.at(-1)}`
    );
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
  for (const key of spec.inject) {
    if (!result[key] && values[key]) result[key] = values[key];
  }
  return use(result);
}
