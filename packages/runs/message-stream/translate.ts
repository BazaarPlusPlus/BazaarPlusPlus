import { MpMap, type MpValue } from '../msgpack';
import {
  parseTypeExpr,
  shortName,
  type GameMessageSchema,
  type SchemaMember,
  type SchemaType,
  type TypeExpr
} from './schema';

/**
 * An object of a schema type. `extra` holds values at indices the schema cannot name; `tag` is
 * the union case it was read as, if any.
 */
export class TObject {
  readonly type: string;
  readonly fields: Record<string, Translated>;
  readonly extra: Array<[number, RawValue]>;
  readonly tag: number | null;
  constructor(
    type: string,
    fields: Record<string, Translated>,
    extra: Array<[number, RawValue]> = [],
    tag: number | null = null
  ) {
    this.type = type;
    this.fields = fields;
    this.extra = extra;
    this.tag = tag;
  }
}

/** An enum value with every name the schema has for it; no name when the value is unknown. */
export class EnumValue {
  readonly type: string;
  readonly value: number | bigint;
  readonly names: string[];
  constructor(type: string, value: number | bigint, names: string[]) {
    this.type = type;
    this.value = value;
    this.names = names;
  }
}

/** A dictionary, its keys translated with their own type. */
export class TMap {
  readonly entries: Array<[Translated, Translated]>;
  constructor(entries: Array<[Translated, Translated]>) {
    this.entries = entries;
  }
}

/** A value kept as the bytes decoded it, because the schema cannot name it. */
export class RawValue {
  readonly value: MpValue;
  readonly reason: string;
  constructor(value: MpValue, reason: string) {
    this.value = value;
    this.reason = reason;
  }
}

export type Translated =
  | null
  | boolean
  | number
  | bigint
  | string
  | Uint8Array
  | Translated[]
  | TObject
  | EnumValue
  | TMap
  | RawValue;

export type TranslationFlag = { path: string; reason: string };

const LISTS = new Set([
  'System.Collections.Generic.List',
  'System.Collections.Generic.IList',
  'System.Collections.Generic.IReadOnlyList',
  'System.Collections.Generic.IReadOnlyCollection',
  'System.Collections.Generic.ICollection',
  'System.Collections.Generic.IEnumerable',
  'System.Collections.Generic.HashSet',
  'System.Collections.Generic.ISet'
]);
const DICTIONARIES = new Set([
  'System.Collections.Generic.Dictionary',
  'System.Collections.Generic.IDictionary',
  'System.Collections.Generic.IReadOnlyDictionary'
]);
const INTEGERS = new Set([
  'System.Byte',
  'System.SByte',
  'System.Int16',
  'System.UInt16',
  'System.Int32',
  'System.UInt32',
  'System.Int64',
  'System.UInt64'
]);

type ObjectLayout = {
  byKey: Map<number, SchemaMember[]>;
  keysOf: Map<string, Set<number>>;
};

/** Looks types up in one schema; built once per schema. */
export class SchemaIndex {
  private readonly parsed = new Map<string, TypeExpr>();
  private readonly enums = new Map<string, Map<bigint, string[]>>();

  private readonly layouts = new Map<string, ObjectLayout>();

  readonly schema: GameMessageSchema;
  constructor(schema: GameMessageSchema) {
    this.schema = schema;
  }

  /** An object type's members by key, and every key each member name has in some build. */
  layout(name: string, members: SchemaMember[]): ObjectLayout {
    let found = this.layouts.get(name);
    if (!found) {
      const byKey = new Map<number, SchemaMember[]>();
      const keysOf = new Map<string, Set<number>>();
      for (const member of members) {
        byKey.set(member.key, [...(byKey.get(member.key) ?? []), member]);
        keysOf.set(
          member.name,
          (keysOf.get(member.name) ?? new Set()).add(member.key)
        );
      }
      found = { byKey, keysOf };
      this.layouts.set(name, found);
    }
    return found;
  }

  type(name: string): SchemaType | undefined {
    return Object.hasOwn(this.schema.types, name)
      ? this.schema.types[name]
      : undefined;
  }

  expr(text: string): TypeExpr {
    let found = this.parsed.get(text);
    if (!found) {
      found = parseTypeExpr(text);
      this.parsed.set(text, found);
    }
    return found;
  }

