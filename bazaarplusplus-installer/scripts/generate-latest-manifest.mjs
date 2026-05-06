import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PLATFORMS = ['windows-x86_64', 'darwin-aarch64'];

function readJsonIfExists(filePath) {
  if (!fs.existsSync(filePath)) {
    return null;
  }
  const raw = fs.readFileSync(filePath, 'utf8').trim();
  if (!raw) {
    return null;
  }
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function isValidFragment(fragment, expectedVersion, expectedPlatform) {
  return (
    fragment &&
    fragment.version === expectedVersion &&
    fragment.platform === expectedPlatform &&
    typeof fragment.url === 'string' &&
    typeof fragment.signature === 'string'
  );
}

export function buildLatestManifest({ version, fragments, existingLatest, now = new Date() }) {
  if (fragments.length === 0) {
    throw new Error(`No uploaded platform manifest fragments found for ${version}`);
  }

  const reuseExisting =
    existingLatest && existingLatest.version === version;

  const latest = {
    version,
    notes:
      reuseExisting && typeof existingLatest.notes === 'string'
        ? existingLatest.notes
        : `Release ${version}`,
    pub_date:
      reuseExisting && typeof existingLatest.pub_date === 'string'
        ? existingLatest.pub_date
        : now.toISOString(),
    platforms: {}
  };

  for (const fragment of fragments) {
    latest.platforms[fragment.platform] = {
      url: fragment.url,
      signature: fragment.signature
    };
  }

  return latest;
}

export function writeLatestManifest({ outputPath, version, tempDir }) {
  const fragments = [];
  const missingPlatforms = [];

  for (const platform of PLATFORMS) {
    const fragment = readJsonIfExists(path.join(tempDir, `${platform}.json`));
    if (!isValidFragment(fragment, version, platform)) {
      missingPlatforms.push(platform);
      continue;
    }
    fragments.push(fragment);
  }

  const existingLatest = readJsonIfExists(path.join(tempDir, 'existing-latest.json'));
  const latest = buildLatestManifest({ version, fragments, existingLatest });

  fs.writeFileSync(outputPath, `${JSON.stringify(latest, null, 2)}\n`);
  return { latest, missingPlatforms, fragments };
}

function main() {
  const [outputPath, version, , tempDir] = process.argv.slice(2);

  if (!outputPath || !version || !tempDir) {
    console.error(
      'Usage: generate-latest-manifest.mjs <output> <version> <baseUrl> <tempDir>'
    );
    process.exit(1);
  }

  try {
    const { fragments, missingPlatforms } = writeLatestManifest({
      outputPath,
      version,
      tempDir
    });
    console.log(
      `latest.json platforms: ${fragments.map((f) => f.platform).join(', ')}`
    );
    if (missingPlatforms.length > 0) {
      console.log(`latest.json skipped platforms: ${missingPlatforms.join(', ')}`);
    }
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
