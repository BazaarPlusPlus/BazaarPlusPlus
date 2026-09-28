import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { parseEnv } from 'node:util';

export const root = path.resolve(import.meta.dirname, '..');
const CONFIG = 'config.ini';
const KEYS = 'keys';
const STAGING = '.signing-';
// Sections in config.ini order. A projection section is copied verbatim into
// its project file; a profile section only injects its allowlisted keys.
const sections = {
  server: { projection: 'bazaarplusplus-server/.dev.vars' },
  analyzer: { projection: 'bazaarplusplus-analyzer/.env' },
  release: {
    profile: 'release',
    keys: [
      'BPP_R2_ACCOUNT_ID',
      'BPP_R2_ACCESS_KEY_ID',
      'BPP_R2_SECRET_ACCESS_KEY'
    ]
  },
  signing: {
    keys: [
      'APPLE_SIGNING_IDENTITY',
      'APPLE_API_ISSUER',
      'APPLE_API_KEY',
      'APPLE_API_KEY_PATH',
      'TAURI_SIGNING_PRIVATE_KEY_PASSWORD'
    ]
  },
  machine: { profile: 'mod', keys: ['BPP_MANAGED_PATH', 'BPP_GAME_ROOT'] },
  cloudflare: {
    profile: 'cloudflare',
    keys: ['CLOUDFLARE_API_TOKEN', 'CLOUDFLARE_ACCOUNT_ID']
  }
};
// bundle.sh reads signing values from these files in BPP_SIGNING_SECRETS_DIR.
const signingFiles = {
  'apple-signing-identity': 'APPLE_SIGNING_IDENTITY',
  'apple-api-issuer': 'APPLE_API_ISSUER',
  'apple-api-key': 'APPLE_API_KEY',
  'apple-api-key-path': 'APPLE_API_KEY_PATH',
  'tauri-updater.password': 'TAURI_SIGNING_PRIVATE_KEY_PASSWORD'
};
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

export function readEnvironment(file) {
  if (!fs.existsSync(file)) return {};
  try {
    return parseEnv(fs.readFileSync(file, 'utf8'));
  } catch {
    throw new Error(`Cannot parse configuration: ${file}`);
  }
}