  enumNames(name: string, value: bigint): string[] {
    let values = this.enums.get(name);
    if (!values) {
      values = new Map();
      const type = this.type(name);
      if (type?.kind === 'enum')
        for (const v of type.values) {
          const key = BigInt(v.value);
          values.set(key, [...(values.get(key) ?? []), v.name]);
        }
      this.enums.set(name, values);
    }
    const exact = values.get(value);
    if (exact) return exact;
    // A [Flags] value without its own name is the names of its bits.
    const type = this.type(name);
    if (type?.kind !== 'enum' || !type.flags || value <= 0n) return [];
    const bits: string[] = [];
    let rest = value;
    for (const [bit, names] of [...values].sort((a, b) =>
      a[0] < b[0] ? -1 : 1
    )) {
      if (bit > 0n && (bit & (bit - 1n)) === 0n && (rest & bit) === bit) {
        bits.push(names[0]);
        rest &= ~bit;
      }
    }
    return rest === 0n ? [bits.join(' | ')] : [];
  }
}

type Context = { index: SchemaIndex; flags: TranslationFlag[] };

function raw(
  ctx: Context,
  path: string,
  value: MpValue,
  reason: string
): RawValue {
  ctx.flags.push({ path, reason });
  return new RawValue(value, reason);
}

function isInteger(value: MpValue): value is number | bigint {
  return (
    typeof value === 'bigint' ||
    (typeof value === 'number' && Number.isInteger(value))
  );
}

function translateObject(
  ctx: Context,
  name: string,
  members: SchemaMember[],
  value: MpValue,
  path: string
): Translated {
  if (value === null) return null;
  if (!Array.isArray(value)) {
    return raw(ctx, path, value, `expected ${shortName(name)} as an array`);
  }
  const { byKey, keysOf } = ctx.index.layout(name, members);
  const fields: Record<string, Translated> = {};
  const extra: Array<[number, RawValue]> = [];
  value.forEach((item, key) => {
    const candidates = byKey.get(key) ?? [];
    const names = new Set(candidates.map((c) => c.name));
    const types = new Set(candidates.map((c) => c.type));
    // A member that sits at another index in another build: which index holds it is unknown.
    const moved = candidates.find((c) => (keysOf.get(c.name)?.size ?? 0) > 1);
    if (moved) {
      extra.push([
        key,
        raw(
          ctx,
          `${path}[${key}]`,
          item,
          `${moved.name} is at index ${[...(keysOf.get(moved.name) ?? [])].sort((a, b) => a - b).join(' or ')} across builds`
        )
      ]);
      return;
    }
    if (candidates.length > 0 && names.size === 1 && types.size === 1) {
      const member = candidates[0];
      fields[member.name] = translateValue(
        ctx,
        ctx.index.expr(member.type),
        item,
        `${path}.${member.name}`
      );
      return;
    }
    // A nil at an index no build names is an ordinary gap.
    if (item === null && candidates.length === 0) return;
    extra.push([
      key,
      raw(
        ctx,
        `${path}[${key}]`,
        item,
        candidates.length === 0
          ? 'unnamed index'
          : `index ${key} is ${candidates.map((c) => `${c.name}: ${shortName(c.type)}`).join(' or ')} across builds`
      )
    ]);
  });
  return new TObject(name, fields, extra);
}

function translateNamed(
  ctx: Context,
  name: string,
  value: MpValue,
  path: string
): Translated {
  const type = ctx.index.type(name);
  if (!type) return raw(ctx, path, value, `unknown type ${name}`);
  if (type.kind === 'enum') {
    if (!isInteger(value)) {
      return raw(ctx, path, value, `expected ${shortName(name)}`);
    }
    const names = ctx.index.enumNames(name, BigInt(value));
    if (names.length === 0) {
      ctx.flags.push({ path, reason: `unknown ${shortName(name)} value` });
    }
    return new EnumValue(name, value, names);
  }
  if (type.kind === 'object') {
    return translateObject(ctx, name, type.members, value, path);
  }
  if (value === null) return null;
  if (!Array.isArray(value) || value.length !== 2 || !isInteger(value[0])) {
    return raw(ctx, path, value, `expected ${shortName(name)} as [tag, body]`);
  }
  const tag = Number(value[0]);
  const cases = type.cases.filter((c) => c.tag === tag);
  if (cases.length !== 1) {
    return raw(
      ctx,
      path,
      value,
      cases.length === 0
        ? `unknown ${shortName(name)} case ${tag}`
        : `${shortName(name)} case ${tag} is ${cases.map((c) => shortName(c.type)).join(' or ')} across builds`
    );
  }
  const body = translateNamed(ctx, cases[0].type, value[1], path);
  if (body instanceof TObject) {
    return new TObject(body.type, body.fields, body.extra, tag);
  }
  return raw(
    ctx,
    path,
    value,
    `${shortName(name)} case ${tag} is not an object`
  );
}

