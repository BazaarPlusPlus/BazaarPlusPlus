import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function buildPlatformFragment({
  platformKey,
  baseUrl,
  version,
  updaterFile,
  updaterSignature
}) {
  return {
    version,
    platform: platformKey,
    url: `${baseUrl}/${version}/${platformKey}/updater/${path.basename(updaterFile)}`,
    signature: updaterSignature.trim()
  };
}

export function writePlatformManifest({
  outputPath,
  platformKey,
  baseUrl,
  version,
  updaterFile,
  updaterSig
}) {
  const updaterSignature = fs.readFileSync(updaterSig, 'utf8');
  const fragment = buildPlatformFragment({
    platformKey,
    baseUrl,
    version,
    updaterFile,
    updaterSignature
  });
  fs.writeFileSync(outputPath, `${JSON.stringify(fragment, null, 2)}\n`);
  return fragment;
}

function main() {
  const [outputPath, platformKey, baseUrl, version, updaterFile, updaterSig] =
    process.argv.slice(2);

  if (!outputPath || !platformKey || !baseUrl || !version || !updaterFile || !updaterSig) {
    console.error(
      'Usage: generate-platform-manifest.mjs <output> <platform> <baseUrl> <version> <updaterFile> <updaterSig>'
    );
    process.exit(1);
  }

  writePlatformManifest({
    outputPath,
    platformKey,
    baseUrl,
    version,
    updaterFile,
    updaterSig
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