// Errors name the line, never its content: values are credentials.
export function parseConfig(text) {
  const preamble = [];
  const found = new Map();
  let current = null;
  text.split(/\r?\n/).forEach((line, index) => {
    const where = `config.ini line ${index + 1}`;
    if (/^\s*\[/.test(line)) {
      const name = /^\[([a-z]+)\]\s*$/.exec(line)?.[1];
      if (!name || !sections[name])
        throw new Error(`Unknown section header on ${where}`);
      if (found.has(name)) throw new Error(`Duplicate [${name}] on ${where}`);
      current = [];
      found.set(name, current);
      return;
    }
    if (/^\s*(#.*)?$/.test(line)) {
      (current || preamble).push(line);
      return;
    }
    const value = /^\s*(?:export\s+)?[A-Za-z_][A-Za-z0-9_]*\s*=\s*(.*)$/.exec(
      line
    )?.[1];
    if (value === undefined || !current)
      throw new Error(`Expected KEY=value inside a section on ${where}`);
    const quote = /^["'`]/.exec(value)?.[0];
    if (quote && !value.slice(1).includes(quote))
      throw new Error(`Multi-line quoted values are unsupported on ${where}`);
    current.push(line);
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
    sections: new Map([...found].map(([name, lines]) => [name, trim(lines)]))
  };
}

function renderConfig({ preamble, sections: bodies }) {
  const parts = preamble ? [preamble] : [];
  for (const name of Object.keys(sections))
    if (bodies.has(name)) parts.push(`[${name}]\n${bodies.get(name)}`);
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

export function sectionValues(config, name) {
  return parseEnv(config.sections.get(name) || '');
}

const quoted = (key, value) => {
  if (/[\r\n"\0]/.test(value))
    throw new Error(`Unsupported characters for ${key}`);
  return `${key}="${value}"`;
};

function setValue(body, key, value) {
  const line = quoted(key, value);
  const pattern = new RegExp(`^\\s*(?:export\\s+)?${key}\\s*=.*$`, 'gm');
  return pattern.test(body)
    ? body.replace(pattern, () => line)
    : `${body}${line}\n`;
}

function templateConfig(repository) {
  const blank = (keys) => keys.map((key) => `${key}=\n`).join('');
  return parseConfig(
    [
      '# BazaarPlusPlus local configuration. Parsed as data, never sourced.',
      '# Sections and what each one supplies: node scripts/workspace.mjs --help',
      '',
      '[server]',
      blank([
        'R2_PRESIGN_ACCESS_KEY_ID',
        'R2_PRESIGN_SECRET_ACCESS_KEY',
        'BUNDLE_SYNC_TOKEN',
        'BAZAARDB_DELIVERY_TOKEN'
      ]),
      '[analyzer]',
      fs
        .readFileSync(
          path.join(repository, 'bazaarplusplus-analyzer/.env.example'),
          'utf8'
        )
        .trimEnd(),
      '',
      '[release]',
      blank(sections.release.keys),
      '[signing]',
      '# Key files live in keys/: tauri-updater.key and AuthKey_<APPLE_API_KEY>.p8.',
      '# APPLE_API_KEY_PATH is optional; a relative path resolves against this directory.',
      blank(sections.signing.keys),
      '[machine]',
      '# Optional overrides for nonstandard game installations.',
      blank(sections.machine.keys),
      '[cloudflare]',
      '# Optional. Existing Wrangler login or exported CLI credentials also work.',
      blank(sections.cloudflare.keys)
    ].join('\n')
  );
}

const unset = (body) => Object.values(parseEnv(body)).every((v) => !v.trim());

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
  for (const [name, { projection }] of Object.entries(sections)) {
    if (!projection) continue;
    const file = path.join(source, projection);
    if (!regularFile(file)) continue;
    let body = fs.readFileSync(file, 'utf8');
    if (name === 'analyzer') {
      const value = readEnvironment(file).BPP_DATA_ROOT;
      if (value && !path.isAbsolute(value)) {
        const absolute = path.resolve(path.dirname(file), value);
        body = setValue(body, 'BPP_DATA_ROOT', absolute.replaceAll('\\', '/'));
      }
    }
    body = parseConfig(`[${name}]\n${body}`).sections.get(name);
    const existing = config.sections.get(name) || '';
    if (
      existing !== body &&
      existing !== template.get(name) &&
      !unset(existing)
    )
      conflict(`[${name}]`);
    config.sections.set(name, body);
    imported.push(`[${name}]`);
  }
  const keys = new Map();
  const signing = path.join(source, 'bazaarplusplus-installer/signing-secrets');
  if (fs.existsSync(signing)) {
    if (fs.lstatSync(signing).isSymbolicLink())
      throw new Error(
        'Import signing-secrets from its owning checkout, not a symlink'
      );
    const values = {};
    for (const entry of fs.readdirSync(signing).sort()) {
      const file = path.join(signing, entry);
      regularFile(file);
      if (keyMaterial(entry)) keys.set(entry, fs.readFileSync(file));
      else if (signingFiles[entry])
        values[signingFiles[entry]] = fs.readFileSync(file, 'utf8').trim();
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
    let body = config.sections.get('signing') || '';
    const current = parseEnv(body);
    for (const key of sections.signing.keys) {
      const value = values[key];
      if (!value || current[key] === value) continue;
      if (current[key]?.trim()) conflict(`[signing] ${key}`);
      body = setValue(body, key, value);
      imported.push(`[signing] ${key}`);
    }
    config.sections.set('signing', body);
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
  for (const [name, { projection: relative }] of Object.entries(sections)) {
    if (!relative || !config.sections.has(name)) continue;
    const target = ownedPath(repository, relative);
    const bytes = Buffer.from(config.sections.get(name));
    if (name === 'analyzer') {
      const data = sectionValues(config, name).BPP_DATA_ROOT;
      if (data && !path.isAbsolute(data))
        throw new Error('BPP_DATA_ROOT must be absolute in [analyzer]');
    }
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

// Absolute Apple key path from [signing], defaulting to the keys/ convention.
export function appleKeyPath(home, values) {
  if (values.APPLE_API_KEY_PATH)
    return path.resolve(home, values.APPLE_API_KEY_PATH);
  return values.APPLE_API_KEY
    ? path.join(home, KEYS, `AuthKey_${values.APPLE_API_KEY}.p8`)
    : '';
}

// Signing values reach bundle.sh as files in a private staging directory, so
// the environment of every earlier step (npm ci, payload builds) carries only
// its path. An explicit BPP_SIGNING_SECRETS_DIR is used as given, unmixed.
export function stageSigning(home, env = process.env) {
  if (env.BPP_SIGNING_SECRETS_DIR) return { env: { ...env }, cleanup() {} };
  const config = readConfig(home);
  const values = sectionValues(config, 'signing');
  const staging = fs.mkdtempSync(path.join(home, STAGING));
  const cleanup = () => fs.rmSync(staging, { recursive: true, force: true });
  try {
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
    for (const [file, key] of Object.entries(signingFiles)) {
      const value =
        key === 'APPLE_API_KEY_PATH' ? appleKeyPath(home, values) : values[key];
      if (value) privateWrite(path.join(staging, file), value, true);
    }
  } catch (error) {
    cleanup();
    throw error;
  }
  return { env: { ...env, BPP_SIGNING_SECRETS_DIR: staging }, cleanup };
}

export function commandEnvironment(
  profile,
  home,
  env = process.env,
  repository = root
) {
  assertExternal(home, [repository]);
  const result = { ...env };
  if (profile === 'server' || profile === 'analyzer') {
    if (!readConfig(home).sections.has(profile))
      throw new Error(`Missing [${profile}] in ${CONFIG}; run just setup`);
    refreshProjections(home, repository);
    return result;
  }
  const [name, spec] =
    Object.entries(sections).find(([, spec]) => spec.profile === profile) || [];
  if (!spec)
    throw new Error(
      'Unknown profile; use release, signing, mod, server, analyzer or cloudflare'
    );
  const values = sectionValues(readConfig(home), name);
  for (const key of spec.keys) {
    if (!result[key] && values[key]) result[key] = values[key];
  }
  return result;
}
