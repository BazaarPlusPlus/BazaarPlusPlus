import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { afterEach, expect, test } from 'vitest';
import {
  PRE_ADAPTATION_PURPOSE,
  adaptationRecordKey,
  buildAdaptationRecord,
  certificateSha256,
  exportReleaseKey,
  interopStatement,
  releasePurpose,
  signInteropStatement,
  validateAdaptationRecord,
  verifyInteropStatement
} from './android-interop.mjs';
import { RELEASE_BASE_URL } from './product.mjs';

// The interop statement and the Pre-Adaptation record against the shared
// fixtures in release/fixtures/android-interop/, which the Android App's
// verifier reads too: the statement bytes, a record whose signature verifies
// under the committed test certificate, and failure modes (another game,
// another purpose, another key, a malformed record). Keys are throwaway ones
// generated in a temporary directory; BPP_UPDATE_GOLDENS=1 rewrites the
// fixtures with a new one.

const FIXTURES = path.join(import.meta.dirname, 'fixtures', 'android-interop');
const fixture = (name) => path.join(FIXTURES, name);
const updating = process.env.BPP_UPDATE_GOLDENS === '1';

// Synthetic digests: the fixture names no real game build.
const INPUTS = Object.freeze({
  zipSha256: `1${'0'.repeat(63)}`,
  il2cppSha256: `2${'0'.repeat(63)}`,
  metadataSha256: `3${'0'.repeat(63)}`
});
const GAME = Object.freeze({
  gameVersion: '1.0.12900-production-android-0badc0de',
  versionCode: 12900,
  il2cppSha256: INPUTS.il2cppSha256,
  metadataSha256: INPUTS.metadataSha256
});
const LOADER = Object.freeze({
  tag: 'v0.7.4-android.4',
  sha256: `4${'0'.repeat(63)}`
});

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});
const tempDir = () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-android-interop-'));
  roots.push(root);
  return root;
};

// A throwaway RSA key and its self-signed certificate, as the release
// keystore holds one, plus that keystore as PKCS12.
function throwawayKey(root = tempDir()) {
  const keyFile = path.join(root, 'key.pem');
  const certificateFile = path.join(root, 'certificate.pem');
  execFileSync(
    'openssl',
    [
      'req',
      '-x509',
      '-newkey',
      'rsa:2048',
      '-nodes',
      '-keyout',
      keyFile,
      '-out',
      certificateFile,
      '-subj',
      '/CN=BazaarPlusPlus interop test key',
      '-days',
      '36500'
    ],
    { stdio: 'ignore' }
  );
  return { root, keyFile, certificateFile };
}

function golden(name, produced) {
  if (updating) fs.writeFileSync(fixture(name), produced);
  expect(Buffer.from(produced).toString('utf8'), name).toBe(
    fs.readFileSync(fixture(name), 'utf8')
  );
}

test('the interop statement is five LF-terminated lines, as in the shared fixtures', () => {
  golden(
    'statement-release.txt',
    interopStatement({ ...INPUTS, purpose: releasePurpose('6.1.0') })
  );
  golden(
    'statement-pre-adaptation.txt',
    interopStatement({ ...INPUTS, purpose: PRE_ADAPTATION_PURPOSE })
  );
  expect(
    interopStatement({ ...INPUTS, purpose: 'release 6.1.0' }).toString()
  ).toBe(
    `bpp-interop-v1\n${INPUTS.zipSha256}\n${INPUTS.il2cppSha256}\n${INPUTS.metadataSha256}\nrelease 6.1.0\n`
  );
});

test.each([
  ['an unknown purpose', { purpose: 'compatibility' }, /purpose/],
  ['a release purpose without a version', { purpose: 'release ' }, /./],
  ['a release purpose with a non-version', { purpose: 'release v6' }, /./],
  ['an uppercase digest', { zipSha256: 'A'.repeat(64) }, /zipSha256/],
  ['a missing game digest', { metadataSha256: undefined }, /metadataSha256/]
])('the statement refuses %s', (_, change, error) => {
  expect(() =>
    interopStatement({ ...INPUTS, purpose: PRE_ADAPTATION_PURPOSE, ...change })
  ).toThrow(error);
});

test('a signed statement verifies under its certificate and binds the game, purpose and key', () => {
  const key = throwawayKey();
  const statement = interopStatement({
    ...INPUTS,
    purpose: releasePurpose('6.1.0')
  });
  const signature = signInteropStatement({ statement, keyFile: key.keyFile });
  const certificate = fs.readFileSync(key.certificateFile);
  expect(verifyInteropStatement({ statement, signature, certificate })).toBe(
    true
  );
  for (const other of [
    { ...INPUTS, purpose: PRE_ADAPTATION_PURPOSE },
    { ...INPUTS, il2cppSha256: 'f'.repeat(64), purpose: 'release 6.1.0' },
    { ...INPUTS, purpose: 'release 6.1.1' }
  ])
    expect(
      verifyInteropStatement({
        statement: interopStatement(other),
        signature,
        certificate
      })
    ).toBe(false);
  const stranger = fs.readFileSync(throwawayKey().certificateFile);
  expect(
    verifyInteropStatement({ statement, signature, certificate: stranger })
  ).toBe(false);
  expect(
    verifyInteropStatement({ statement, signature: '', certificate })
  ).toBe(false);
});

test('signing refuses a key that is not RSA', () => {
  const root = tempDir();
  const keyFile = path.join(root, 'ec.pem');
  const { privateKey } = crypto.generateKeyPairSync('ec', {
    namedCurve: 'P-256'
  });
  fs.writeFileSync(
    keyFile,
    privateKey.export({ type: 'pkcs8', format: 'pem' })
  );
  expect(() =>
    signInteropStatement({ statement: Buffer.from('x'), keyFile })
  ).toThrow(/SHA256withRSA/);
});

