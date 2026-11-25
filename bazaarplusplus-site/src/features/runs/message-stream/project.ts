import { shortName } from './schema';
import type { MessageStream, StreamFrame } from './stream';
import {
  EnumValue,
  RawValue,
  TMap,
  TObject,
  type Translated
} from './translate';

/** A card as the stream knew it at one moment; an offer freezes it when it is shown. */
export type CardRef = {
  instanceId: string;
  templateId: string | null;
  /** ECardType member name, e.g. `Item`, `EventEncounter`. */
  type: string | null;
  tier: string | null;
  enchant: string | null;
  /** Its BuyPrice attribute then; null when the stream did not carry one. */
  price: number | null;
};

export type Verb =
  | 'start'
  | 'reconnect'
  | 'chose'
  | 'bought'
  | 'picked'
  | 'sold'
  | 'rerolled'
  | 'moved'
  | 'skill'
  | 'upgraded'
  | 'enchanted'
  | 'committed'
  | 'left'
  | 'other';

/**
 * `done`: a paired response shows the effect. `attempted`: responses came, none shows it (a
 * NetMessageError among them sets `error`). `no-response`: nothing answered the command.
 */
export type Outcome = 'done' | 'attempted' | 'no-response';

type Placed = {
  /** Sort key on the Seq timeline. */
  order: number;
  day: number | null;
  hour: number | null;
  /** Every frame this entry is read from, in Seq order. */
  frames: number[];
};

export type DecisionEntry = Placed &
  (
    | {
        kind: 'offer';
        /** ERunState member name. */
        state: string;
        encounter: CardRef | null;
        cards: CardRef[];
        rerollCost: number | null;
        rerollsRemaining: number | null;
        /** The game's SelectionContextRules.SelectionIsFree. */
        free: boolean;
      }
    | {
        kind: 'action';
        command: string;
        verb: Verb;
        card: CardRef | null;
        /** Price paid or sell price, from the paired event. */
        amount: number | null;
        /** Section and socket a move or a buy targeted. */
        target: string | null;
        /** Tier or enchantment a pedestal gave. */
        result: string | null;
        outcome: Outcome;
        error: string | null;
        gold: number | null;
        health: number | null;
      }
    | { kind: 'battle'; battleId: string }
    | { kind: 'reconnect' }
    | {
        kind: 'update';
        gold: number | null;
        health: number | null;
      }
    | { kind: 'undecoded'; error: string }
  );

export type BattleMarker = {
  id: string;
  /** The `pvp_combat_recorded` (or other) event's Seq when known. */
  seq: number | null;
  recordedAtMs: number;
  day: number | null;
  hour: number | null;
  /** A PvP Battle sits after the PVPCombat state it opened; a monster fight after Combat. */
  pvp: boolean;
};

type CardState = {
  templateId: string | null;
  type: string | null;
  tier: string | null;
  enchant: string | null;
  attributes: Map<string, number>;
};

type RunState = {
  /** False after an undecodable message, until a GameStateSync gives a new baseline. */
  known: boolean;
  day: number | null;
  hour: number | null;
  stateName: string | null;
  encounterId: string | null;
  selection: string[];
  rerollCost: number | null;
  rerollsRemaining: number | null;
  free: boolean;
  player: Map<string, number>;
  cards: Map<string, CardState>;
};

/** What one decoded message showed, for pairing with its command. */
type Evidence = {
  purchased: Map<string, number>;
  sold: Map<string, number>;
  moved: Map<string, string>;
  equipped: Set<string>;
  /** A bought or picked duplicate merges into the card it upgrades: selected id → that card. */
  fused: Map<string, { into: string; tier: string | null }>;
  upgraded: Map<string, string>;
  enchanted: Map<string, string>;
  transitioned: boolean;
  dealt: boolean;
  initialized: boolean;
  synced: boolean;
  errors: string[];
};

function emptyEvidence(): Evidence {
  return {
    purchased: new Map(),
    sold: new Map(),
    moved: new Map(),
    equipped: new Set(),
    fused: new Map(),
    upgraded: new Map(),
    enchanted: new Map(),
    transitioned: false,
    dealt: false,
    initialized: false,
    synced: false,
    errors: []
  };
}

