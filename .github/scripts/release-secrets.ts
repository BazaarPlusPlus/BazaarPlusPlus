import fs from 'node:fs';
import path from 'node:path';
import {
  RELEASE_PLATFORMS,
  RELEASE_STAGES,
  loadGithubSecrets,
  localSource,
  releaseWorkflowNames,
  requiredForRelease,
  stagedSigningEntries
} from '../../release/github-secrets.ts';
import type {
  GithubSecretReleasePlatform,
  GithubSecretsTable
} from '../../release/github-secrets.ts';

// The GitHub secrets and variables .github/workflows/release.yml consumes:
// which ones a run needs and how the signing ones reach desktop packaging and
// Android release signing. The names, scopes, local sources and staged file
// layout live in release/github-secrets.json and release/github-secrets.ts.
// Values are never printed; release-secrets.test.ts beside this file is the
// executable specification.

const USAGE =
  'Usage: release-secrets.ts check <macos|windows|android> <prepare|release> | stage <macos|windows|android> <directory> | names';

export interface ReleaseSecretsIo {
  stdout: (line: string) => void;
  stderr: (line: string) => void;
}

export interface ReleaseSecretsOptions {
  env?: NodeJS.ProcessEnv;
  workspaceRoot?: string;
  io?: ReleaseSecretsIo;
}

function isReleasePlatform(
  value: string
): value is GithubSecretReleasePlatform {
  return RELEASE_PLATFORMS.includes(value);
}

function sourceOf(table: GithubSecretsTable, name: string): string {
  const item = table.items.find((entry) => entry.name === name);
  if (!item) throw new Error(`${name} is not in release/github-secrets.json`);
  return localSource(item);
}

function reportMissing(
  table: GithubSecretsTable,
  name: string,
  io: ReleaseSecretsIo
): void {
  io.stdout(
    `::error title=Missing secret::${name} is not set for this run; its value is: ${sourceOf(table, name)}`
  );
}

function writePrivateFile(filePath: string, data: string | Buffer): void {
  const fd = fs.openSync(filePath, 'wx', 0o600);
  try {
    if (process.platform !== 'win32') fs.fchmodSync(fd, 0o600);
    fs.writeFileSync(fd, data);
  } finally {
    fs.closeSync(fd);
  }
}

function decodeStrictBase64(value: string): Buffer | null {
  const compact = value.replace(/\s+/g, '');
  if (
    compact.length === 0 ||
    compact.length % 4 !== 0 ||
    !/^[A-Za-z0-9+/]*={0,2}$/.test(compact)
  ) {
    return null;
  }
  const decoded = Buffer.from(compact, 'base64');
  if (decoded.length === 0 || decoded.toString('base64') !== compact) {
    return null;
  }
  return decoded;
}

export function runReleaseSecrets(
  argv: readonly string[],
  {
    env = process.env,
    workspaceRoot = path.resolve(import.meta.dirname, '../..'),
    io = {
      stdout: (line) => process.stdout.write(`${line}\n`),
      stderr: (line) => process.stderr.write(`${line}\n`)
    }
  }: ReleaseSecretsOptions = {}
): number {
  const [command, ...rest] = argv;
  const table = loadGithubSecrets(workspaceRoot);

  if (command === 'names') {
    if (rest.length !== 0) {
      io.stderr(USAGE);
      return 2;
    }
    for (const name of releaseWorkflowNames(table)) io.stdout(name);
    return 0;
  }

  if (command === 'check') {
    if (rest.length !== 2) {
      io.stderr(USAGE);
      return 2;
    }
    const [platform, stage] = rest;
    if (!isReleasePlatform(platform)) {
      io.stderr(
        `release-secrets.ts: platform must be macos, windows or android, got '${platform}'`
      );
      return 2;
    }
    if (!RELEASE_STAGES.includes(stage)) {
      io.stderr(
        `release-secrets.ts: stage must be prepare or release, got '${stage}'`
      );
      return 2;
    }
    const required = requiredForRelease(table, platform, stage);
    let missing = 0;
    for (const name of required) {
      if (!env[name]) {
        reportMissing(table, name, io);
        missing += 1;
      }
    }
    if (missing > 0) {
      io.stderr(
        `${missing} required secret(s) missing for the ${platform} ${stage} stage; add them with 'just secrets-sync' (docs/release.md).`
      );
      return 1;
    }
    io.stdout(
      `All ${required.length} secrets for the ${platform} ${stage} stage are present.`
    );
    return 0;
  }

  if (command === 'stage') {
    if (rest.length !== 2) {
      io.stderr(USAGE);
      return 2;
    }
    const [platform, directory] = rest;
    if (!isReleasePlatform(platform)) {
      io.stderr(
        `release-secrets.ts: platform must be macos, windows or android, got '${platform}'`
      );
      return 2;
    }
    if (!directory) {
      io.stderr('release-secrets.ts: stage needs a directory');
      return 2;
    }
    const entries = stagedSigningEntries(platform);
    for (const entry of entries) {
      if (!env[entry.name]) {
        reportMissing(table, entry.name, io);
        return 1;
      }
    }
    const prepared: { file: string; data: string | Buffer }[] = [];
    for (const entry of entries) {
      const value = env[entry.name] as string;
      const file =
        typeof entry.file === 'function' ? entry.file(env) : entry.file;
      if (entry.encoding === 'base64') {
        const decoded = decodeStrictBase64(value);
        if (!decoded) {
          io.stdout(
            `::error title=Invalid secret::${entry.name} is not base64; its value is: ${sourceOf(table, entry.name)}`
          );
          return 1;
        }
        prepared.push({ file, data: decoded });
      } else {
        prepared.push({ file, data: value });
      }
    }
    fs.mkdirSync(directory, { recursive: true, mode: 0o700 });
    if (process.platform !== 'win32') fs.chmodSync(directory, 0o700);
    try {
      for (const item of prepared) {
        writePrivateFile(path.join(directory, item.file), item.data);
      }
    } catch (error) {
      fs.rmSync(directory, { recursive: true, force: true });
      throw error;
    }
    io.stdout(`Staged ${platform} signing secrets into ${directory}`);
    return 0;
  }

  io.stderr(USAGE);
  return 2;
}

if (import.meta.main) {
  process.exitCode = runReleaseSecrets(process.argv.slice(2));
}