test('the release key and certificate come out of the PKCS12 keystore through the password file', () => {
  const key = throwawayKey();
  const passwordFile = path.join(key.root, 'android-keystore-password');
  fs.writeFileSync(passwordFile, 'correct horse battery staple');
  const keystore = path.join(key.root, 'android-release.p12');
  execFileSync(
    'openssl',
    [
      'pkcs12',
      '-export',
      '-inkey',
      key.keyFile,
      '-in',
      key.certificateFile,
      '-name',
      'bpp-android-host',
      '-passout',
      `file:${passwordFile}`,
      '-out',
      keystore
    ],
    { stdio: 'ignore' }
  );
  const into = path.join(key.root, 'exported');
  const exported = exportReleaseKey({
    signing: { keystore, passwordFile, alias: 'bpp-android-host' },
    into
  });
  expect(fs.statSync(exported.keyFile).mode & 0o077).toBe(0);
  expect(certificateSha256(fs.readFileSync(exported.certificateFile))).toBe(
    certificateSha256(fs.readFileSync(key.certificateFile))
  );
  const statement = interopStatement({
    ...INPUTS,
    purpose: PRE_ADAPTATION_PURPOSE
  });
  expect(
    verifyInteropStatement({
      statement,
      signature: signInteropStatement({
        statement,
        keyFile: exported.keyFile
      }),
      certificate: fs.readFileSync(key.certificateFile)
    })
  ).toBe(true);

  fs.writeFileSync(passwordFile, 'wrong');
  expect(() =>
    exportReleaseKey({
      signing: { keystore, passwordFile },
      into: path.join(key.root, 'again')
    })
  ).toThrow(/openssl pkcs12 could not read the Android release keystore/);
});

test('the shared Pre-Adaptation record verifies under the test certificate', () => {
  const statement = interopStatement({
    ...INPUTS,
    purpose: PRE_ADAPTATION_PURPOSE
  });
  if (updating) {
    const key = throwawayKey();
    fs.copyFileSync(key.certificateFile, fixture('test-certificate.pem'));
    const record = buildAdaptationRecord({
      baseUrl: RELEASE_BASE_URL,
      game: GAME,
      interop: {
        size: 15_000_000,
        sha256: INPUTS.zipSha256,
        snapshotSha256: `5${'0'.repeat(63)}`,
        signature: signInteropStatement({ statement, keyFile: key.keyFile })
      },
      loader: LOADER,
      il2cppInteropVersion: '1.5.3.0',
      publishedAt: new Date('2026-11-01T00:00:00.000Z')
    });
    fs.writeFileSync(
      fixture('adaptation-record.json'),
      `${JSON.stringify(record, null, 2)}\n`
    );
  }
  const record = JSON.parse(
    fs.readFileSync(fixture('adaptation-record.json'), 'utf8')
  );
  expect(validateAdaptationRecord(record)).toBe(record);
  expect(
    buildAdaptationRecord({
      baseUrl: RELEASE_BASE_URL,
      game: GAME,
      interop: record.interop,
      loader: LOADER,
      il2cppInteropVersion: '1.5.3.0',
      publishedAt: new Date('2026-11-01T00:00:00.000Z')
    })
  ).toEqual(record);
  expect(record.interop.url).toBe(
    `${RELEASE_BASE_URL}/android/interop/${INPUTS.zipSha256}.zip`
  );
  const certificate = fs.readFileSync(fixture('test-certificate.pem'));
  expect(
    verifyInteropStatement({
      statement,
      signature: record.interop.signature,
      certificate
    })
  ).toBe(true);
  // The record's signature is for Compatibility Mode only.
  expect(
    verifyInteropStatement({
      statement: interopStatement({
        ...INPUTS,
        purpose: releasePurpose('6.1.0')
      }),
      signature: record.interop.signature,
      certificate
    })
  ).toBe(false);
  expect(
    adaptationRecordKey({
      il2cppSha256: record.game.il2cppSha256,
      metadataSha256: record.game.metadataSha256,
      loaderTag: record.loader.tag
    })
  ).toBe(
    `android/adaptations/${INPUTS.il2cppSha256}_${INPUTS.metadataSha256}/v0.7.4-android.4.json`
  );
});

const brokenRecord = (mutate) => {
  const record = JSON.parse(
    fs.readFileSync(fixture('adaptation-record.json'), 'utf8')
  );
  mutate(record);
  return record;
};

test.each([
  ['another kind', (r) => (r.kind = 'android-app'), /Pre-Adaptation record/],
  ['a later schema', (r) => (r.schemaVersion = 2), /Pre-Adaptation record/],
  ['a local time', (r) => (r.publishedAt = '2026-11-01'), /record/],
  ['no Game Code Hash', (r) => delete r.game.metadataSha256, /game/],
  [
    'an interop outside its content address',
    (r) =>
      (r.interop.url = r.interop.url.replace(
        '/android/interop/',
        '/6.1.0/android-aarch64/interop/'
      )),
    /Unexpected Interop Package URL/
  ],
  [
    'an unsigned interop',
    (r) => (r.interop.signature = ''),
    /Invalid Pre-Adaptation Interop Package/
  ],
  ['a loader tag with a path', (r) => (r.loader.tag = '../x'), /loader/]
])('a Pre-Adaptation record with %s is refused', (_, mutate, error) => {
  expect(() => validateAdaptationRecord(brokenRecord(mutate))).toThrow(error);
});

test('a Pre-Adaptation key refuses a loader tag that is not a plain token', () => {
  expect(() =>
    adaptationRecordKey({ ...INPUTS, loaderTag: 'v1/../../latest' })
  ).toThrow(/loader/);
});