function field(
  value: Translated | undefined,
  name: string
): Translated | undefined {
  return value instanceof TObject ? value.fields[name] : undefined;
}

function text(value: Translated | undefined): string | null {
  return typeof value === 'string' ? value : null;
}

function int(value: Translated | undefined): number | null {
  return typeof value === 'number' && Number.isInteger(value) ? value : null;
}

function enumName(value: Translated | undefined): string | null {
  return value instanceof EnumValue && value.names.length
    ? value.names[0]
    : null;
}

function enumKeyed(value: Translated | undefined): Array<[string, Translated]> {
  if (!(value instanceof TMap)) return [];
  return value.entries.flatMap(([k, v]) => {
    const key = enumName(k) ?? (typeof k === 'string' ? k : null);
    return key === null ? [] : [[key, v] as [string, Translated]];
  });
}

function list(value: Translated | undefined): Translated[] {
  return Array.isArray(value) ? value : [];
}

function newState(): RunState {
  return {
    known: true,
    day: null,
    hour: null,
    stateName: null,
    encounterId: null,
    selection: [],
    rerollCost: null,
    rerollsRemaining: null,
    free: false,
    player: new Map(),
    cards: new Map()
  };
}

function card(state: RunState, id: string): CardState {
  let found = state.cards.get(id);
  if (!found) {
    found = {
      templateId: null,
      type: null,
      tier: null,
      enchant: null,
      attributes: new Map()
    };
    state.cards.set(id, found);
  }
  return found;
}

export function cardRef(state: RunState, id: string): CardRef {
  const c = state.cards.get(id);
  return {
    instanceId: id,
    templateId: c?.templateId ?? null,
    type: c?.type ?? null,
    tier: c?.tier ?? null,
    enchant: c?.enchant ?? null,
    price: c?.attributes.get('BuyPrice') ?? null
  };
}

/** CurrentEncounterId holds the encounter's TemplateId, not an instance id. */
function encounterRef(state: RunState): CardRef | null {
  const templateId = state.encounterId?.toLowerCase();
  if (!templateId) return null;
  for (const [id, c] of state.cards)
    if (c.templateId === templateId) return cardRef(state, id);
  return {
    instanceId: '',
    templateId,
    type: null,
    tier: null,
    enchant: null,
    price: null
  };
}

function applyRunState(state: RunState, current: Translated | undefined) {
  if (!(current instanceof TObject)) return;
  state.stateName = enumName(field(current, 'StateName')) ?? state.stateName;
  state.encounterId = text(field(current, 'CurrentEncounterId'));
  state.selection = list(field(current, 'SelectionSet')).flatMap((id) =>
    typeof id === 'string' ? [id] : []
  );
  state.rerollCost = int(field(current, 'RerollCost'));
  state.rerollsRemaining = int(field(current, 'RerollsRemaining'));
  state.free =
    field(field(current, 'SelectionContextRules'), 'SelectionIsFree') === true;
}

function applyRun(state: RunState, run: Translated | undefined) {
  if (!(run instanceof TObject)) return;
  state.day = int(field(run, 'Day')) ?? state.day;
  state.hour = int(field(run, 'Hour')) ?? state.hour;
}

/** Applies a GameStateSync: the whole state is replaced, a new baseline. */
function applySync(state: RunState, data: Translated | undefined) {
  Object.assign(state, newState());
  applyRun(state, field(data, 'Run'));
  applyRunState(state, field(data, 'CurrentState'));
  for (const [name, value] of enumKeyed(
    field(field(data, 'Player'), 'Attributes')
  )) {
    const n = int(value);
    if (n !== null) state.player.set(name, n);
  }
  for (const snapshot of list(field(data, 'Cards'))) {
    const id = text(field(snapshot, 'InstanceId'));
    if (id === null) continue;
    const c = card(state, id);
    c.templateId = text(field(snapshot, 'TemplateId'))?.toLowerCase() ?? null;
    c.type = enumName(field(snapshot, 'Type'));
    c.tier = enumName(field(snapshot, 'Tier'));
    c.enchant = enumName(field(snapshot, 'Enchantment'));
    for (const [name, value] of enumKeyed(field(snapshot, 'Attributes'))) {
      const n = int(value);
      if (n !== null) c.attributes.set(name, n);
    }
  }
}