function translateValue(
  ctx: Context,
  expr: TypeExpr,
  value: MpValue,
  path: string
): Translated {
  if ('array' in expr) {
    if (value === null) return null;
    const element = expr.array;
    if (!('array' in element) && element.name === 'System.Byte') {
      return value instanceof Uint8Array
        ? value
        : raw(ctx, path, value, 'expected bytes');
    }
    if (!Array.isArray(value)) return raw(ctx, path, value, 'expected a list');
    return value.map((item, i) =>
      translateValue(ctx, element, item, `${path}[${i}]`)
    );
  }
  const { name, args } = expr;
  if (name === 'System.Nullable') {
    return value === null ? null : translateValue(ctx, args[0], value, path);
  }
  if (LISTS.has(name)) {
    if (value === null) return null;
    if (!Array.isArray(value)) return raw(ctx, path, value, 'expected a list');
    return value.map((item, i) =>
      translateValue(ctx, args[0], item, `${path}[${i}]`)
    );
  }
  if (DICTIONARIES.has(name)) {
    if (value === null) return null;
    if (!(value instanceof MpMap)) {
      return raw(ctx, path, value, 'expected a dictionary');
    }
    return new TMap(
      value.entries.map(([key, item], i) => [
        translateValue(ctx, args[0], key, `${path}{${i}}`),
        translateValue(ctx, args[1], item, `${path}[${keyLabel(key)}]`)
      ])
    );
  }
  if (INTEGERS.has(name)) {
    return isInteger(value) ? value : raw(ctx, path, value, `expected ${name}`);
  }
  if (name === 'System.Single' || name === 'System.Double') {
    return typeof value === 'number'
      ? value
      : raw(ctx, path, value, `expected ${name}`);
  }
  if (name === 'System.Boolean') {
    return typeof value === 'boolean'
      ? value
      : raw(ctx, path, value, 'expected a boolean');
  }
  // MessagePack-CSharp's standard resolver writes a Guid as its text.
  if (name === 'System.String' || name === 'System.Guid') {
    return value === null || typeof value === 'string'
      ? value
      : raw(ctx, path, value, `expected ${name}`);
  }
  if (name.startsWith('System.') || name.startsWith('!')) {
    return raw(ctx, path, value, `no reader for ${name}`);
  }
  return translateNamed(ctx, name, value, path);
}

function keyLabel(key: MpValue): string {
  return typeof key === 'string' ||
    typeof key === 'number' ||
    typeof key === 'bigint'
    ? String(key)
    : '?';
}

export type TranslatedMessage = {
  value: Translated;
  flags: TranslationFlag[];
};

/** Translates one decompressed message, `[tag, body]` of the schema's root union. */
export function translateMessage(
  index: SchemaIndex,
  message: MpValue
): TranslatedMessage {
  const ctx: Context = { index, flags: [] };
  const value = translateNamed(ctx, index.schema.root, message, 'Message');
  return { value, flags: ctx.flags };
}

/** Counts non-nil scalars, map keys included, for the no-loss check. */
export function countInputLeaves(value: MpValue): number {
  if (value === null) return 0;
  if (Array.isArray(value))
    return value.reduce<number>((n, v) => n + countInputLeaves(v), 0);
  if (value instanceof MpMap)
    return value.entries.reduce(
      (n, [k, v]) => n + countInputLeaves(k) + countInputLeaves(v),
      0
    );
  return 1;
}

/** Counts the input scalars a translation holds; a union case holds its tag. */
export function countOutputLeaves(value: Translated): number {
  if (value === null) return 0;
  if (value instanceof RawValue) return countInputLeaves(value.value);
  if (Array.isArray(value))
    return value.reduce<number>((n, v) => n + countOutputLeaves(v), 0);
  if (value instanceof TMap)
    return value.entries.reduce(
      (n, [k, v]) => n + countOutputLeaves(k) + countOutputLeaves(v),
      0
    );
  if (value instanceof TObject)
    return (
      (value.tag === null ? 0 : 1) +
      Object.values(value.fields).reduce<number>(
        (n, v) => n + countOutputLeaves(v),
        0
      ) +
      value.extra.reduce((n, [, v]) => n + countOutputLeaves(v), 0)
    );
  return 1;
}
