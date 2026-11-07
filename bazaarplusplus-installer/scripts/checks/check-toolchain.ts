import fs from 'node:fs';
import process from 'node:process';

export function assertSupportedVersion(actual: string, expected: string): void {
  if (actual.trim() !== expected) {
    throw new Error(
      `Bun ${actual} is unsupported; package.json requires ${expected}`
    );
  }
}

function main(args: string[]): void {
  if (args.length !== 1) {
    throw new Error('Usage: check-toolchain.ts <bun-version>');
  }
  const packageJson = JSON.parse(fs.readFileSync('package.json', 'utf8')) as {
    engines: { bun: string };
  };
  const [version] = args;
  assertSupportedVersion(version, packageJson.engines.bun);
  console.log(`Toolchain: Bun ${version}`);
}

if (import.meta.main) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