function updateTemplate(c: CardState, snapshot: Translated) {
  const templateId = text(field(snapshot, 'TemplateId'))?.toLowerCase();
  if (templateId && templateId !== c.templateId) {
    c.templateId = templateId;
    c.enchant = null;
  }
  c.type = enumName(field(snapshot, 'Type')) ?? c.type;
}

function applyGameSim(
  state: RunState,
  data: Translated | undefined,
  evidence: Evidence
) {
  for (const event of list(field(data, 'Events'))) {
    if (!(event instanceof TObject)) continue;
    const id = text(field(event, 'InstanceId'));
    switch (shortName(event.type)) {
      case 'GameSimEventCardDealt':
      case 'GameSimEventCardSpawned':
        if (id !== null) {
          const c = card(state, id);
          updateTemplate(c, event);
        }
        evidence.dealt = true;
        break;
      case 'GameSimEventCardTransformed':
        for (const t of list(field(event, 'TransformedCards'))) {
          const tid = text(field(t, 'InstanceId'));
          if (tid === null) continue;
          const c = card(state, tid);
          updateTemplate(c, t);
        }
        break;
      case 'GameSimEventCardFused': {
        const selected = text(field(event, 'SelectedInstanceId'));
        const into = text(field(event, 'FusedInstanceId'));
        const tier = enumName(field(event, 'NewTier'));
        if (selected !== null && into !== null) {
          evidence.fused.set(selected, { into, tier });
          if (tier) card(state, into).tier = tier;
        }
        break;
      }
      case 'GameSimEventCardPurchased':
        if (id !== null)
          evidence.purchased.set(id, int(field(event, 'BuyPrice')) ?? 0);
        break;
      case 'GameSimEventCardSold':
        if (id !== null)
          evidence.sold.set(id, int(field(event, 'SellPrice')) ?? 0);
        break;
      case 'GameSimEventCardMoved':
        if (id !== null)
          evidence.moved.set(
            id,
            [
              enumName(field(event, 'ToInventory')),
              enumName(field(event, 'ToSocket'))
            ]
              .filter(Boolean)
              .join(' ')
          );
        break;
      case 'GameSimEventPlayerSkillEquipped':
        if (id !== null) evidence.equipped.add(id);
        break;
      case 'GameSimEventCardUpgraded':
        if (id !== null) {
          const tier = enumName(field(event, 'NewTier'));
          evidence.upgraded.set(id, tier ?? '');
          if (tier) card(state, id).tier = tier;
        }
        break;
      case 'GameSimEventCardEnchanted':
        if (id !== null) {
          const enchant = enumName(field(event, 'EnchantmentType'));
          evidence.enchanted.set(id, enchant ?? '');
          card(state, id).enchant = enchant;
        }
        break;
      case 'GameSimEventStateTransitioned':
        evidence.transitioned = true;
        break;
      default:
        break;
    }
  }
  applyRun(state, field(data, 'Run'));
  for (const [name, value] of enumKeyed(
    field(field(data, 'Player'), 'Attributes')
  )) {
    const n = int(value);
    if (n !== null) state.player.set(name, n);
  }
  const cards = field(data, 'Cards');
  if (cards instanceof TMap)
    for (const [key, update] of cards.entries) {
      const id = text(key);
      if (id === null || !(update instanceof TObject)) continue;
      const c = card(state, id);
      c.enchant = enumName(field(update, 'Enchantment')) ?? c.enchant;
      c.tier = enumName(field(update, 'Tier')) ?? c.tier;
      for (const [name, delta] of enumKeyed(field(update, 'Attributes'))) {
        // An AttributeDelta of type Delete removes the attribute, e.g. a price.
        if (enumName(field(delta, 'DeltaType')) === 'Delete')
          c.attributes.delete(name);
        else {
          const n = int(field(delta, 'Value'));
          if (n !== null) c.attributes.set(name, n);
        }
      }
    }
  applyRunState(state, field(data, 'CurrentState'));
}

