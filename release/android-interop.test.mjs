import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { afterEach, expect, test } from 'vitest';
import {
  certificateSha256,
  exportReleaseKey,
  interopStatement,
  releasePurpose,
  signInteropStatement,
  verifyInteropStatement
} from './android-interop.mjs';

// The interop statement against the shared fixture in
// release/fixtures/android-interop/, which the Android App's verifier reads
// too, and its signature's failure modes (another game, another purpose,
// another key). Keys are throwaway ones generated in a temporary directory;
// BPP_UPDATE_GOLDENS=1 rewrites the fixture.

const FIXTURES = path.join(import.meta.dirname, 'fixtures', 'android-interop');
const fixture = (name) => path.join(FIXTURES, name);
const updating = process.env.BPP_UPDATE_GOLDENS === '1';

// Synthetic digests: the fixture names no real game build.
const INPUTS = Object.freeze({
  zipSha256: `1${'0'.repeat(63)}`,
  il2cppSha256: `2${'0'.repeat(63)}`,
  metadataSha256: `3${'0'.repeat(63)}`
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

test('the interop statement is five LF-terminated lines, as in the shared fixture', () => {
  golden(
    'statement-release.txt',
    interopStatement({ ...INPUTS, purpose: releasePurpose('6.1.0') })
  );
  expect(
    interopStatement({ ...INPUTS, purpose: 'release 6.1.0' }).toString()
  ).toBe(
    `bpp-interop-v1\n${INPUTS.zipSha256}\n${INPUTS.il2cppSha256}\n${INPUTS.metadataSha256}\nrelease 6.1.0\n`
  );
});

test.each([
  ['an unknown purpose', { purpose: 'pre-adaptation' }, /purpose/],
  ['a release purpose without a version', { purpose: 'release ' }, /./],
  ['a release purpose with a non-version', { purpose: 'release v6' }, /./],
  ['an uppercase digest', { zipSha256: 'A'.repeat(64) }, /zipSha256/],
  ['a missing game digest', { metadataSha256: undefined }, /metadataSha256/]
])('the statement refuses %s', (_, change, error) => {
  expect(() =>
    interopStatement({
      ...INPUTS,
      purpose: releasePurpose('6.1.0'),
      ...change
    })
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
    purpose: releasePurpose('6.1.0')
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
