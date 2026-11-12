import crypto from 'node:crypto';
import {
  ANDROID_RELEASE_SIGNER_SHA256,
  androidManagedPayloadKey,
  buildAndroidFragment,
  fragmentKey
} from '../manifest.ts';
import type {
  AndroidAdaptationFacts,
  AndroidPlatformFragment
} from '../manifest.ts';
import { putImmutable } from '../r2-store.ts';
import type { ObjectStore } from '../r2-store.ts';
import type { ProductVersion } from '../product.ts';

const sha256 = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value: unknown): Buffer =>
  Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

// One product version's Android upload; every artifact's bytes and the
// signatures have defaults.
export interface UploadAndroidOptions {
  version: ProductVersion;
  gitCommit: string;
  baseUrl: string;
  app?: string;
  interop?: string;
  managedPayload?: string;
  signature?: string;
  signerSha256?: string;
}

export interface UploadedAndroid {
  fragment: AndroidPlatformFragment;
  appKey: string;
}

// An artifact record of a fragment: where it is served.
interface ServedRecord {
  url: string;
}

// What the Android release job leaves in the store for one product version
// (ADR 0012, ADR 0019): its Android App APK, each Game Adaptation's Interop
// Package (one content-addressed object every version of the same snapshot
// shares) and Managed Payload zip, and the fragment that names them. The
// publisher adaptation keeps the earlier fixture's game and bytes. The bytes are literal; the interop
// signature is a placeholder that no certificate verifies. The App bytes are
// the earlier fixture's APK bytes, so the site's screenshot goldens keep the
// size and SHA-256 they show.
export async function uploadAndroid(
  store: ObjectStore,
  {
    version,
    gitCommit,
    baseUrl,
    app = `android apk 1.0.12825 with ${version}\n`,
    interop = 'android interop package 1.0.12825\n',
    managedPayload = `android managed payload ${version}\n`,
    signature = `android-managed-signature-${version}`,
    signerSha256 = ANDROID_RELEASE_SIGNER_SHA256
  }: UploadAndroidOptions
): Promise<UploadedAndroid> {
  const appBytes = Buffer.from(app);
  const bytes = {
    publisher: {
      interop: Buffer.from(interop),
      managedPayload: Buffer.from(managedPayload)
    },
    play: {
      interop: Buffer.from('android interop package 1.0.6 (13168)\n'),
      managedPayload: Buffer.from(`android play managed payload ${version}\n`)
    }
  };
  const adaptation = (
    source: keyof typeof bytes,
    game: AndroidAdaptationFacts['game'],
    snapshotSha256: string
  ): AndroidAdaptationFacts => ({
    game,
    interop: {
      size: bytes[source].interop.length,
      sha256: sha256(bytes[source].interop),
      snapshotSha256,
      signature: Buffer.from(
        `interop statement signature ${source} ${version}`
      ).toString('base64')
    },
    managedPayload: {
      size: bytes[source].managedPayload.length,
      sha256: sha256(bytes[source].managedPayload),
      signature: source === 'publisher' ? signature : `${signature}-play`
    }
  });
  const fragment = buildAndroidFragment({
    version,
    gitCommit,
    baseUrl,
    app: { size: appBytes.length, sha256: sha256(appBytes), signerSha256 },
    adaptations: {
      publisher: adaptation(
        'publisher',
        {
          gameVersion: '1.0.12825-production-android-d9601ded',
          versionCode: 12825,
          il2cppSha256: '7'.repeat(64),
          metadataSha256: '9'.repeat(64)
        },
        `3f9a1c2d${'9'.repeat(56)}`
      ),
      play: adaptation(
        'play',
        {
          gameVersion: '1.0.6',
          versionCode: 13168,
          il2cppSha256: '5'.repeat(64),
          metadataSha256: '4'.repeat(64)
        },
        `2e8b0d1c${'4'.repeat(56)}`
      )
    },
    loader: { tag: 'v0.7.4-android.4', sha256: '8'.repeat(64) },
    selfUpdatable: true,
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: '6'.repeat(64)
  });
  const key = (record: ServedRecord): string =>
    record.url.slice(`${baseUrl}/`.length);
  await putImmutable(store, key(fragment.app), appBytes);
  for (const source of ['publisher', 'play'] as const) {
    await putImmutable(
      store,
      key(fragment.adaptations![source].interop),
      bytes[source].interop
    );
    await putImmutable(
      store,
      androidManagedPayloadKey(version, source),
      bytes[source].managedPayload
    );
  }
  await putImmutable(
    store,
    fragmentKey(version, fragment.platform),
    jsonBytes(fragment),
    'application/json'
  );
  return { fragment, appKey: key(fragment.app) };
}