type Pending = {
  entry: Extract<DecisionEntry, { kind: 'action' }>;
  args: Record<string, unknown>;
  evidence: Evidence;
  responses: number;
  /** Gold and health changes summed over the paired responses; null once one is unreadable. */
  gold: number | null;
  health: number | null;
  before: {
    selection: string[];
    free: boolean;
  };
};

const VERBS: Record<string, Verb> = {
  SendInitializeRun: 'start',
  ReestablishSession: 'reconnect',
  SendSelectEncounter: 'chose',
  SendSelectItem: 'bought',
  SendSellCard: 'sold',
  SendReRollSelection: 'rerolled',
  SendMoveItem: 'moved',
  SendSelectSkill: 'skill',
  SendCommitToPedestal: 'committed',
  SendExitCurrentState: 'left'
};

function argId(args: Record<string, unknown>): string | null {
  for (const key of ['instanceId', 'cardId', 'id']) {
    if (typeof args[key] === 'string') return args[key];
  }
  return null;
}

/** Positive evidence from the paired responses decides the verb and the outcome. */
function settle(pending: Pending, state: RunState) {
  const { entry, args, evidence } = pending;
  const id = argId(args);
  let done = false;
  switch (entry.command) {
    case 'SendInitializeRun':
      done = evidence.initialized;
      break;
    case 'ReestablishSession':
      done = evidence.synced;
      break;
    case 'SendSelectEncounter':
      done = id !== null && evidence.purchased.has(id);
      break;
    case 'SendSelectItem': {
      const fused = id === null ? undefined : evidence.fused.get(id);
      const bought = fused ? fused.into : id;
      if (bought !== null && evidence.purchased.has(bought)) {
        done = true;
        entry.amount = evidence.purchased.get(bought)!;
        entry.result = fused?.tier ?? null;
        // The game's own price context, not a zero price, makes a pick free.
        if (pending.before.free) entry.verb = 'picked';
      }
      if (
        Array.isArray(args.desiredSockets) ||
        typeof args.targetSection === 'string'
      )
        entry.target = [
          args.targetSection,
          ...((args.desiredSockets as unknown[]) ?? [])
        ]
          .filter((x) => typeof x === 'string')
          .join(' ');
      break;
    }
    case 'SendSellCard':
      if (id !== null && evidence.sold.has(id)) {
        done = true;
        entry.amount = evidence.sold.get(id)!;
      }
      break;
    case 'SendMoveItem':
      if (id !== null && evidence.moved.has(id)) {
        done = true;
        entry.target = evidence.moved.get(id)!;
      }
      break;
    case 'SendReRollSelection':
      // Only a selection with new cards shows the reroll happened.
      done = state.selection.some((x) => !pending.before.selection.includes(x));
      break;
    case 'SendSelectSkill': {
      const fused = id === null ? undefined : evidence.fused.get(id);
      done =
        id !== null &&
        (evidence.equipped.has(id) ||
          evidence.purchased.has(id) ||
          fused !== undefined);
      entry.result = fused?.tier ?? null;
      break;
    }
    case 'SendCommitToPedestal':
      if (id !== null && evidence.upgraded.has(id)) {
        done = true;
        entry.verb = 'upgraded';
        entry.result = evidence.upgraded.get(id) || null;
      } else if (id !== null && evidence.enchanted.has(id)) {
        done = true;
        entry.verb = 'enchanted';
        entry.result = evidence.enchanted.get(id) || null;
      }
      break;
    case 'SendExitCurrentState':
      done = evidence.transitioned;
      break;
    default:
      done = false;
  }
  entry.outcome = done
    ? 'done'
    : pending.responses === 0
      ? 'no-response'
      : 'attempted';
  entry.error = evidence.errors[0] ?? null;
  entry.gold = pending.gold;
  entry.health = pending.health;
}

/** Adds one paired response's change; an unreadable response makes the sum unknown. */
function attribute(
  pending: Pending,
  before: { gold: number | null; health: number | null; known: boolean },
  state: RunState,
  baseline: boolean
) {
  // A GameStateSync is a new baseline, not a change the command caused.
  if (baseline) return;
  if (!before.known || !state.known) {
    pending.gold = null;
    pending.health = null;
    return;
  }
  const gold = state.player.get('Gold') ?? null;
  const health = state.player.get('Health') ?? null;
  pending.gold =
    pending.gold === null || gold === null || before.gold === null
      ? null
      : pending.gold + gold - before.gold;
  pending.health =
    pending.health === null || health === null || before.health === null
      ? null
      : pending.health + health - before.health;
}

