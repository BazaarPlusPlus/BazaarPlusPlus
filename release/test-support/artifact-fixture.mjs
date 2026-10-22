import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { tauriPlatform } from '../release-platforms.mjs';
import { PAYLOAD_BUILD_RECORD_SCHEMA_VERSION } from '../artifact-manifest.mjs';

// Where a native build leaves one platform's installer, updater and updater
// signature under the installer project, laid out by RELEASE_PLATFORMS.
// Windows ships its NSIS installer as the updater; macOS ships an app archive.
export function artifactPaths(rootDir, platform, version) {
  const definition = tauriPlatform(platform);
  const bundleRoot = path.join(rootDir, definition.bundleRoot);
  const installerDir = path.join(rootDir, definition.installerDir);
  const installer = path.join(
    installerDir,
    platform === 'windows'
      ? `BazaarPlusPlus_${version}_x64-setup.exe`
      : `BazaarPlusPlus_${version}_aarch64.dmg`
  );
  const updater =
    platform === 'windows'
      ? installer
      : path.join(bundleRoot, 'macos', 'BazaarPlusPlus.app.tar.gz');
  return {
    bundleRoot,
    bundleDir: installerDir,
    installer,
    updater,
    signature: `${updater}.sig`,
    managedPayload: path.join(
      rootDir,
      'src-tauri/target/release-artifacts/managed',
      definition.key,
      `BazaarPlusPlus_${version}_${definition.key}-managed.zip`
    ),
    payloadBuild: path.join(
      rootDir,
      'src-tauri/resources/BepInExSource',
      platform,
      'payload-build.json'
    )
  };
}

// The self-update facts every shared fixture records for a desktop build.
export const MANAGED_PAYLOAD_FACTS = Object.freeze({
  historyDatabaseUserVersion: 1,
  nonManagedPayloadDigest: 'e'.repeat(64)
});

// Replaces the platform's bundle with literal bytes, plus the sealed Payload
// receipt and the signed Managed Payload createArtifactManifest records.
// `signature` is written verbatim. The result's `managed` is the
// createArtifactManifest `managedPayload` argument.
export function writeArtifacts(
  rootDir,
  platform,
  {
    version,
    installer = 'installer bytes',
    updater = 'updater bytes',
    signature = 'public-signature\n',
    managedPayload = `${platform} managed payload`,
    managedSignature = `${platform}-managed-signature`
  }
) {
  const paths = artifactPaths(rootDir, platform, version);
  fs.rmSync(paths.bundleRoot, { recursive: true, force: true });
  fs.rmSync(path.dirname(paths.managedPayload), {
    recursive: true,
    force: true
  });
  for (const file of [
    paths.installer,
    paths.signature,
    paths.payloadBuild,
    paths.managedPayload
  ])
    fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(paths.managedPayload, managedPayload);
  fs.writeFileSync(paths.installer, installer);
  if (paths.updater !== paths.installer) {
    fs.mkdirSync(path.dirname(paths.updater), { recursive: true });
    fs.writeFileSync(paths.updater, updater);
  }
  fs.writeFileSync(paths.signature, signature);
  fs.writeFileSync(
    paths.payloadBuild,
    JSON.stringify({
      schemaVersion: PAYLOAD_BUILD_RECORD_SCHEMA_VERSION,
      productVersion: version,
      platform
    })
  );
  return {
    rootDir,
    ...paths,
    managed: {
      path: paths.managedPayload,
      signature: managedSignature,
      ...MANAGED_PAYLOAD_FACTS
    }
  };
}

export function artifactFixture(platform, options = {}) {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-artifacts-'));
  return writeArtifacts(rootDir, platform, { version: '9.9.9', ...options });
}
