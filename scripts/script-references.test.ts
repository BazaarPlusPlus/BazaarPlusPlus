import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';

import { runGit } from './git-command.ts';

// Renamed scripts can break scheduled or release jobs that run long after merge.
const root = path.resolve(import.meta.dirname, '..');
const tracked = runGit(['ls-files', '-z'], { cwd: root })
  .split('\0')
  .filter((file) => file && fs.existsSync(path.join(root, file)));
const basenames = new Set(tracked.map((name) => path.posix.basename(name)));

const LAUNCHERS = [
  /^\.github\/workflows\/[^/]+\.ya?ml$/,
  /(^|\/)JUSTFILE$/,
  /\.just$/,
  /\.sh$/,
  /\.(csproj|props|targets)$/,
  /\.gradle\.kts$/,
  /\.java$/,
  /(^|\/)package\.json$/,
  /(^|\/)tauri\.conf\.json$/,
  /^lefthook\.yml$/
];
// Prose names many files; only its `bun <script>` commands are checked.
const PROSE = /\.md$/;

// A path segment may hold a `${{ expression }}`, which contains spaces.
const SCRIPT = String.raw`(?:\$\{\{[^}]*\}\}|[^\s"'\x60;|&])+?\.(?:[cm]?js|ts)`;
const BUN_COMMAND = new RegExp(
  String.raw`\b(?:bun|node)\s+(?:--[\w-]+(?:=\S+)?\s+)*["']?(${SCRIPT})(?=["'\s);|&\x60]|$)`,
  'g'
);
const QUOTED_PATH = new RegExp(String.raw`["'](${SCRIPT})["']`, 'g');
const PREFIX = /^(?:\$\{\{[^}]*\}\}|\$\([^)]*\)|\$\{?\w+\}?)\/|^\.{1,2}\//;

interface ScriptReference {
  file: string;
  script: string;
  target: string;
}

function referencedScripts(): ScriptReference[] {
  const found: ScriptReference[] = [];
  for (const file of tracked) {
    const prose = PROSE.test(file);
    if (!prose && !LAUNCHERS.some((pattern) => pattern.test(file))) continue;
    const text = fs
      .readFileSync(path.join(root, file), 'utf8')
      .replaceAll('&quot;', '"');
    for (const pattern of prose ? [BUN_COMMAND] : [BUN_COMMAND, QUOTED_PATH])
      for (const [, script] of text.matchAll(pattern)) {
        if (/[*{}<>]/.test(script.replace(/\$\{\{[^}]*\}\}/g, ''))) continue;
        let target = script;
        while (PREFIX.test(target)) target = target.replace(PREFIX, '');
        // Publishable package manifests declare built dist/ outputs in exports/main/types.
        if (
          /(^|\/)package\.json$/.test(file) &&
          pattern === QUOTED_PATH &&
          target.startsWith('dist/')
        )
          continue;
        found.push({ file, script, target });
      }
  }
  return found;
}

function exists(target: string): boolean {
  return target.includes('/')
    ? tracked.some((name) => name === target || name.endsWith(`/${target}`))
    : basenames.has(target);
}

test('every script path a launcher or a command in the docs names is tracked', () => {
  const referenced = referencedScripts();
  // A pattern that silently stops matching would pass the check below.
  expect(referenced.length).toBeGreaterThan(100);
  expect(referenced.filter(({ target }) => !exists(target))).toEqual([]);
});

test('root tooling is TypeScript; no .mjs remains', () => {
  expect(tracked.filter((name) => name.endsWith('.mjs'))).toEqual([]);
});
