import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { assertProductVersion } from './product.mjs';
import {
  assertAndroidGame,
  assertAndroidLoader,
  assertInteropRecord,
  interopPackageKey
} from './manifest.mjs';

// The interop statement, its signature and the Pre-Adaptation record
// (docs/adr/0012-android-app.md "发布产物" and "预适配 interop 包"). The
// Android App loads an Interop Package only when the Android release key
// signed a statement binding the zip to the installed game's code and to a
// purpose: `release <product version>` for normal mode, `pre-adaptation` for
// Compatibility Mode. Shared fixtures: release/fixtures/android-interop/.

const SHA256 = /^[a-f0-9]{64}$/;
const STATEMENT_HEADER = 'bpp-interop-v1';
export const PRE_ADAPTATION_PURPOSE = 'pre-adaptation';

export const releasePurpose = (version) =>
  `release ${assertProductVersion(version)}`;

function assertPurpose(purpose) {
  if (purpose === PRE_ADAPTATION_PURPOSE) return purpose;
  const match = /^release (.+)$/.exec(purpose ?? '');
  if (match && releasePurpose(match[1]) === purpose) return purpose;
  throw new Error(
    `An interop statement's purpose is "release <version>" or "${PRE_ADAPTATION_PURPOSE}", not ${JSON.stringify(purpose)}`
  );
}

// The exact bytes the Android release key signs: five LF-terminated lines.
export function interopStatement({
  zipSha256,
  il2cppSha256,
  metadataSha256,
  purpose
}) {
  for (const [name, value] of Object.entries({
    zipSha256,
    il2cppSha256,
    metadataSha256
  }))
    if (!SHA256.test(value ?? ''))
      throw new Error(`An interop statement needs ${name} as sha256 hex`);
  return Buffer.from(
    `${[STATEMENT_HEADER, zipSha256, il2cppSha256, metadataSha256, assertPurpose(purpose)].join('\n')}\n`,
    'utf8'
  );
}

function rsaKey(key, description) {
  if (key.asymmetricKeyType !== 'rsa')
    throw new Error(
      `${description} is a ${key.asymmetricKeyType} key; interop statements are SHA256withRSA`
    );
  return key;
}

// SHA256withRSA (RSASSA-PKCS1-v1_5) over the statement, base64. The key is a
// PEM file, never key material on a command line or in a log.
export function signInteropStatement({ statement, keyFile }) {
  const key = rsaKey(
    crypto.createPrivateKey(fs.readFileSync(keyFile)),
    'The signing key'
  );
  return crypto.sign('sha256', statement, key).toString('base64');
}

const x509 = (certificate) =>
  certificate instanceof crypto.X509Certificate
    ? certificate
    : new crypto.X509Certificate(certificate);

// The SHA-256 of a certificate's DER bytes: what `apksigner verify
// --print-certs` reports and ANDROID_RELEASE_SIGNER_SHA256 pins.
export function certificateSha256(certificate) {
  return crypto
    .createHash('sha256')
    .update(x509(certificate).raw)
    .digest('hex');
}

// True when `signature` (base64) is the certificate's key's signature of the
// statement. `certificate` is PEM or DER bytes, or an X509Certificate.
export function verifyInteropStatement({ statement, signature, certificate }) {
  if (typeof signature !== 'string' || !signature) return false;
  const key = rsaKey(x509(certificate).publicKey, 'The certificate');
  return crypto.verify(
    'sha256',
    statement,
    key,
    Buffer.from(signature, 'base64')
  );
}

// The Android release key's private key and certificate as PEM files in
// `into`, read from the PKCS12 keystore androidSigningKey stages
// (release/android-release.mjs). The keystore password reaches openssl only
// through its file. The caller deletes `into` when done.
export function exportReleaseKey({ signing, into, openssl = 'openssl' }) {
  fs.mkdirSync(into, { recursive: true, mode: 0o700 });
  const keyFile = path.join(into, 'release-key.pem');
  const certificateFile = path.join(into, 'release-certificate.pem');
  const run = (args) => {
    try {
      execFileSync(
        openssl,
        [
          'pkcs12',
          '-in',
          signing.keystore,
          '-passin',
          `file:${signing.passwordFile}`,
          ...args
        ],
        { stdio: ['ignore', 'ignore', 'pipe'] }
      );
    } catch (error) {
      throw new Error(
        `openssl pkcs12 could not read the Android release keystore: ${String(error.stderr || error.message).trim()}`
      );
    }
  };
  // Only the owner can enter the directory or read the key; openssl keeps the
  // mode of the file it truncates.
  fs.chmodSync(into, 0o700);
  fs.writeFileSync(keyFile, '', { mode: 0o600 });
  fs.chmodSync(keyFile, 0o600);
  run(['-nocerts', '-nodes', '-out', keyFile]);
  run(['-nokeys', '-clcerts', '-out', certificateFile]);
  return { keyFile, certificateFile };
}

// Where `android-apk-watch.yml` records one Pre-Adaptation, below the release
// origin: one record per game code and Loader Release.
export function adaptationRecordKey({
  il2cppSha256,
  metadataSha256,
  loaderTag
}) {
  assertAndroidLoader({ tag: loaderTag, sha256: '0'.repeat(64) }, 'adaptation');
  if (!SHA256.test(il2cppSha256 ?? '') || !SHA256.test(metadataSha256 ?? ''))
    throw new Error('A Pre-Adaptation record is keyed by the Game Code Hash');
  return `android/adaptations/${il2cppSha256}_${metadataSha256}/${loaderTag}.json`;
}

export function validateAdaptationRecord(record) {
  if (
    record?.schemaVersion !== 1 ||
    record.kind !== 'android-adaptation' ||
    typeof record.il2cppInteropVersion !== 'string' ||
    !record.il2cppInteropVersion ||
    typeof record.publishedAt !== 'string' ||
    new Date(record.publishedAt).toISOString() !== record.publishedAt
  )
    throw new Error('Invalid Pre-Adaptation record');
  assertAndroidGame(record.game, 'Pre-Adaptation');
  assertInteropRecord(record.interop, 'Pre-Adaptation');
  assertAndroidLoader(record.loader, 'Pre-Adaptation');
  return record;
}

// The record of one Pre-Adaptation. `interop.signature` signs the
// `pre-adaptation` statement, so the record itself needs no signature.
export function buildAdaptationRecord({
  baseUrl,
  game,
  interop,
  loader,
  il2cppInteropVersion,
  publishedAt = new Date()
}) {
  const origin = baseUrl.replace(/\/$/, '');
  return validateAdaptationRecord({
    schemaVersion: 1,
    kind: 'android-adaptation',
    game: {
      gameVersion: game?.gameVersion,
      versionCode: game?.versionCode,
      il2cppSha256: game?.il2cppSha256,
      metadataSha256: game?.metadataSha256
    },
    interop: {
      url: `${origin}/${interopPackageKey(interop?.sha256)}`,
      size: interop?.size,
      sha256: interop?.sha256,
      snapshotSha256: interop?.snapshotSha256,
      signature: interop?.signature
    },
    loader: { tag: loader?.tag, sha256: loader?.sha256 },
    il2cppInteropVersion,
    publishedAt:
      publishedAt instanceof Date ? publishedAt.toISOString() : publishedAt
  });
}
