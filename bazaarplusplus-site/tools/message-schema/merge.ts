import type {
  GameMessageSchema,
  SchemaSource,
  SchemaType
} from '../../src/features/runs/message-stream/schema';
import { SCHEMA_ROOT } from '../../src/features/runs/message-stream/schema';

/** What extract.cs prints for one assembly. */
export type Extraction = {
  assembly: string;
  mode: 'attributes' | 'names';
  types: Record<string, ExtractedType>;
};

export type ExtractedType =
  | {
      kind: 'object';
      members: Array<[number, string, string] | [string, string]>;
    }
  | { kind: 'union'; cases: Array<[number, string]> }
  | {
      kind: 'enum';
      underlying: string;
      flags: boolean;
      values: Array<[string, string]>;
    }
  | { kind: 'interface' };

type Entries = Map<string, Set<string>>;

type TypeModel =
  | { kind: 'object'; sources: Set<string>; members: Entries; unkeyed: Entries }
  | { kind: 'union'; sources: Set<string>; cases: Entries }
  | {
      kind: 'enum';
      sources: Set<string>;
      underlying: string;
      flags: boolean;
      values: Entries;
    };

type Model = {
  sources: Map<string, SchemaSource>;
  types: Map<string, TypeModel>;
};

const SEP = '\u0000';

export const EMPTY_SCHEMA: GameMessageSchema = {
  format: 1,
  root: SCHEMA_ROOT,
  sources: [],
  types: {}
};

function add(entries: Entries, key: string, sources: Iterable<string>) {
  const set = entries.get(key) ?? new Set<string>();
  for (const source of sources) set.add(source);
  entries.set(key, set);
}

function toModel(schema: GameMessageSchema): Model {
  const types = new Map<string, TypeModel>();
  for (const [name, type] of Object.entries(schema.types)) {
    const sources = new Set(type.sources);
    const own = (list?: string[]) => list ?? type.sources;
    if (type.kind === 'object') {
      const members: Entries = new Map();
      const unkeyed: Entries = new Map();
      for (const m of type.members)
        add(members, [m.key, m.name, m.type].join(SEP), own(m.sources));
      for (const u of type.unkeyed ?? [])
        add(unkeyed, [u.name, u.type].join(SEP), u.sources);
      types.set(name, { kind: 'object', sources, members, unkeyed });
    } else if (type.kind === 'union') {
      const cases: Entries = new Map();
      for (const c of type.cases)
        add(cases, [c.tag, c.type].join(SEP), own(c.sources));
      types.set(name, { kind: 'union', sources, cases });
    } else {
      const values: Entries = new Map();
      for (const v of type.values)
        add(values, [v.name, v.value].join(SEP), own(v.sources));
      types.set(name, {
        kind: 'enum',
        sources,
        underlying: type.underlying,
        flags: type.flags,
        values
      });
    }
  }
  return {
    sources: new Map(schema.sources.map((s) => [s.id, s])),
    types
  };
}

const byText = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0);

function sorted(set: Set<string>): string[] {
  return [...set].sort(byText);
}

function same(a: Set<string>, b: Set<string>): boolean {
  return a.size === b.size && [...a].every((x) => b.has(x));
}

function fromModel(model: Model): GameMessageSchema {
  const types: Record<string, SchemaType> = {};
  for (const name of [...model.types.keys()].sort(byText)) {
    const type = model.types.get(name)!;
    const sources = sorted(type.sources);
    /** Sets `sources` on an entry whose sources differ from its type's. */
    const own = <T extends object>(entry: T, set: Set<string>): T => {
      if (!same(set, type.sources))
        Object.assign(entry, { sources: sorted(set) });
      return entry;
    };
    if (type.kind === 'object') {
      const members = [...type.members]
        .map(([key, set]) => {
          const [k, member, memberType] = key.split(SEP);
          return own({ key: Number(k), name: member, type: memberType }, set);
        })
        .sort(
          (a, b) =>
            a.key - b.key || byText(a.name, b.name) || byText(a.type, b.type)
        );
      const unkeyed = [...type.unkeyed]
        .map(([key, set]) => {
          const [member, memberType] = key.split(SEP);
          return { name: member, type: memberType, sources: sorted(set) };
        })
        .sort((a, b) => byText(a.name, b.name) || byText(a.type, b.type));
      types[name] = {
        kind: 'object',
        sources,
        members,
        ...(unkeyed.length ? { unkeyed } : {})
      };
    } else if (type.kind === 'union') {
      types[name] = {
        kind: 'union',
        sources,
        cases: [...type.cases]
          .map(([key, set]) => {
            const [tag, caseType] = key.split(SEP);
            return own({ tag: Number(tag), type: caseType }, set);
          })
          .sort((a, b) => a.tag - b.tag || byText(a.type, b.type))
      };
    } else {
      types[name] = {
        kind: 'enum',
        sources,
        underlying: type.underlying,
        flags: type.flags,
        values: [...type.values]
          .map(([key, set]) => {
            const [valueName, value] = key.split(SEP);
            return own({ name: valueName, value }, set);
          })
          .sort((a, b) => {
            const d = BigInt(a.value) - BigInt(b.value);
            return d < 0n ? -1 : d > 0n ? 1 : byText(a.name, b.name);
          })
      };
    }
  }
  return {
    format: 1,
    root: SCHEMA_ROOT,
    sources: [...model.sources.values()].sort((a, b) => byText(a.id, b.id)),
    types
  };
}

