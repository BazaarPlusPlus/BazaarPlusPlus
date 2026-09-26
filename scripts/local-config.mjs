import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { parseEnv } from 'node:util';

export const root = path.resolve(import.meta.dirname, '..');
const projections = {
  'analyzer.env': 'bazaarplusplus-analyzer/.env',
  'server.dev.vars': 'bazaarplusplus-server/.dev.vars'
};
const profiles = {
  release: [
    'release-r2.env',
    ['BPP_R2_ACCOUNT_ID', 'BPP_R2_ACCESS_KEY_ID', 'BPP_R2_SECRET_ACCESS_KEY']
  ],
  mod: ['machine.env', ['BPP_MANAGED_PATH', 'BPP_GAME_ROOT']],
  cloudflare: [
    'cloudflare.env',
    ['CLOUDFLARE_API_TOKEN', 'CLOUDFLARE_ACCOUNT_ID']
  ]
};
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

function setEnvironmentValue(text, key, value) {
  if (/[\r\n"\0]/.test(value))
    throw new Error(`Unsupported path characters for ${key}`);
  const line = `${key}="${value.replaceAll('\\', '/')}"`;
  const pattern = new RegExp(`^\\s*(?:export\\s+)?${key}\\s*=.*$`, 'gm');
  return pattern.test(text)
    ? text.replace(pattern, () => line)
    : `${text}\n${line}\n`;
}

export function importCheckout(source, home, repository = root) {
  source = fs.realpathSync(source);
  assertExternal(home, [repository, source]);
  const pending = new Map();
  for (const [name, relative] of Object.entries(projections)) {
    const file = path.join(source, relative);
    if (!regularFile(file)) continue;
    let text = fs.readFileSync(file, 'utf8');
    if (name === 'analyzer.env') {
      const value = readEnvironment(file).BPP_DATA_ROOT;
      if (value && !path.isAbsolute(value)) {
        text = setEnvironmentValue(
          text,
          'BPP_DATA_ROOT',
          path.resolve(path.dirname(file), value)
        );
      }
    }
    pending.set(name, Buffer.from(text));
  }
  const signing = path.join(source, 'bazaarplusplus-installer/signing-secrets');
  if (fs.existsSync(signing)) {
    if (fs.lstatSync(signing).isSymbolicLink())
      throw new Error(
        'Import signing-secrets from its owning checkout, not a symlink'
      );
    for (const entry of fs.readdirSync(signing)) {
      const file = path.join(signing, entry);
      regularFile(file);
      pending.set(`signing-secrets/${entry}`, fs.readFileSync(file));
    }
    const pointer = pending.get('signing-secrets/apple-api-key-path');
    if (pointer) {
      const oldPath = path.resolve(
        source,
        'bazaarplusplus-installer',
        pointer.toString().trim()
      );
      if (!inside(signing, oldPath) || !fs.existsSync(oldPath)) {
        throw new Error(
          'Apple API key must exist inside the source signing-secrets directory before import'
        );
      }
      pending.set(
        'signing-secrets/apple-api-key-path',
        Buffer.from(
          `${path.join(home, 'signing-secrets', path.relative(signing, oldPath))}\n`
        )
      );
    }
  }
  // Preflight the entire import before creating any file. A differing credential
  // is a conflict, never an invitation to guess which identity should win.
  for (const [name, bytes] of pending) {
    const target = ownedPath(home, name);
    if (regularFile(target) && !fs.readFileSync(target).equals(bytes)) {
      throw new Error(
        `Import conflict: ${name}; existing configuration was preserved`
      );
    }
  }
  privateDirectory(home);
  for (const [name, bytes] of pending) {
    const target = ownedPath(home, name);
    privateDirectory(path.dirname(target));
    if (!fs.existsSync(target)) privateWrite(target, bytes, true);
    else if (process.platform !== 'win32') fs.chmodSync(target, 0o600);
  }
  return [...pending.keys()];
}

export function initializeConfig(home, repository = root) {
  assertExternal(home, [repository]);
  privateDirectory(home);
  const templates = {
    'analyzer.env': fs.readFileSync(
      path.join(repository, 'bazaarplusplus-analyzer/.env.example')
    ),
    'server.dev.vars':
      'R2_PRESIGN_ACCESS_KEY_ID=\nR2_PRESIGN_SECRET_ACCESS_KEY=\nBUNDLE_SYNC_TOKEN=\nBAZAARDB_DELIVERY_TOKEN=\n',
    'release-r2.env': profiles.release[1].map((key) => `${key}=\n`).join(''),
    'machine.env':
      '# Optional overrides for nonstandard game installations.\nBPP_MANAGED_PATH=\nBPP_GAME_ROOT=\n',
    'cloudflare.env':
      '# Optional. Existing Wrangler login or exported CLI credentials also work.\nCLOUDFLARE_API_TOKEN=\nCLOUDFLARE_ACCOUNT_ID=\n'
  };
  for (const [name, bytes] of Object.entries(templates)) {
    const file = ownedPath(home, name);
    if (!regularFile(file)) privateWrite(file, bytes, true);
    else if (process.platform !== 'win32') fs.chmodSync(file, 0o600);
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
  const state = projectionState(repository);
  const changes = [];
  for (const [name, relative] of Object.entries(projections)) {
    const source = ownedPath(home, name);
    const target = ownedPath(repository, relative);
    if (!regularFile(source)) continue;
    const bytes = fs.readFileSync(source);
    if (name === 'analyzer.env') {
      const data = readEnvironment(source).BPP_DATA_ROOT;
      if (data && !path.isAbsolute(data))
        throw new Error(
          'BPP_DATA_ROOT must be absolute in the shared analyzer.env'
        );
    }
    const previous = regularFile(target) ? fs.readFileSync(target) : null;
    if (
      previous &&
      !previous.equals(bytes) &&
      (state.configHome !== home ||
        state.files?.[relative] !== digest(previous))
    ) {
      throw new Error(
        `Local configuration conflict: ${relative}; preserve your edits in ${name} before setup`
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

export function commandEnvironment(
  profile,
  home,
  env = process.env,
  repository = root
) {
  assertExternal(home, [repository]);
  const result = { ...env };
  if (profile === 'server' || profile === 'analyzer') {
    const name = profile === 'server' ? 'server.dev.vars' : 'analyzer.env';
    if (!fs.existsSync(path.join(home, name)))
      throw new Error(`Missing ${name}; run just setup`);
    refreshProjections(home, repository);
    return result;
  }
  if (profile === 'signing') {
    if (
      !result.BPP_SIGNING_SECRETS_DIR &&
      fs.existsSync(path.join(home, 'signing-secrets'))
    ) {
      result.BPP_SIGNING_SECRETS_DIR = path.join(home, 'signing-secrets');
    }
    return result;
  }
  const spec = profiles[profile];
  if (!spec)
    throw new Error(
      'Unknown profile; use release, signing, mod, server, analyzer or cloudflare'
    );
  const values = readEnvironment(path.join(home, spec[0]));
  for (const key of spec[1]) {
    if (!result[key] && values[key]) result[key] = values[key];
  }
  return result;
}
