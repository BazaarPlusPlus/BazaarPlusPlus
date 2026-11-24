import { toHex } from '../bundle/hex';

export const DEVICE_TOKEN_PREFIX = 'bppd_';
export const VERIFICATION_CODE_TTL_MS = 10 * 60 * 1000;
export const VERIFICATION_CODE_MAX_ATTEMPTS = 5;
export const HOURLY_EMAIL_SEND_LIMIT = 5;
export const HOURLY_WINDOW_MS = 60 * 60 * 1000;
export const WEB_SESSION_TTL_MS = 30 * 24 * 60 * 60 * 1000;
export const BIND_CODE_TTL_MS = 10 * 60 * 1000;
export const PASSWORD_MIN_LENGTH = 8;
export const PASSWORD_MAX_LENGTH = 128;

const PBKDF2_ITERATIONS = 100_000;
const PBKDF2_SALT_BYTES = 16;
const PBKDF2_HASH_BITS = 256;
const PASSWORD_HASH_PREFIX = 'pbkdf2-sha256';
// A well-formed hash nobody can match: login for an unknown email derives
// against it, so both paths cost one PBKDF2 and fail the same way.
const DUMMY_PASSWORD_HASH =
  'pbkdf2-sha256$100000$ZHVtbXktc2FsdC0xNmJ5dA$3FT1ETVeEAXT9pd4zFnwNSyfuW04qZAklO1bFbbo3l8';

// No 0, 1, I or O, so a retyped code stays unambiguous; 32 symbols.
const BIND_CODE_ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
const BIND_CODE_LENGTH = 8;
const BIND_CODE_PATTERN = /^[23456789A-HJ-NP-Z]{8}$/;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const SIX_DIGIT_CODE = /^[0-9]{6}$/;

export function normalizeEmail(raw: string): string | null {
  const normalized = raw.trim().toLowerCase();
  if (
    normalized.length === 0 ||
    normalized.length > 254 ||
    /[\r\n]/.test(normalized) ||
    !EMAIL_PATTERN.test(normalized)
  ) {
    return null;
  }
  return normalized;
}

export function maskEmail(normalizedEmail: string): string {
  const at = normalizedEmail.indexOf('@');
  if (at <= 0) return '***';
  const local = normalizedEmail.slice(0, at);
  const domain = normalizedEmail.slice(at);
  if (local.length === 1) return `*${domain}`;
  return `${local[0]}***${domain}`;
}

export function validSixDigitCode(value: string): boolean {
  return SIX_DIGIT_CODE.test(value);
}

export function generateSixDigitCode(): string {
  // Rejection sampling avoids modulo bias across 0..999_999.
  const limit = 4_294_000_000;
  const word = new Uint32Array(1);
  while (true) {
    crypto.getRandomValues(word);
    if (word[0] < limit) {
      return String(word[0] % 1_000_000).padStart(6, '0');
    }
  }
}

export function toBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '');
}

export function fromBase64Url(text: string): Uint8Array | null {
  if (!/^[A-Za-z0-9_-]+$/.test(text)) return null;
  const padded = text
    .replace(/-/g, '+')
    .replace(/_/g, '/')
    .padEnd(Math.ceil(text.length / 4) * 4, '=');
  try {
    const binary = atob(padded);
    return Uint8Array.from(binary, (character) => character.charCodeAt(0));
  } catch {
    return null;
  }
}

export function generateDeviceToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return `${DEVICE_TOKEN_PREFIX}${toBase64Url(bytes)}`;
}

export function generateSessionToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return toBase64Url(bytes);
}

export async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(value)
  );
  return toHex(digest);
}

export async function hmacVerificationCode(
  secret: string,
  purpose: 'register' | 'reset',
  id: string,
  email: string,
  code: string
): Promise<string> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    'raw',
    encoder.encode(secret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const signature = await crypto.subtle.sign(
    'HMAC',
    key,
    encoder.encode(`${purpose}:${id}:${email}:${code}`)
  );
  return toHex(signature);
}

export function constantTimeHexEquals(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let index = 0; index < a.length; index += 1) {
    diff |= a.charCodeAt(index) ^ b.charCodeAt(index);
  }
  return diff === 0;
}

export function validPasswordLength(password: string): boolean {
  return (
    password.length >= PASSWORD_MIN_LENGTH &&
    password.length <= PASSWORD_MAX_LENGTH
  );
}

async function pbkdf2(password: string, salt: Uint8Array): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(password),
    'PBKDF2',
    false,
    ['deriveBits']
  );
  const bits = await crypto.subtle.deriveBits(
    { name: 'PBKDF2', hash: 'SHA-256', salt, iterations: PBKDF2_ITERATIONS },
    key,
    PBKDF2_HASH_BITS
  );
  return new Uint8Array(bits);
}

export async function hashPassword(password: string): Promise<string> {
  const salt = new Uint8Array(PBKDF2_SALT_BYTES);
  crypto.getRandomValues(salt);
  const hash = await pbkdf2(password, salt);
  return `${PASSWORD_HASH_PREFIX}$${PBKDF2_ITERATIONS}$${toBase64Url(salt)}$${toBase64Url(hash)}`;
}

// A null stored hash verifies against the dummy hash and returns false, so an
// unknown email costs the same PBKDF2 as a wrong password.
export async function verifyPassword(
  password: string,
  stored: string | null
): Promise<boolean> {
  const parts = (stored ?? DUMMY_PASSWORD_HASH).split('$');
  if (
    parts.length !== 4 ||
    parts[0] !== PASSWORD_HASH_PREFIX ||
    parts[1] !== String(PBKDF2_ITERATIONS)
  ) {
    return false;
  }
  const salt = fromBase64Url(parts[2]);
  const expected = fromBase64Url(parts[3]);
  if (salt === null || expected === null) return false;
  const actual = await pbkdf2(password, salt);
  return (
    stored !== null && constantTimeHexEquals(toHex(actual), toHex(expected))
  );
}

export function generateBindCode(): string {
  const bytes = new Uint8Array(BIND_CODE_LENGTH);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (byte) => BIND_CODE_ALPHABET[byte & 31]).join('');
}

// Strips spaces and dashes and uppercases, so `abcd-efgh` matches `ABCDEFGH`.
export function normalizeBindCode(raw: string): string | null {
  const normalized = raw.replace(/[\s-]/g, '').toUpperCase();
  return BIND_CODE_PATTERN.test(normalized) ? normalized : null;
}

export async function hmacBindCode(
  secret: string,
  normalizedCode: string
): Promise<string> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    'raw',
    encoder.encode(secret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const signature = await crypto.subtle.sign(
    'HMAC',
    key,
    encoder.encode(normalizedCode)
  );
  return toHex(signature);
}
