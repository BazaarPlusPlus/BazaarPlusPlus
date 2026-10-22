import crypto from 'node:crypto';
import {
  buildAndroidFragment,
  fragmentKey,
  managedPayloadKey
} from '../manifest.mjs';
import { putImmutable } from '../r2-store.mjs';

const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

// What the Android release job (#358) leaves in the store for one product
// version: the Android Release APK under its own identity, the Managed Payload
// zip of the version, and the fragment that names both. The bytes are
// literal; `apkVersion` is the product version whose Mods the APK carries.
export async function uploadAndroid(
  store,
  {
    version,
    gitCommit,
    baseUrl,
    apkVersion = version,
    apk = `android apk 1.0.12825 with ${apkVersion}\n`,
    managedPayload = `android managed payload ${version}\n`,
    signature = `android-managed-signature-${version}`
  }
) {
  const apkBytes = Buffer.from(apk);
  const zipBytes = Buffer.from(managedPayload);
  const fragment = buildAndroidFragment({
    version,
    gitCommit,
    baseUrl,
    apk: {
      size: apkBytes.length,
      sha256: sha256(apkBytes),
      versionCode: 12825,
      signerSha256: '5'.repeat(64),
      builtForProductVersion: apkVersion
    },
    managedPayload: {
      size: zipBytes.length,
      sha256: sha256(zipBytes),
      signature
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
  });
  const apkKey = fragment.apk.url.slice(`${baseUrl}/`.length);
  await putImmutable(store, apkKey, apkBytes);
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
  return { fragment, apkKey };
}
