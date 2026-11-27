/**
 * Keep dependency bots from changing Mod runtime inputs (ADR-0010).
 *
 * The NuGet allowlist controls direct updates; this checks the resulting diff,
 * including transitive lockfile churn. It does not certify game compatibility.
 *
 * Self-contained (Node built-ins only): `.github/workflows/checks.yml` checks
 * out this file from `$BASE_SHA` into `$RUNNER_TEMP` and runs it there so a
 * PR cannot tamper with its own boundary check.
 */
import { execFileSync, spawnSync } from 'node:child_process';

export const TEST_TOOLS = new Set([
  'Microsoft.NET.Test.Sdk',
  'xunit',
  'xunit.runner.visualstudio'
]);
export const CENTRAL_PACKAGES = 'bazaarplusplus-mod/Directory.Packages.props';

interface XmlElement {
  tag: string;
  attrs: Map<string, string>;
  text: string;
  tail: string;
  children: XmlElement[];
}

export class XmlParseError extends Error {}

function decodeEntities(raw: string): string {
  return raw.replace(
    /&(lt|gt|amp|apos|quot|#x[0-9a-fA-F]+|#[0-9]+);/g,
    (match, body: string) => {
      switch (body) {
        case 'lt':
          return '<';
        case 'gt':
          return '>';
        case 'amp':
          return '&';
        case 'apos':
          return "'";
        case 'quot':
          return '"';
        default: {
          const code = body.startsWith('#x')
            ? Number.parseInt(body.slice(2), 16)
            : Number.parseInt(body.slice(1), 10);
          if (!Number.isFinite(code) || code < 0 || code > 0x10ffff) {
            throw new XmlParseError(`Invalid character reference: ${match}`);
          }
          return String.fromCodePoint(code);
        }
      }
    }
  );
}

function validateTextChunk(chunk: string): void {
  if (chunk.includes(']]>')) {
    throw new XmlParseError('Unexpected ]]> in text');
  }
  const stripped = chunk.replace(
    /&(lt|gt|amp|apos|quot|#x[0-9a-fA-F]+|#[0-9]+);/g,
    ''
  );
  if (stripped.includes('&')) {
    throw new XmlParseError('Unescaped & in XML text');
  }
}

function parseXml(source: string): XmlElement {
  let index = 0;
  const length = source.length;
  if (source.startsWith('\ufeff')) index = 1;

  const isNameStart = (ch: string): boolean => /[A-Za-z_:]/.test(ch);
  const isNameChar = (ch: string): boolean => /[A-Za-z0-9_.:-]/.test(ch);
  const skipWs = (): void => {
    while (index < length && /\s/.test(source[index])) index += 1;
  };
  const readName = (): string => {
    const start = index;
    if (index >= length || !isNameStart(source[index])) {
      throw new XmlParseError(`Expected XML name at offset ${index}`);
    }
    index += 1;
    while (index < length && isNameChar(source[index])) index += 1;
    return source.slice(start, index);
  };
  const skipComment = (): void => {
    const end = source.indexOf('-->', index + 4);
    if (end === -1) throw new XmlParseError('Unclosed XML comment');
    if (source.slice(index + 4, end).includes('--')) {
      throw new XmlParseError('Invalid -- inside XML comment');
    }
    index = end + 3;
  };
  const skipProcessingInstruction = (): void => {
    const end = source.indexOf('?>', index + 2);
    if (end === -1) {
      throw new XmlParseError('Unclosed processing instruction');
    }
    index = end + 2;
  };
  const skipMisc = (): void => {
    while (index < length) {
      skipWs();
      if (source.startsWith('<!--', index)) {
        skipComment();
      } else if (source.startsWith('<?', index)) {
        skipProcessingInstruction();
      } else {
        break;
      }
    }
  };
  const parseElement = (): XmlElement => {
    if (source[index] !== '<') {
      throw new XmlParseError(`Expected < at offset ${index}`);
    }
    index += 1;
    const tag = readName();
    const attrs = new Map<string, string>();
    while (index < length) {
      const beforeWs = index;
      skipWs();
      if (source.startsWith('/>', index)) {
        index += 2;
        return { tag, attrs, text: '', tail: '', children: [] };
      }
      if (source[index] === '>') {
        index += 1;
        break;
      }
      if (index === beforeWs) {
        throw new XmlParseError(
          `Missing whitespace before attribute in <${tag}>`
        );
      }
      const attrName = readName();
      if (attrs.has(attrName)) {
        throw new XmlParseError(`Duplicate attribute ${attrName} on <${tag}>`);
      }
      skipWs();
      if (source[index] !== '=') {
        throw new XmlParseError(`Expected = after attribute ${attrName}`);
      }
      index += 1;
      skipWs();
      const quote = source[index];
      if (quote !== '"' && quote !== "'") {
        throw new XmlParseError(`Expected quoted value for ${attrName}`);
      }
      index += 1;
      const valueStart = index;
      const valueEnd = source.indexOf(quote, valueStart);
      if (valueEnd === -1) {
        throw new XmlParseError(`Unclosed attribute value for ${attrName}`);
      }
      const rawValue = source.slice(valueStart, valueEnd);
      if (rawValue.includes('<')) {
        throw new XmlParseError(`Illegal < in attribute value for ${attrName}`);
      }
      validateTextChunk(rawValue);
      attrs.set(attrName, decodeEntities(rawValue));
      index = valueEnd + 1;
    }

    const element: XmlElement = {
      tag,
      attrs,
      text: '',
      tail: '',
      children: []
    };
    const appendText = (chunk: string): void => {
      if (element.children.length === 0) {
        element.text += chunk;
      } else {
        element.children[element.children.length - 1].tail += chunk;
      }
    };

    while (index < length) {
      if (source.startsWith('</', index)) {
        index += 2;
        const closeTag = readName();
        skipWs();
        if (source[index] !== '>') {
          throw new XmlParseError(`Unclosed end tag </${closeTag}>`);
        }
        index += 1;
        if (closeTag !== tag) {
          throw new XmlParseError(
            `Mismatched closing tag </${closeTag}> for <${tag}>`
          );
        }
        return element;
      }
      if (source.startsWith('<!--', index)) {
        skipComment();
      } else if (source.startsWith('<![CDATA[', index)) {
        const end = source.indexOf(']]>', index + 9);
        if (end === -1) throw new XmlParseError('Unclosed CDATA section');
        appendText(source.slice(index + 9, end));
        index = end + 3;
      } else if (source.startsWith('<?', index)) {
        skipProcessingInstruction();
      } else if (source[index] === '<') {
        element.children.push(parseElement());
      } else {
        const nextTag = source.indexOf('<', index);
        if (nextTag === -1) {
          throw new XmlParseError(`Unclosed element <${tag}>`);
        }
        const chunk = source.slice(index, nextTag);
        validateTextChunk(chunk);
        appendText(decodeEntities(chunk));
        index = nextTag;
      }
    }
    throw new XmlParseError(`Unclosed element <${tag}>`);
  };

  skipMisc();
  if (index >= length || source[index] !== '<') {
    throw new XmlParseError('Missing root XML element');
  }
  const root = parseElement();
  skipMisc();
  if (index !== length) {
    throw new XmlParseError(`Unexpected trailing content at offset ${index}`);
  }
  return root;
}

type StructureNode = [
  string,
  [string, string][],
  string,
  string,
  StructureNode[]
];

export function packageStructure(source: string): string {
  const root = parseXml(source);
  const visit = (element: XmlElement): StructureNode => {
    const attrs = new Map(element.attrs);
    if (
      element.tag === 'PackageVersion' &&
      TEST_TOOLS.has(attrs.get('Include') ?? '') &&
      attrs.has('Version')
    ) {
      attrs.set('Version', '<test-tool-version>');
    }
    return [
      element.tag,
      [...attrs.entries()].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
      element.text.trim(),
      element.tail.trim(),
      element.children.map(visit)
    ];
  };
  return JSON.stringify(visit(root));
}

export type FileChange = readonly [before: string | null, after: string | null];

export function validateChanges(changes: Record<string, FileChange>): string[] {
  const problems: string[] = [];
  for (const [filePath, [before, after]] of Object.entries(changes)) {
    if (!filePath.startsWith('bazaarplusplus-mod/')) continue;
    if (filePath !== CENTRAL_PACKAGES || before === null || after === null) {
      problems.push(`${filePath}: requires manual Mod dependency maintenance`);
      continue;
    }
    try {
      if (packageStructure(before) !== packageStructure(after)) {
        problems.push(
          `${filePath}: only existing test-tool versions may change`
        );
      }
    } catch (error) {
      if (error instanceof XmlParseError) {
        problems.push(`${filePath}: invalid MSBuild XML`);
      } else {
        throw error;
      }
    }
  }
  return problems;
}

function git(args: readonly string[]): string {
  return execFileSync('git', args, { encoding: 'utf8' });
}

function readFileAtRevision(revision: string, filePath: string): string | null {
  const result = spawnSync('git', ['show', `${revision}:${filePath}`], {
    encoding: 'utf8'
  });
  return result.status === 0 ? result.stdout : null;
}

function main(argv: readonly string[]): number {
  if (argv.length !== 2) {
    process.stderr.write(
      'Usage: check-mod-dependency-update.ts <base> <head>\n'
    );
    return 2;
  }
  const [baseArg, headArg] = argv;
  // Resolve revisions first: a missing commit must never look like an empty diff.
  const baseRev = git(['rev-parse', '--verify', `${baseArg}^{commit}`]).trim();
  const headRev = git(['rev-parse', '--verify', `${headArg}^{commit}`]).trim();
  const mergeBase = git(['merge-base', baseRev, headRev]).trim();
  const names = git([
    'diff',
    '--name-only',
    '--no-renames',
    '-z',
    mergeBase,
    headRev,
    '--',
    'bazaarplusplus-mod/'
  ])
    .split('\0')
    .filter(Boolean);
  const changes: Record<string, FileChange> = {};
  for (const name of names) {
    changes[name] = [
      readFileAtRevision(mergeBase, name),
      readFileAtRevision(headRev, name)
    ];
  }
  const problems = validateChanges(changes);
  if (problems.length > 0) {
    process.stderr.write(
      `${problems.join('\n')}\nSee ADR-0010 and docs/development.md.\n`
    );
    return 1;
  }
  process.stdout.write(
    'Bot Mod changes are limited to test-tool versions; game compatibility was not tested.\n'
  );
  return 0;
}

if (import.meta.main) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(
      `${error instanceof Error ? error.message : String(error)}\n`
    );
    process.exitCode = 1;
  }
}
