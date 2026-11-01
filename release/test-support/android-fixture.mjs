import crypto from 'node:crypto';
import {
  ANDROID_RELEASE_SIGNER_SHA256,
  buildAndroidFragment,
  fragmentKey,
  managedPayloadKey
} from '../manifest.mjs';
import { putImmutable } from '../r2-store.mjs';

const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

// What the Android release job leaves in the store for one product version
// (ADR 0012): its Android App APK, the Interop Package (one content-addressed
// object every version of the same snapshot shares), its Managed Payload zip,
// and the fragment that names them. The bytes are literal; the interop
// signature is a placeholder that no certificate verifies. The App bytes are
// the earlier fixture's APK bytes, so the site's screenshot goldens keep the
// size and SHA-256 they show.
export async function uploadAndroid(
  store,
  {
    version,
    gitCommit,
    baseUrl,
    app = `android apk 1.0.12825 with ${version}\n`,
    interop = 'android interop package 1.0.12825\n',
    managedPayload = `android managed payload ${version}\n`,
    signature = `android-managed-signature-${version}`,
    signerSha256 = ANDROID_RELEASE_SIGNER_SHA256
  }
) {
  const appBytes = Buffer.from(app);
  const interopBytes = Buffer.from(interop);
  const zipBytes = Buffer.from(managedPayload);
  const fragment = buildAndroidFragment({
    version,
    gitCommit,
    baseUrl,
    app: { size: appBytes.length, sha256: sha256(appBytes), signerSha256 },
    game: {
      gameVersion: '1.0.12825-production-android-d9601ded',
      versionCode: 12825,
      il2cppSha256: '7'.repeat(64),
      metadataSha256: '9'.repeat(64)
    },
    interop: {
      size: interopBytes.length,
      sha256: sha256(interopBytes),
      snapshotSha256: `3f9a1c2d${'9'.repeat(56)}`,
      signature: Buffer.from(`interop statement signature ${version}`).toString(
        'base64'
      )
    },
    managedPayload: {
      size: zipBytes.length,
      sha256: sha256(zipBytes),
      signature
    },
    loader: { tag: 'v0.7.4-android.4', sha256: '8'.repeat(64) },
    selfUpdatable: true,
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: '6'.repeat(64)
  });
  const key = (record) => record.url.slice(`${baseUrl}/`.length);
  await putImmutable(store, key(fragment.app), appBytes);
  await putImmutable(store, key(fragment.interop), interopBytes);
  await putImmutable(
    store,
    managedPayloadKey(version, fragment.platform),
    zipBytes
  );
  await putImmutable(
    store,
    fragmentKey(version, fragment.platform),
    jsonBytes(fragment),
    'application/json'
  );
  return { fragment, appKey: key(fragment.app) };
}

// A promoted ADR 0010 Android Platform Release Manifest (the injected APK):
// what the first Android App promote finds on R2. Delete it together with
// isRetiredAndroidManifest in manifest.mjs.
export const retiredAndroidManifest = (version = '3.1.1') => ({
  version,
  gitCommit: 'c'.repeat(40),
  notes: `Release ${version}`,
  pub_date: '2026-09-21T00:00:00.000Z',
  downloads: {
    'android-aarch64': {
      apk: {
        url: `https://cdn.bazaarplusplus.com/installer/android/apk/BazaarPlusPlus_${version}_1.0.12825.apk`,
        size: 33,
        sha256:
          '984288b806422b6f3f5882e4f69083148e63da7651384cd0691521aa493e7fb0',
        versionCode: 12825,
        signerSha256: '5'.repeat(64),
        builtForProductVersion: version
      },
      managedPayload: {
        url: `https://cdn.bazaarplusplus.com/installer/${version}/android-aarch64/managed/BazaarPlusPlus_${version}_android-aarch64-managed.zip`,
        size: 30,
        sha256:
          '03e651b2e1f76ad4b9b4b63d729ce34870bc50ed1d20f5e70a620686631c5abc',
        signature: `android-managed-signature-${version}`
      },
      selfUpdatable: true,
      historyDatabaseUserVersion: 1,
      nonManagedPayloadDigest: '6'.repeat(64),
      game: {
        gameVersion: '1.0.12825-production-android-d9601ded',
        versionCode: 12825,
        officialApkSha256: '7'.repeat(64)
      },
      loader: { tag: 'v0.7.4-android.4', sha256: '8'.repeat(64) },
      interopSha256: `3f9a1c2d${'9'.repeat(56)}`
    }
  }
});
