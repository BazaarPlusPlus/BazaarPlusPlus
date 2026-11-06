import fs from 'node:fs';
import process from 'node:process';

// A tool version as [major, minor, patch].
type ToolVersion = [number, number, number];

// The package.json fields the toolchain check reads.
interface PackageEngines {
  engines: { node: string; npm: string };
}

function parseVersion(value: string): ToolVersion {
  const match = value
    .trim()
    .replace(/^v/, '')
    .match(/^(\d+)(?:\.(\d+))?(?:\.(\d+))?/);
  if (!match) throw new Error(`Invalid tool version: ${value}`);
  return [Number(match[1]), Number(match[2] ?? 0), Number(match[3] ?? 0)];
}

function compareVersions(left: ToolVersion, right: ToolVersion): number {
  for (let index = 0; index < 3; index += 1) {
    if (left[index] !== right[index]) return left[index] - right[index];
  }
  return 0;
}

export function assertSupportedVersion(
  name: string,
  actualValue: string,
  range: string
): void {
  const actual = parseVersion(actualValue);
  const constraints = range.trim().split(/\s+/);
  const supported = constraints.every((constraint) => {
    // An empty range has no match; reading it throws, as it always has.
    const match = constraint.match(/^(>=|<=|>|<|=)?(.+)$/) as RegExpMatchArray;
    const operator = match[1] ?? '=';
    const comparison = compareVersions(actual, parseVersion(match[2]));
    if (operator === '>=') return comparison >= 0;
    if (operator === '<=') return comparison <= 0;
    if (operator === '>') return comparison > 0;
    if (operator === '<') return comparison < 0;
    return comparison === 0;
  });
  if (!supported) {
    throw new Error(
      `${name} ${actualValue.replace(/^v/, '')} is unsupported; package.json requires ${range}`
    );
  }
}

function main(args: string[]): void {
  if (args.length !== 2) {
    throw new Error('Usage: check-toolchain.ts <node-version> <npm-version>');
  }
  const packageJson = JSON.parse(
    fs.readFileSync('package.json', 'utf8')
  ) as PackageEngines;
  const [nodeVersion, npmVersion] = args;
  console.log(
    `Toolchain: Node ${nodeVersion.replace(/^v/, '')}, npm ${npmVersion}`
  );
  assertSupportedVersion('node', nodeVersion, packageJson.engines.node);
  assertSupportedVersion('npm', npmVersion, packageJson.engines.npm);
}

if (import.meta.main) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
