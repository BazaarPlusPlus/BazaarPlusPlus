import { MpExt, MpMap, type MpValue } from '../msgpack';
import {
  EnumValue,
  RawValue,
  TMap,
  TObject,
  type Translated
} from './translate';

/** JSON that can hold a 64-bit integer exactly: a bigint prints as its digits. */
type Json =
  | null
  | boolean
  | number
  | bigint
  | string
  | Json[]
  | { [key: string]: Json };

export function base64(bytes: Uint8Array): string {
  let text = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    text += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(text);
}

function number(value: number): Json {
  return Number.isFinite(value) ? value : String(value);
}

/** A raw MessagePack value as JSON: bytes as base64, a map as its ordered entries. */
export function readableRaw(value: MpValue): Json {
  if (value === null || typeof value === 'boolean' || typeof value === 'string')
    return value;
  if (typeof value === 'number') return number(value);
  if (typeof value === 'bigint') return value;
  if (value instanceof Uint8Array) return { $bytes: base64(value) };
  if (value instanceof MpExt)
    return { $ext: value.type, $bytes: base64(value.data) };
  if (value instanceof MpMap)
    return {
      $map: value.entries.map(([k, v]) => [readableRaw(k), readableRaw(v)])
    };
  return value.map(readableRaw);
}

/** An enum value as `Name (n)`; `? (n)` when the schema has no name for it. */
export function enumLabel(value: EnumValue): string {
  return `${value.names.length ? value.names.join('/') : '?'} (${value.value})`;
}

function keyText(key: Json): string | null {
  if (typeof key === 'string') return key;
  if (typeof key === 'number' || typeof key === 'bigint') return String(key);
  return null;
}

export function readable(value: Translated): Json {
  if (value === null || typeof value === 'boolean' || typeof value === 'string')
    return value;
  if (typeof value === 'number') return number(value);
  if (typeof value === 'bigint') return value;
  if (value instanceof Uint8Array) return { $bytes: base64(value) };
  if (value instanceof EnumValue) return enumLabel(value);
  if (value instanceof RawValue)
    return { $raw: readableRaw(value.value), $flag: value.reason };
  if (value instanceof TMap) {
    const entries = value.entries.map(
      ([k, v]) => [readable(k), readable(v)] as const
    );
    const keys = entries.map(([k]) => keyText(k));
    // An object reads best; pairs keep keys that do not print as unique text.
    if (keys.every((k) => k !== null) && new Set(keys).size === keys.length) {
      return Object.fromEntries(entries.map(([, v], i) => [keys[i], v]));
    }
    return { $entries: entries.map(([k, v]) => [k, v]) };
  }
  if (value instanceof TObject) {
    // The qualified type, and the union tag it was read under, as the bytes carry them.
    const out: { [key: string]: Json } = { $type: value.type };
    if (value.tag !== null) out.$tag = value.tag;
    for (const [name, field] of Object.entries(value.fields))
      out[name] = readable(field);
    if (value.extra.length) {
      out.$extra = Object.fromEntries(
        value.extra.map(([key, item]) => [String(key), readable(item)])
      );
    }
    return out;
  }
  return value.map(readable);
}

/** JSON.stringify, except that a bigint prints as its exact digits. */
export function stringifyJson(value: Json, indent = 2): string {
  const pad = (depth: number) => '\n' + ' '.repeat(indent * depth);
  function write(v: Json, depth: number): string {
    if (typeof v === 'bigint') return v.toString();
    if (v === null || typeof v !== 'object') return JSON.stringify(v);
    if (Array.isArray(v)) {
      if (v.length === 0) return '[]';
      return `[${v.map((item) => pad(depth + 1) + write(item, depth + 1)).join(',')}${pad(depth)}]`;
    }
    const keys = Object.keys(v);
    if (keys.length === 0) return '{}';
    return `{${keys
      .map(
        (k) =>
          `${pad(depth + 1)}${JSON.stringify(k)}: ${write(v[k], depth + 1)}`
      )
      .join(',')}${pad(depth)}}`;
  }
  return write(value, 0);
}
