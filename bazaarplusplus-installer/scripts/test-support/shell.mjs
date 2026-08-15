import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';

const projectDir = process.cwd();

// Resolve a usable bash. On Windows, `bash` is frequently absent from the PATH
// that npm spawns with (PowerShell/cmd), so fall back to the Git for Windows
// install before giving up.
export function resolveBashCommand() {
  if (process.platform !== 'win32') {
    return 'bash';
  }
  const candidates = [];
  if (process.env.BPP_BASH) {
    candidates.push(process.env.BPP_BASH);
  }
  try {
    const execPath = execFileSync('git', ['--exec-path'], {
      encoding: 'utf8'
    }).trim();
    const gitRoot = execPath.replace(/[/\\](mingw\d+|usr)[/\\].*$/i, '');
    if (gitRoot && gitRoot !== execPath) {
      candidates.push(`${gitRoot}/bin/bash.exe`);
    }
  } catch {
    // git not on PATH; fall back to the well-known install locations below.
  }
  candidates.push(
    'C:/Program Files/Git/bin/bash.exe',
    'C:/Program Files (x86)/Git/bin/bash.exe'
  );
  return (
    candidates.find((candidate) => candidate && existsSync(candidate)) ?? 'bash'
  );
}

const bashCommand = resolveBashCommand();

// Git Bash treats `E:\foo` as a relative path, so paths handed to build.sh
// must be POSIX-style absolute (`/e/foo`) on Windows.
export function toBashPath(p) {
  return p
    .replace(/\\/g, '/')
    .replace(/^([A-Za-z]):/, (_, drive) => `/${drive.toLowerCase()}`);
}

export function runShell(script) {
  return execFileSync(bashCommand, ['-lc', script], {
    cwd: projectDir,
    encoding: 'utf8',
    timeout: 120000
  });
}
