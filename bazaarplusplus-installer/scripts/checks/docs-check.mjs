import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const ROOT_DOC_FILES = ['CLAUDE.md', 'CONTEXT.md', 'README.md'];
const REQUIRED_FRONTMATTER_KEYS = ['status', 'topic', 'last-verified'];
const LAST_VERIFIED_KEY = 'last-verified';
const CURRENT_STATUS = 'current';

// ---------- shared file helpers ----------

function readFile(rootDir, relativePath) {
  return fs.readFileSync(
    path.join(rootDir, ...relativePath.split('/')),
    'utf8'
  );
}

function isFile(rootDir, relativePath) {
  return !!fs
    .statSync(path.join(rootDir, ...relativePath.split('/')), {
      throwIfNoEntry: false
    })
    ?.isFile();
}

// Recursively lists every `.md` file under `rootDir/relDir`, returning
// repo-relative posix paths. A missing directory (docs/plans/ does not exist
// until the first plan is written) returns an empty list instead of throwing.
export function listMarkdownFiles(rootDir, relDir) {
  const dirPath = path.join(rootDir, ...relDir.split('/'));
  if (!fs.statSync(dirPath, { throwIfNoEntry: false })?.isDirectory()) {
    return [];
  }

  const matches = [];
  const walk = (absoluteDir, relativeDir) => {
    for (const dirent of fs.readdirSync(absoluteDir, { withFileTypes: true })) {
      const childAbsolute = path.join(absoluteDir, dirent.name);
      const childRelative = `${relativeDir}/${dirent.name}`;
      if (dirent.isDirectory()) {
        walk(childAbsolute, childRelative);
      } else if (dirent.isFile() && dirent.name.endsWith('.md')) {
        matches.push(childRelative);
      }
    }
  };
  walk(dirPath, relDir);
  return matches.sort();
}

// Current behavior lives directly under docs/. Lifecycle-specific material
// belongs to named subdirectories and is intentionally excluded here.
export function listCurrentDocFiles(rootDir) {
  const docsDir = path.join(rootDir, 'docs');
  if (!fs.statSync(docsDir, { throwIfNoEntry: false })?.isDirectory()) {
    return [];
  }

  return fs
    .readdirSync(docsDir, { withFileTypes: true })
    .filter((dirent) => dirent.isFile() && dirent.name.endsWith('.md'))
    .map((dirent) => `docs/${dirent.name}`)
    .sort();
}

function lineNumberAt(content, index) {
  let line = 1;
  for (let cursor = 0; cursor < index; cursor += 1) {
    if (content.charCodeAt(cursor) === 10) line += 1;
  }
  return line;
}

function countLines(content) {
  const lines = content.split('\n');
  if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
  return lines.length;
}

// ---------- assertion 1: frontmatter completeness ----------

const FRONTMATTER_PATTERN = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/;

export function parseFrontmatter(content) {
  const data = {};
  const match = content.match(FRONTMATTER_PATTERN);
  if (!match) return data;
  for (const line of match[1].split(/\r?\n/)) {
    const separatorIndex = line.indexOf(':');
    if (separatorIndex === -1) continue;
    const key = line.slice(0, separatorIndex).trim();
    const value = line.slice(separatorIndex + 1).trim();
    if (key) data[key] = value;
  }
  return data;
}

export function findMissingFrontmatterKeys(content, requiredKeys) {
  const data = parseFrontmatter(content);
  return requiredKeys.filter((key) => !data[key]);
}

function frontmatterScanTargets(rootDir) {
  return listCurrentDocFiles(rootDir);
}

export function checkFrontmatterCompleteness(rootDir) {
  const files = frontmatterScanTargets(rootDir);
  const failures = [];
  for (const relativePath of files) {
    const missing = findMissingFrontmatterKeys(
      readFile(rootDir, relativePath),
      REQUIRED_FRONTMATTER_KEYS
    );
    for (const key of missing) {
      failures.push({
        file: relativePath,
        line: 1,
        message: `missing frontmatter key \`${key}\``
      });
    }
  }
  return { checked: files.length, failures };
}