function parseArgs(frame: StreamFrame): Record<string, unknown> {
  if (frame.commandArgsJson === null) return {};
  try {
    const parsed: unknown = JSON.parse(frame.commandArgsJson);
    return parsed && typeof parsed === 'object'
      ? (parsed as Record<string, unknown>)
      : {};
  } catch {
    return {};
  }
}

const STATE_PARTS = ['Run', 'Player', 'Cards', 'CurrentState', 'Events'];

/** Yields to the browser between chunks so a long Run does not block a phone's main thread. */
const CHUNK = 64;
const pause = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

/**
 * Folds the stream in Seq order into decision entries. Every frame is referenced by exactly
 * the entries it feeds; a frame no rule reads still appears (an `update`), so every frame is
 * reachable from the path.
 */
export async function projectDecisions(
  stream: MessageStream,
  battles: readonly BattleMarker[],
  yieldEvery: number = CHUNK
): Promise<DecisionEntry[]> {
  const state = newState();
  const entries: DecisionEntry[] = [];
  const pending = new Map<number, Pending>();
  /** Seqs of the messages that entered a combat state, by state name. */
  const combats: Record<'PVPCombat' | 'Combat', number[]> = {
    PVPCombat: [],
    Combat: []
  };

  for (const [i, frame] of stream.frames.entries()) {
    // oxlint-disable-next-line no-await-in-loop -- the fold is sequential; this only yields.
    if (yieldEvery > 0 && i > 0 && i % yieldEvery === 0) await pause();
    if (frame.command !== null) {
      const entry: Extract<DecisionEntry, { kind: 'action' }> = {
        kind: 'action',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        command: frame.command,
        verb: VERBS[frame.command] ?? 'other',
        card: null,
        amount: null,
        target: null,
        result: null,
        outcome: 'no-response',
        error: null,
        gold: null,
        health: null
      };
      const args = parseArgs(frame);
      const id = argId(args);
      if (id !== null) entry.card = cardRef(state, id);
      entries.push(entry);
      pending.set(frame.seq, {
        entry,
        args,
        evidence: emptyEvidence(),
        responses: 0,
        gold: state.known ? 0 : null,
        health: state.known ? 0 : null,
        before: { selection: state.selection, free: state.free }
      });
      continue;
    }
    if (frame.message === null) {
      entries.push({
        kind: 'update',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        gold: null,
        health: null
      });
      continue;
    }
    const owner =
      frame.respondsToSeq === null
        ? undefined
        : pending.get(frame.respondsToSeq);
    if (owner) {
      owner.entry.frames.push(frame.seq);
      owner.responses++;
    }
    const decoded = stream.message(frame);
    if (!decoded || !decoded.ok) {
      // The missing delta makes later inferences unsafe until a baseline returns.
      state.known = false;
      entries.push({
        kind: 'undecoded',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        error: decoded && !decoded.ok ? decoded.error : ''
      });
      if (owner) {
        owner.gold = null;
        owner.health = null;
        settle(owner, state);
      }
      continue;
    }
    const data = field(decoded.value, 'Data');
    // A state message whose parts the schema could not read is as good as missing.
    const partial =
      (decoded.type === 'NetMessageGameSim' ||
        decoded.type === 'NetMessageGameStateSync') &&
      (!(data instanceof TObject) ||
        STATE_PARTS.some((part) => data.fields[part] instanceof RawValue));
    if (partial) {
      state.known = false;
      entries.push({
        kind: 'undecoded',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        error: decoded.flags.map((f) => `${f.path}: ${f.reason}`).join('; ')
      });
      if (owner) {
        owner.gold = null;
        owner.health = null;
        settle(owner, state);
      }
      continue;
    }
    const evidence = owner?.evidence ?? emptyEvidence();
    const before = {
      selection: state.selection,
      stateName: state.stateName,
      encounterId: state.encounterId,
      gold: state.player.get('Gold') ?? null,
      health: state.player.get('Health') ?? null,
      known: state.known
    };
    switch (decoded.type) {
      case 'NetMessageGameSim':
        applyGameSim(state, data, evidence);
        break;
      case 'NetMessageGameStateSync':
        applySync(state, data);
        evidence.synced = true;
        break;
      case 'NetMessageRunInitialized':
        evidence.initialized = true;
        break;
      case 'NetMessageError':
        evidence.errors.push(
          [
            enumName(field(decoded.value, 'ErrorType')),
            text(field(decoded.value, 'Message'))
          ]
            .filter(Boolean)
            .join(': ')
        );
        break;
      default:
        break;
    }
    if (
      (state.stateName === 'PVPCombat' || state.stateName === 'Combat') &&
      before.stateName !== state.stateName
    ) {
      combats[state.stateName].push(frame.seq);
    }
    let read = owner !== undefined;
    if (evidence.synced && !owner) {
      entries.push({
        kind: 'reconnect',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq]
      });
      read = true;
    }
    // A new offer: new cards, or a new state. A card leaving the offer (bought) is not one.
    const baseline = decoded.type === 'NetMessageGameStateSync';
    // A sync is a fresh baseline: its selection is shown again, with its own prices.
    const shown =
      state.known &&
      state.selection.length > 0 &&
      (baseline ||
        !before.known ||
        state.selection.some((id) => !before.selection.includes(id)) ||
        before.stateName !== state.stateName ||
        before.encounterId !== state.encounterId);
    if (shown) {
      entries.push({
        kind: 'offer',
        order: frame.seq + 0.5,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        state: state.stateName ?? '',
        encounter: encounterRef(state),
        cards: state.selection.map((id) => cardRef(state, id)),
        rerollCost: state.rerollCost,
        rerollsRemaining: state.rerollsRemaining,
        free: state.free
      });
      read = true;
    }
    if (!read) {
      const gold = state.player.get('Gold') ?? null;
      const health = state.player.get('Health') ?? null;
      const known = before.known && state.known;
      entries.push({
        kind: 'update',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        gold:
          known && gold !== null && before.gold !== null && gold !== before.gold
            ? gold - before.gold
            : null,
        health:
          known &&
          health !== null &&
          before.health !== null &&
          health !== before.health
            ? health - before.health
            : null
      });
    }
    if (owner) {
      attribute(owner, before, state, baseline);
      settle(owner, state);
    }
  }

  for (const battle of battles) {
    // A Battle sits right after the message that entered its combat.
    const anchor =
      battle.seq === null
        ? (stream.frames.find((f) => f.timestampMs >= battle.recordedAtMs)
            ?.seq ?? Number.MAX_SAFE_INTEGER)
        : battle.seq;
    const entered = [...combats[battle.pvp ? 'PVPCombat' : 'Combat']]
      .reverse()
      .find((seq) => seq < anchor);
    entries.push({
      kind: 'battle',
      order: (entered ?? anchor) + 0.25,
      day: battle.day,
      hour: battle.hour,
      frames: [],
      battleId: battle.id
    });
  }
  return entries.sort((a, b) => a.order - b.order);
}

export type HourGroup = {
  day: number | null;
  hour: number | null;
  entries: DecisionEntry[];
};

/**
 * Consecutive entries of one day and hour. Entries before the first known hour (the start of
 * the Run) join the first group that has one.
 */
export function groupByHour(entries: readonly DecisionEntry[]): HourGroup[] {
  const groups: HourGroup[] = [];
  let leading: DecisionEntry[] = [];
  for (const entry of entries) {
    if (entry.day === null && groups.length === 0) {
      leading.push(entry);
      continue;
    }
    const last = groups.at(-1);
    if (last && last.day === entry.day && last.hour === entry.hour)
      last.entries.push(entry);
    else {
      groups.push({
        day: entry.day,
        hour: entry.hour,
        entries: [...leading, entry]
      });
      leading = [];
    }
  }
  if (leading.length) groups.push({ day: null, hour: null, entries: leading });
  return groups;
}
