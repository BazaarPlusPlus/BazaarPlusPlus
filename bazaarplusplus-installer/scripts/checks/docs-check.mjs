import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const ROOT_DOCS = ['CLAUDE.md', 'CONTEXT.md', 'README.md'];

// Docs cite code by symbol (`DefaultStreamWorkflow.deriveSnapshot`,
// `app.windows`) and prose names files this repo does not own
// (`steamapps/libraryfolders.vdf`, `TheBazaar.exe`). Both are shaped like a
// dotted path, so a span counts as a repo path only with one of these real
// source/config extensions plus a directory component — the second signal that
// separates `src/api/commandClient.ts` from bare `commands.ts` shorthand.
const CITABLE_EXTENSIONS = new Set(
  'rs ts tsx js mjs cjs json toml sh md css yml yaml lock plist c h'.split(' ')
);

const BACKTICK_PATH_PATTERN = /`([\w./-]+\.[A-Za-z][\w-]*)`/g;
const MARKDOWN_LINK_PATTERN = /\]\(([^)]+)\)/g;

const absolute = (rootDir, relativePath) =>
  path.join(rootDir, ...relativePath.split('/'));
const read = (rootDir, relativePath) =>
  fs.readFileSync(absolute(rootDir, relativePath), 'utf8');
const isFile = (filePath) =>
  !!fs.statSync(filePath, { throwIfNoEntry: false })?.isFile();
const lineAt = (content, index) => content.slice(0, index).split('\n').length;

// A missing directory is normal: docs/plans/ and docs/archive/ appear only once
// there is something to put in them.
export function listDocs(rootDir, relDir) {
  const dirPath = absolute(rootDir, relDir);
  if (!fs.statSync(dirPath, { throwIfNoEntry: false })?.isDirectory())
    return [];
  return fs
    .readdirSync(dirPath, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
    .map((entry) => `${relDir}/${entry.name}`)
    .sort();
}

export function extractCitedPaths(content) {
  const cited = [];
  for (const match of content.matchAll(BACKTICK_PATH_PATTERN)) {
    const target = match[1];
    const extension = target.slice(target.lastIndexOf('.') + 1).toLowerCase();
    if (!target.includes('/') || !CITABLE_EXTENSIONS.has(extension)) continue;
    cited.push({ target, line: lineAt(content, match.index) });
  }
  return cited;
}

export function extractMarkdownLinks(content) {
  const links = [];
  for (const match of content.matchAll(MARKDOWN_LINK_PATTERN)) {
    const target = match[1].trim();
    if (!target || target.startsWith('#')) continue;
    if (/^[a-z][a-z0-9+.-]*:/i.test(target)) continue; // https:, mailto:, …
    const [pathPart] = target.split('#');
    if (!pathPart.toLowerCase().endsWith('.md')) continue;
    links.push({ target: pathPart, line: lineAt(content, match.index) });
  }
  return links;
}

// Documents that must describe the tree as it is now. docs/adr/ is absent on
// purpose: a decision record must be free to name code that has since been
// deleted — ADR-003 documents the removed Tempo launch flow, and that
// historical mention is the record's value.
const currentDocs = (rootDir) => [
  ...ROOT_DOCS,
  ...listDocs(rootDir, 'docs'),
  ...listDocs(rootDir, 'docs/plans')
];

export function checkCitedPaths(rootDir) {
  const files = currentDocs(rootDir);
  const failures = [];
  let checked = 0;

  for (const file of files) {
    for (const cited of extractCitedPaths(read(rootDir, file))) {
      checked += 1;
      if (!isFile(absolute(rootDir, cited.target))) {
        failures.push({
          file,
          line: cited.line,
          message: `\`${cited.target}\` does not resolve to a file`
        });
      }
    }
  }
  return { checked, failures };
}

export function checkMarkdownLinks(rootDir) {
  const files = [
    ...currentDocs(rootDir),
    ...listDocs(rootDir, 'docs/adr'),
    ...listDocs(rootDir, 'docs/archive')
  ];
  const failures = [];
  let checked = 0;

  for (const file of files) {
    const fromDir = path.dirname(absolute(rootDir, file));
    for (const link of extractMarkdownLinks(read(rootDir, file))) {
      checked += 1;
      if (!isFile(path.join(fromDir, ...link.target.split('/')))) {
        failures.push({
          file,
          line: link.line,
          message: `link to \`${link.target}\` does not resolve`
        });
      }
    }
  }
  return { checked, failures };
}

// CONTEXT.md is the sole entry map, so an unlinked topic is one an agent can
// only find by luck.
export function checkEntryMapCoverage(rootDir) {
  const linked = new Set(
    extractMarkdownLinks(read(rootDir, 'CONTEXT.md')).map((link) =>
      path.resolve(path.dirname(absolute(rootDir, 'CONTEXT.md')), link.target)
    )
  );
  const topics = listDocs(rootDir, 'docs');
  const failures = topics
    .filter((topic) => !linked.has(path.resolve(absolute(rootDir, topic))))
    .map((topic) => ({
      file: topic,
      line: 1,
      message: 'topic is not linked from `CONTEXT.md`'
    }));
  return { checked: topics.length, failures };
}

const ASSERTIONS = [
  { name: 'Cited path existence', run: checkCitedPaths },
  { name: 'Relative markdown links', run: checkMarkdownLinks },
  { name: 'CONTEXT.md topic coverage', run: checkEntryMapCoverage }
];

export function runDocsCheck(rootDir, { log = console.log } = {}) {
  let ok = true;
  for (const assertion of ASSERTIONS) {
    const { checked, failures } = assertion.run(rootDir);
    if (failures.length > 0) {
      ok = false;
      log(`==> ${assertion.name}: ${failures.length} failure(s)`);
      for (const failure of failures) {
        log(`  ${failure.file}:${failure.line}: ${failure.message}`);
      }
    } else {
      log(`==> ${assertion.name}: ok (${checked} checked)`);
    }
  }
  return ok;
}

if (import.meta.main) {
  const rootDir = path.resolve(import.meta.dirname, '..', '..');
  try {
    const ok = runDocsCheck(rootDir);
    console.log(ok ? 'docs-check: ok' : 'docs-check: failed');
    process.exitCode = ok ? 0 : 1;
  } catch (error) {
    console.error(
      `docs-check: ${error instanceof Error ? error.message : String(error)}`
    );
    process.exitCode = 1;
  }
}
