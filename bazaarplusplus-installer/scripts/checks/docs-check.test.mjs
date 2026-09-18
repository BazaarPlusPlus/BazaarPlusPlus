import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test, expect } from 'vitest';

import {
  checkCitedPaths,
  checkEntryMapCoverage,
  checkMarkdownLinks,
  extractCitedPaths,
  extractMarkdownLinks,
  listDocs,
  runDocsCheck
} from './docs-check.mjs';

function writeFile(rootDir, relativePath, content) {
  const filePath = path.join(rootDir, ...relativePath.split('/'));
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content);
}

function createFixtureRoot() {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-docs-check-'));
  writeFile(rootDir, 'CLAUDE.md', '# Claude\n');
  writeFile(rootDir, 'CONTEXT.md', '# Context\n');
  writeFile(rootDir, 'README.md', '# Readme\n');
  return rootDir;
}

// ---------- what counts as a cited path ----------

test('a symbol citation is not treated as a path', () => {
  const content =
    'The window config is `app.windows`, set via `DefaultStreamWorkflow.deriveSnapshot`.';
  expect(extractCitedPaths(content)).toEqual([]);
});

test('a file this repo does not own is not treated as a path', () => {
  const content =
    'Steam detection reads `steamapps/libraryfolders.vdf` and launches `TheBazaar.exe`.';
  expect(extractCitedPaths(content)).toEqual([]);
});

test('a root-relative source path is treated as a path', () => {
  const content =
    'Generated bindings live in `src/types/generated/commands.ts`.';
  expect(extractCitedPaths(content)).toEqual([
    { target: 'src/types/generated/commands.ts', line: 1 }
  ]);
});

// ---------- seam 1: cited paths exist ----------

test('a cited path with no file behind it fails, reported at its line', () => {
  const rootDir = createFixtureRoot();
  writeFile(
    rootDir,
    'CLAUDE.md',
    '# Claude\n\nSee `src/does-not-exist.rs` for details.\n'
  );

  const result = checkCitedPaths(rootDir);

  expect(result.failures).toHaveLength(1);
  expect(result.failures[0]).toMatchObject({ file: 'CLAUDE.md', line: 3 });
  expect(result.failures[0].message).toMatch(/src\/does-not-exist\.rs/);
});

test('a cited path that resolves passes', () => {
  const rootDir = createFixtureRoot();
  writeFile(rootDir, 'src/lib.rs', 'fn main() {}\n');
  writeFile(rootDir, 'docs/architecture.md', 'Entry point: `src/lib.rs`.\n');

  const result = checkCitedPaths(rootDir);

  expect(result.failures).toEqual([]);
  expect(result.checked).toBe(1);
});

test('an ADR may name code that no longer exists', () => {
  const rootDir = createFixtureRoot();
  writeFile(
    rootDir,
    'docs/adr/003-steam-only-launch.md',
    'It worked through `src/services/tempo.rs`, since removed.\n'
  );

  expect(checkCitedPaths(rootDir).failures).toEqual([]);
});

// ---------- seam 2: relative markdown links resolve ----------

test('absolute URLs and bare anchors are not link targets', () => {
  const content =
    '[site](https://example.com) and [here](#section) and [doc](other.md)';
  expect(extractMarkdownLinks(content)).toEqual([
    { target: 'other.md', line: 1 }
  ]);
});

test('a dead relative link fails and a live one passes', () => {
  const rootDir = createFixtureRoot();
  writeFile(rootDir, 'docs/adr/002-example.md', '# Two\n');
  writeFile(
    rootDir,
    'docs/adr/001-example.md',
    'See [two](002-example.md) and [gone](099-missing.md).\n'
  );

  const result = checkMarkdownLinks(rootDir);

  expect(result.failures).toEqual([
    {
      file: 'docs/adr/001-example.md',
      line: 1,
      message: 'link to `099-missing.md` does not resolve'
    }
  ]);
});

test('a link is resolved against the linking file, not the repository root', () => {
  const rootDir = createFixtureRoot();
  writeFile(rootDir, 'CONTEXT.md', '[Architecture](docs/architecture.md)\n');
  writeFile(rootDir, 'docs/architecture.md', '[ADR](adr/001-example.md)\n');
  writeFile(rootDir, 'docs/adr/001-example.md', '# One\n');

  expect(checkMarkdownLinks(rootDir).failures).toEqual([]);
});

// ---------- seam 3: CONTEXT.md reaches every topic ----------

test('a topic missing from the entry map fails', () => {
  const rootDir = createFixtureRoot();
  writeFile(rootDir, 'docs/architecture.md', '# Architecture\n');

  expect(checkEntryMapCoverage(rootDir).failures).toEqual([
    {
      file: 'docs/architecture.md',
      line: 1,
      message: 'topic is not linked from `CONTEXT.md`'
    }
  ]);
});

test('a topic linked from the entry map passes, and ADRs need no entry', () => {
  const rootDir = createFixtureRoot();
  writeFile(rootDir, 'CONTEXT.md', '[Architecture](docs/architecture.md)\n');
  writeFile(rootDir, 'docs/architecture.md', '# Architecture\n');
  writeFile(rootDir, 'docs/adr/001-example.md', '# One\n');

  const result = checkEntryMapCoverage(rootDir);

  expect(result.failures).toEqual([]);
  expect(result.checked).toBe(1);
});

// ---------- runner ----------

test('listDocs tolerates a directory that does not exist yet', () => {
  const rootDir = createFixtureRoot();
  expect(listDocs(rootDir, 'docs/plans')).toEqual([]);
});

test('runDocsCheck reports failure once any seam fails, and passes a clean tree', () => {
  const rootDir = createFixtureRoot();
  writeFile(rootDir, 'CONTEXT.md', '[Architecture](docs/architecture.md)\n');
  writeFile(rootDir, 'src/lib.rs', 'fn main() {}\n');
  writeFile(rootDir, 'docs/architecture.md', 'Entry point: `src/lib.rs`.\n');

  expect(runDocsCheck(rootDir, { log: () => {} })).toBe(true);

  writeFile(rootDir, 'docs/orphan.md', '# Orphan\n');
  expect(runDocsCheck(rootDir, { log: () => {} })).toBe(false);
});