export function checkCurrentDocMetadata(rootDir) {
  const files = frontmatterScanTargets(rootDir);
  const completeness = checkFrontmatterCompleteness(rootDir);
  const failures = [...completeness.failures];
  const topics = new Map();

  for (const relativePath of files) {
    const metadata = parseFrontmatter(readFile(rootDir, relativePath));
    if (metadata.status && metadata.status !== CURRENT_STATUS) {
      failures.push({
        file: relativePath,
        line: 1,
        message: `status must be \`${CURRENT_STATUS}\`, found \`${metadata.status}\``
      });
    }
    if (!metadata.topic) continue;
    if (topics.has(metadata.topic)) {
      failures.push({
        file: relativePath,
        line: 1,
        message: `topic \`${metadata.topic}\` is already owned by \`${topics.get(metadata.topic)}\``
      });
    } else {
      topics.set(metadata.topic, relativePath);
    }
  }

  return { checked: files.length, failures };
}

// ---------- assertion 2: last-verified hash ancestry ----------

export function resolveAncestryRef({
  execFileSyncImpl = execFileSync,
  cwd
} = {}) {
  for (const ref of ['HEAD', 'origin/master', 'master']) {
    try {
      execFileSyncImpl('git', ['rev-parse', '--verify', '--quiet', ref], {
        cwd,
        stdio: ['ignore', 'ignore', 'ignore']
      });
      return ref;
    } catch {
      // Fall through to the next candidate ref.
    }
  }
  return null;
}

export function checkLastVerifiedHash(
  hash,
  { execFileSyncImpl = execFileSync, cwd, ref } = {}
) {
  try {
    execFileSyncImpl('git', ['cat-file', '-e', hash], {
      cwd,
      stdio: ['ignore', 'ignore', 'ignore']
    });
  } catch {
    return { ok: false, skipped: false, reason: 'is not a known Git object' };
  }

  if (!ref) {
    return {
      ok: true,
      skipped: true,
      reason: 'no current, origin/master, or local master ref available'
    };
  }

  try {
    execFileSyncImpl('git', ['merge-base', '--is-ancestor', hash, ref], {
      cwd,
      stdio: ['ignore', 'ignore', 'ignore']
    });
    return { ok: true, skipped: false };
  } catch {
    return {
      ok: false,
      skipped: false,
      reason: `is not an ancestor of ${ref}`
    };
  }
}

export function checkLastVerifiedHashes(
  rootDir,
  { execFileSyncImpl = execFileSync } = {}
) {
  const files = frontmatterScanTargets(rootDir);
  const ref = resolveAncestryRef({ execFileSyncImpl, cwd: rootDir });
  const failures = [];
  let checked = 0;
  let skipped = 0;

  for (const relativePath of files) {
    const hash = parseFrontmatter(readFile(rootDir, relativePath))[
      LAST_VERIFIED_KEY
    ];
    // A missing key is already reported by checkFrontmatterCompleteness.
    if (!hash) continue;

    checked += 1;
    const result = checkLastVerifiedHash(hash, {
      execFileSyncImpl,
      cwd: rootDir,
      ref
    });
    if (result.skipped) {
      skipped += 1;
      continue;
    }
    if (!result.ok) {
      failures.push({
        file: relativePath,
        line: 1,
        message: `\`${LAST_VERIFIED_KEY}: ${hash}\` ${result.reason}`
      });
    }
  }
  return { checked, skipped, failures };
}

// ---------- shared: backticked path/citation extraction ----------

// Matches an inline-code span whose entire content looks like a
// `path.ext`, optionally followed by `:N` or `:N-M`. The extension must
// start with a letter so an incidental `word:number` span (e.g. an address
// like `127.0.0.1:17654`) is never mistaken for a citation.
const BACKTICK_REFERENCE_PATTERN =
  /`([\w./-]+\.[A-Za-z][\w-]*)(?::(\d+)(?:-(\d+))?)?`/g;

// The documentation contract (CLAUDE.md; ADR-007) has current docs cite code by
// symbol rather than by line: `DefaultStreamWorkflow.deriveSnapshot`,
// `app.windows`, `UserConfig.BetaKey`. Those spans are syntactically
// indistinguishable from `path.ext` (dotted, letter-led final segment), and
// prose also names external, non-repo paths the same way (a Steam client
// file: `steamapps/libraryfolders.vdf`; the game's own binary: `TheBazaar.exe`).
// A real citation always uses one of these actual source/config/doc
// extensions. A `:N`/`:N-M` suffix is the second signal: the contract keeps
// `file:line` valid specifically for a single-line literal fact (e.g.
// `build.sh:43`), which is often a bare root filename with no directory
// component, so a line suffix is accepted on its own. Without one, a bare
// dotted span is required to also contain a directory component — that's
// what tells `commands.ts` (shorthand for a symbol, not a citation) apart
// from `src/types/generated/commands.ts` (an actual path).
const CITABLE_EXTENSIONS = new Set([
  'rs',
  'ts',
  'tsx',
  'js',
  'mjs',
  'cjs',
  'json',
  'toml',
  'sh',
  'md',
  'css',
  'yml',
  'yaml',
  'lock',
  'plist',
  'c',
  'h'
]);

