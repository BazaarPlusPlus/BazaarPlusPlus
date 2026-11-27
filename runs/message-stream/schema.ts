/**
 * The Game Message Schema: the MessagePack layout of the game's network messages, merged from
 * every Game Assembly Snapshot that runs/tools/message-schema/generate.ts read. One schema serves
 * every Run: the mod records a message as the client re-serialized it, so a Run's bytes follow
 * its client build, and every client build the schema read is in it.
 *
 * A type expression is a qualified name, `Name<Arg,...>` or `Element[]`, e.g.
 * `System.Nullable<BazaarGameShared.Domain.Core.InstanceId>`.
 */
export type SchemaSource = {
  /** `<platform>-<build>`, e.g. `macos-13176`. Members and values list these ids. */
  id: string;
  /** The snapshot's game version, as the Snapshot Lock records it. */
  gameVersion: string;
  platform: 'macos' | 'windows' | 'android';
  /** `keyed`: read from [Key] and [Union]; `inferred`: names only (Android), keys taken from a keyed build. */
  layout: 'keyed' | 'inferred';
};

/** `sources` is left out when it equals the enclosing type's. */
export type SchemaMember = {
  key: number;
  name: string;
  type: string;
  sources?: string[];
};

export type SchemaUnkeyed = { name: string; type: string; sources: string[] };

export type SchemaCase = { tag: number; type: string; sources?: string[] };

/** `value` is text so a 64-bit constant stays exact. */
export type SchemaEnumValue = {
  name: string;
  value: string;
  sources?: string[];
};

export type SchemaType =
  | {
      kind: 'object';
      sources: string[];
      /** Sorted by key; two entries with one key are a conflict between builds. */
      members: SchemaMember[];
      /** Members an inferred build has that no keyed build places: their index is unknown. */
      unkeyed?: SchemaUnkeyed[];
    }
  | { kind: 'union'; sources: string[]; cases: SchemaCase[] }
  | {
      kind: 'enum';
      sources: string[];
      underlying: string;
      flags: boolean;
      values: SchemaEnumValue[];
    };

export type GameMessageSchema = {
  format: 1;
  root: string;
  sources: SchemaSource[];
  types: Record<string, SchemaType>;
};

export const SCHEMA_ROOT = 'BazaarGameShared.Infra.Messages.INetMessage';

/** A parsed type expression. */
export type TypeExpr = { name: string; args: TypeExpr[] } | { array: TypeExpr };

export function parseTypeExpr(text: string): TypeExpr {
  let at = 0;
  function read(): TypeExpr {
    const start = at;
    while (at < text.length && !'<>,['.includes(text[at])) at++;
    const name = text.slice(start, at);
    if (!name) throw new Error(`Invalid type expression ${text}`);
    const args: TypeExpr[] = [];
    if (text[at] === '<') {
      at++;
      for (;;) {
        args.push(read());
        if (text[at] === ',') {
          at++;
          continue;
        }
        if (text[at] !== '>')
          throw new Error(`Invalid type expression ${text}`);
        at++;
        break;
      }
    }
    let type: TypeExpr = { name, args };
    while (text.startsWith('[]', at)) {
      at += 2;
      type = { array: type };
    }
    return type;
  }
  const type = read();
  if (at !== text.length) throw new Error(`Invalid type expression ${text}`);
  return type;
}

export function formatTypeExpr(type: TypeExpr): string {
  if ('array' in type) return `${formatTypeExpr(type.array)}[]`;
  return type.args.length
    ? `${type.name}<${type.args.map(formatTypeExpr).join(',')}>`
    : type.name;
}

/** The last segment of a qualified name, for display: `NetMessageGameSim`. */
export function shortName(name: string): string {
  const plain = name.slice(name.lastIndexOf('.') + 1);
  return plain.slice(plain.lastIndexOf('+') + 1);
}
