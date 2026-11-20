import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { assertProductVersion } from './product.ts';
import type { ProductVersion } from './product.ts';

// The Android App verifies that the signed statement binds the package to its game code and purpose (ADR 0012).

const SHA256 = /^[a-f0-9]{64}$/;
const STATEMENT_HEADER = 'bpp-interop-v1';

export interface InteropStatementInputs {
  zipSha256: string;
  il2cppSha256: string;
  metadataSha256: string;
  purpose: string;
}

export interface SignInteropStatementOptions {
  statement: Buffer;
  keyFile: string;
}

export type CertificateInput = crypto.X509Certificate | crypto.BinaryLike;

export interface VerifyInteropStatementOptions {
  statement: Buffer;
  signature: string;
  certificate: CertificateInput;
}

export interface ReleaseKeystore {
  keystore: string;
  passwordFile: string;
}

export interface ReleaseKeyFiles {
  keyFile: string;
  certificateFile: string;
}

export interface ExportReleaseKeyOptions {
  signing: ReleaseKeystore;
  into: string;
  openssl?: string;
}

export type ReleaseKeyExporter = (options: {
  signing: ReleaseKeystore;
  into: string;
}) => ReleaseKeyFiles;

export interface SignWithReleaseKeyOptions {
  statement: Buffer;
  signing: ReleaseKeystore;
  signerSha256: string;
  releaseKey?: ReleaseKeyExporter;
}

type OpensslError = Error & { stderr?: Buffer };

export const releasePurpose = (version: ProductVersion): string =>
  `release ${assertProductVersion(version)}`;

function assertPurpose(purpose: string | undefined): string {
  const match = /^release (.+)$/.exec(purpose ?? '');
  if (match && releasePurpose(match[1]) === purpose) return purpose;
  throw new Error(
    `An interop statement's purpose is "release <version>", not ${JSON.stringify(purpose)}`
  );
}

// The exact bytes the Android release key signs: five LF-terminated lines.
export function interopStatement({
  zipSha256,
  il2cppSha256,
  metadataSha256,
  purpose
}: InteropStatementInputs): Buffer {
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

function rsaKey(key: crypto.KeyObject, description: string): crypto.KeyObject {
  if (key.asymmetricKeyType !== 'rsa')
    throw new Error(
      `${description} is a ${key.asymmetricKeyType} key; interop statements are SHA256withRSA`
    );
  return key;
}

// Pass a file path so key material stays out of arguments and logs.
export function signInteropStatement({
  statement,
  keyFile
}: SignInteropStatementOptions): string {
  const key = rsaKey(
    crypto.createPrivateKey(fs.readFileSync(keyFile)),
    'The signing key'
  );
  return crypto.sign('sha256', statement, key).toString('base64');
}

const x509 = (certificate: CertificateInput): crypto.X509Certificate =>
  certificate instanceof crypto.X509Certificate
    ? certificate
    : new crypto.X509Certificate(certificate);

// Hash the DER bytes to match the digest reported by apksigner and pinned by ANDROID_RELEASE_SIGNER_SHA256.
export function certificateSha256(certificate: CertificateInput): string {
  return crypto
    .createHash('sha256')
    .update(x509(certificate).raw)
    .digest('hex');
}

export function verifyInteropStatement({
  statement,
  signature,
  certificate
}: VerifyInteropStatementOptions): boolean {
  if (typeof signature !== 'string' || !signature) return false;
  const key = rsaKey(x509(certificate).publicKey, 'The certificate');
  return crypto.verify(
    'sha256',
    statement,
    key,
    Buffer.from(signature, 'base64')
  );
}

// Pass the keystore password by file so it stays out of the command line.
export function exportReleaseKey({
  signing,
  into,
  openssl = 'openssl'
}: ExportReleaseKeyOptions): ReleaseKeyFiles {
  fs.mkdirSync(into, { recursive: true, mode: 0o700 });
  const keyFile = path.join(into, 'release-key.pem');
  const certificateFile = path.join(into, 'release-certificate.pem');
  const run = (args: string[]): void => {
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
      const failure = error as OpensslError;
      throw new Error(
        `openssl pkcs12 could not read the Android release keystore: ${String(failure.stderr || failure.message).trim()}`
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

// Export the key into a fresh temporary directory and remove it before returning.
export function signWithReleaseKey({
  statement,
  signing,
  signerSha256,
  releaseKey = exportReleaseKey
}: SignWithReleaseKeyOptions): string {
  const keyDirectory = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-android-key-')
  );
  try {
    const { keyFile, certificateFile } = releaseKey({
      signing,
      into: path.join(keyDirectory, 'key')
    });
    const certificate = fs.readFileSync(certificateFile);
    if (certificateSha256(certificate) !== signerSha256)
      throw new Error(
        `The keystore's certificate ${certificateSha256(certificate)} is not the ${signerSha256} that signed the Android App`
      );
    const signature = signInteropStatement({ statement, keyFile });
    if (!verifyInteropStatement({ statement, signature, certificate }))
      throw new Error('The interop statement signature does not verify');
    return signature;
  } finally {
    fs.rmSync(keyDirectory, { recursive: true, force: true });
  }
}