function isCitablePath(candidatePath, hasLineSuffix) {
  if (!hasLineSuffix && !candidatePath.includes('/')) return false;
  const extension = candidatePath.slice(candidatePath.lastIndexOf('.') + 1);
  return CITABLE_EXTENSIONS.has(extension.toLowerCase());
}

export function extractBacktickReferences(content) {
  const references = [];
  for (const match of content.matchAll(BACKTICK_REFERENCE_PATTERN)) {
    const [raw, filePath, startText, endText] = match;
    const hasLineSuffix = startText !== undefined;
    if (!isCitablePath(filePath, hasLineSuffix)) continue;
    const start = hasLineSuffix ? Number(startText) : undefined;
    references.push({
      raw,
      path: filePath,
      start,
      end: endText !== undefined ? Number(endText) : start,
      line: lineNumberAt(content, match.index)
    });
  }
  return references;
}

// ---------- assertion 3: code citation line ranges ----------

export function checkCitationBounds(citation, lineCount) {
  if (citation.start < 1) {
    return {
      ok: false,
      reason: `starts at line ${citation.start}, before line 1`
    };
  }
  if (citation.end < citation.start) {
    return {
      ok: false,
      reason: `end line ${citation.end} precedes start line ${citation.start}`
    };
  }
  if (citation.end > lineCount) {
    return {
      ok: false,
      reason: `line ${citation.end} is past ${citation.path}'s last line (${lineCount})`
    };
  }
  return { ok: true };
}

function citationScanTargets(rootDir) {
  return [
    ...ROOT_DOC_FILES,
    ...listCurrentDocFiles(rootDir),
    ...listMarkdownFiles(rootDir, 'docs/adr'),
    ...listMarkdownFiles(rootDir, 'docs/agents'),
    ...listMarkdownFiles(rootDir, 'docs/plans')
  ];
}

export function checkCodeCitations(rootDir) {
  const files = citationScanTargets(rootDir);
  const lineCounts = new Map();
  const failures = [];
  let checked = 0;

  for (const relativePath of files) {
    const citations = extractBacktickReferences(
      readFile(rootDir, relativePath)
    ).filter((reference) => reference.start !== undefined);

    for (const citation of citations) {
      // A citation whose base path does not resolve to a real file is out of
      // scope here: assertion 4 (checkPathExistence) owns ghost-path
      // reporting for the files it scans, and a bounds check has nothing to
      // measure against without a target file.
      if (!isFile(rootDir, citation.path)) continue;

      checked += 1;
      if (!lineCounts.has(citation.path)) {
        lineCounts.set(
          citation.path,
          countLines(readFile(rootDir, citation.path))
        );
      }
      const result = checkCitationBounds(
        citation,
        lineCounts.get(citation.path)
      );
      if (!result.ok) {
        failures.push({
          file: relativePath,
          line: citation.line,
          // citation.raw already carries its own backticks (it is the full
          // regex match, delimiters included) — do not wrap it again.
          message: `${citation.raw} ${result.reason}`
        });
      }
    }
  }
  return { checked, failures };
}

// ---------- assertion 4: backticked path existence ----------

// docs/adr/ is deliberately excluded here, not merely skipped by accident.
// An ADR must be free to name deleted code without turning a clean tree red
// — e.g. ADR-003 documents the removed Tempo launch flow, and that
// historical mention is the record's value. Do not widen this scope to
// include docs/adr/.
function pathExistenceScanTargets(rootDir) {
  return [
    ...ROOT_DOC_FILES,
    ...listCurrentDocFiles(rootDir),
    ...listMarkdownFiles(rootDir, 'docs/agents'),
    ...listMarkdownFiles(rootDir, 'docs/plans')
  ];
}

export function checkPathExistence(rootDir) {
  const files = pathExistenceScanTargets(rootDir);
  const failures = [];
  let checked = 0;

  for (const relativePath of files) {
    const references = extractBacktickReferences(
      readFile(rootDir, relativePath)
    );
    for (const reference of references) {
      checked += 1;
      if (!isFile(rootDir, reference.path)) {
        failures.push({
          file: relativePath,
          line: reference.line,
          message: `\`${reference.path}\` does not resolve to a file`
        });
      }
    }
  }
  return { checked, failures };
}

