import { toHex } from '../bundle/hex';

export const DEVICE_TOKEN_PREFIX = 'bppd_';
export const VERIFICATION_CODE_TTL_MS = 10 * 60 * 1000;
export const VERIFICATION_CODE_MAX_ATTEMPTS = 5;
export const HOURLY_EMAIL_SEND_LIMIT = 5;
export const HOURLY_WINDOW_MS = 60 * 60 * 1000;
export const WEB_SESSION_TTL_MS = 30 * 24 * 60 * 60 * 1000;

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
  purpose: 'bind' | 'web_login',
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