function removeSource(model: Model, id: string) {
  model.sources.delete(id);
  for (const [name, type] of model.types) {
    type.sources.delete(id);
    const lists =
      type.kind === 'object'
        ? [type.members, type.unkeyed]
        : type.kind === 'union'
          ? [type.cases]
          : [type.values];
    for (const entries of lists)
      for (const [key, set] of entries) {
        set.delete(id);
        if (set.size === 0) entries.delete(key);
      }
    if (type.sources.size === 0) model.types.delete(name);
  }
}

function addKeyed(model: Model, id: string, extraction: Extraction) {
  for (const [name, extracted] of Object.entries(extraction.types)) {
    if (extracted.kind === 'interface')
      throw new Error(
        `${name}: a keyed build has an interface without [Union]`
      );
    const existing = model.types.get(name);
    if (existing && existing.kind !== extracted.kind)
      throw new Error(`${name} is a ${existing.kind} and a ${extracted.kind}`);
    if (extracted.kind === 'object') {
      const type = (existing as
        | Extract<TypeModel, { kind: 'object' }>
        | undefined) ?? {
        kind: 'object',
        sources: new Set<string>(),
        members: new Map(),
        unkeyed: new Map()
      };
      type.sources.add(id);
      for (const member of extracted.members) {
        if (typeof member[0] !== 'number')
          throw new Error(`${name}: a keyed build has an unkeyed member`);
        const [key, memberName, memberType] = member as [
          number,
          string,
          string
        ];
        add(type.members, [key, memberName, memberType].join(SEP), [id]);
        // An inferred build named this member before a keyed build placed it.
        const inferred = [memberName, memberType].join(SEP);
        const named = type.unkeyed.get(inferred);
        if (named) {
          add(type.members, [key, memberName, memberType].join(SEP), named);
          type.unkeyed.delete(inferred);
        }
      }
      model.types.set(name, type);
    } else if (extracted.kind === 'union') {
      const type = (existing as
        | Extract<TypeModel, { kind: 'union' }>
        | undefined) ?? {
        kind: 'union',
        sources: new Set<string>(),
        cases: new Map()
      };
      type.sources.add(id);
      for (const [tag, caseType] of extracted.cases)
        add(type.cases, [tag, caseType].join(SEP), [id]);
      model.types.set(name, type);
    } else {
      const type = (existing as
        | Extract<TypeModel, { kind: 'enum' }>
        | undefined) ?? {
        kind: 'enum',
        sources: new Set<string>(),
        underlying: extracted.underlying,
        flags: extracted.flags,
        values: new Map()
      };
      if (
        type.underlying !== extracted.underlying ||
        type.flags !== extracted.flags
      )
        throw new Error(`${name} changed its underlying type or [Flags]`);
      type.sources.add(id);
      for (const [valueName, value] of extracted.values)
        add(type.values, [valueName, value].join(SEP), [id]);
      model.types.set(name, type);
    }
  }
}

/** An inferred build adds its source where its names match; it never places a member itself. */
function addInferred(model: Model, id: string, extraction: Extraction) {
  if (![...model.sources.values()].some((s) => s.layout === 'keyed'))
    throw new Error('Merge a keyed (desktop) snapshot before an inferred one');
  for (const [name, type] of model.types) {
    const extracted = extraction.types[name];
    if (!extracted) continue;
    type.sources.add(id);
    if (type.kind === 'object' && extracted.kind === 'object') {
      for (const member of extracted.members) {
        const [memberName, memberType] = member.slice(-2) as [string, string];
        let placed = false;
        for (const [key, set] of type.members) {
          const [, keyedName, keyedType] = key.split(SEP);
          if (keyedName === memberName && keyedType === memberType) {
            set.add(id);
            placed = true;
          }
        }
        if (!placed)
          add(type.unkeyed, [memberName, memberType].join(SEP), [id]);
      }
    } else if (type.kind === 'enum' && extracted.kind === 'enum') {
      for (const [valueName, value] of extracted.values)
        add(type.values, [valueName, value].join(SEP), [id]);
    }
  }
}

/**
 * Adds (or replaces) one source. Keyed sources merge in any order; an inferred source merges
 * after the keyed ones, since only they know the keys. Idempotent: merging a source again
 * replaces its earlier contribution.
 */
export function mergeSource(
  schema: GameMessageSchema,
  source: SchemaSource,
  extraction: Extraction
): GameMessageSchema {
  if ((source.layout === 'keyed') !== (extraction.mode === 'attributes'))
    throw new Error(
      `${source.id}: layout ${source.layout} needs another extraction mode`
    );
  const model = toModel(schema);
  removeSource(model, source.id);
  model.sources.set(source.id, source);
  if (source.layout === 'keyed') addKeyed(model, source.id, extraction);
  else addInferred(model, source.id, extraction);
  return fromModel(model);
}

/** The schema as the generator writes it: sorted, with `sources` left out where inherited. */
export function normalizeSchema(schema: GameMessageSchema): GameMessageSchema {
  return fromModel(toModel(schema));
}

export function serializeSchema(schema: GameMessageSchema): string {
  return `${JSON.stringify(schema, null, 2)}\n`;
}