// ---------- assertion 5: relative markdown links ----------

const MARKDOWN_LINK_PATTERN = /\]\(([^)]+)\)/g;

function isRelativeMarkdownLink(target) {
  if (!target || target.startsWith('#')) return false;
  if (/^[a-z][a-z0-9+.-]*:/i.test(target)) return false; // scheme:, mailto:, etc.
  const [pathPart] = target.split('#');
  return pathPart.toLowerCase().endsWith('.md');
}

export function extractMarkdownLinks(content) {
  const links = [];
  for (const match of content.matchAll(MARKDOWN_LINK_PATTERN)) {
    const target = match[1].trim();
    if (!isRelativeMarkdownLink(target)) continue;
    links.push({
      raw: match[0],
      target,
      line: lineNumberAt(content, match.index)
    });
  }
  return links;
}

export function resolveMarkdownLinkPath(rootDir, fromFile, target) {
  const [pathPart] = target.split('#');
  const fromDir = path.dirname(path.join(rootDir, ...fromFile.split('/')));
  return path.join(fromDir, ...pathPart.split('/'));
}

function markdownLinkScanTargets(rootDir) {
  return [
    ...pathExistenceScanTargets(rootDir),
    ...listMarkdownFiles(rootDir, 'docs/adr')
  ];
}

export function checkMarkdownLinks(rootDir) {
  const files = markdownLinkScanTargets(rootDir);
  const failures = [];
  let checked = 0;

  for (const relativePath of files) {
    const links = extractMarkdownLinks(readFile(rootDir, relativePath));
    for (const link of links) {
      checked += 1;
      const resolved = resolveMarkdownLinkPath(
        rootDir,
        relativePath,
        link.target
      );
      if (!fs.statSync(resolved, { throwIfNoEntry: false })?.isFile()) {
        failures.push({
          file: relativePath,
          line: link.line,
          message: `link to \`${link.target}\` does not resolve`
        });
      }
    }
  }
  return { checked, failures };
}

// CONTEXT.md is the sole topic map. Every current document must be reachable
// from it so a new file cannot silently increase cognitive load.
export function checkContextTopicCoverage(rootDir) {
  const currentFiles = listCurrentDocFiles(rootDir);
  const linkedPaths = new Set(
    extractMarkdownLinks(readFile(rootDir, 'CONTEXT.md')).map((link) =>
      path.resolve(resolveMarkdownLinkPath(rootDir, 'CONTEXT.md', link.target))
    )
  );
  const failures = [];

  for (const relativePath of currentFiles) {
    const absolutePath = path.resolve(
      path.join(rootDir, ...relativePath.split('/'))
    );
    if (!linkedPaths.has(absolutePath)) {
      failures.push({
        file: relativePath,
        line: 1,
        message: 'current topic is not linked from `CONTEXT.md`'
      });
    }
  }

  return { checked: currentFiles.length, failures };
}

// ---------- runner ----------

const ASSERTIONS = [
  {
    name: 'Current topic metadata (status/topic/last-verified)',
    run: (rootDir) => checkCurrentDocMetadata(rootDir)
  },
  {
    name: 'CONTEXT.md current-topic coverage',
    run: (rootDir) => checkContextTopicCoverage(rootDir)
  },
  {
    name: 'last-verified hash ancestry',
    run: (rootDir, { execFileSyncImpl }) =>
      checkLastVerifiedHashes(rootDir, { execFileSyncImpl })
  },
  {
    name: 'Code citation line ranges',
    run: (rootDir) => checkCodeCitations(rootDir)
  },
  {
    name: 'Backticked path existence',
    run: (rootDir) => checkPathExistence(rootDir)
  },
  {
    name: 'Relative markdown links',
    run: (rootDir) => checkMarkdownLinks(rootDir)
  }
];

export function runDocsCheck(
  rootDir,
  { execFileSyncImpl = execFileSync, log = console.log } = {}
) {
  let ok = true;

  for (const assertion of ASSERTIONS) {
    const result = assertion.run(rootDir, { execFileSyncImpl });
    if (result.failures.length > 0) {
      ok = false;
      log(`==> ${assertion.name}: ${result.failures.length} failure(s)`);
      for (const failure of result.failures) {
        log(`  ${failure.file}:${failure.line}: ${failure.message}`);
      }
    } else {
      const skippedNote =
        result.skipped > 0
          ? `, ${result.skipped} skipped (no ancestry ref)`
          : '';
      log(
        `==> ${assertion.name}: ok (${result.checked} checked${skippedNote})`
      );
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
