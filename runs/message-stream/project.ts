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
  tag: 'Merchant' | 'Event' | null;
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
    | {
        kind: 'battle';
        battleId: string;
        pvp: boolean;
        encounterId: string | null;
      }
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
  encounterId?: string | null;
  recordedAtMs: number;
  day: number | null;
  hour: number | null;
  /** A PvP Battle sits after the PVPCombat state it opened; a monster fight after Combat. */
  pvp: boolean;
};

type PlacedCard = CardRef & { slot: number; size: 1 | 2 | 3 };
type Inventory = {
  board: PlacedCard[] | null;
  stash: PlacedCard[] | null;
  skills: CardRef[] | null;
  gold: number | null;
  health: number | null;
  level: number | null;
};
export type Moment = {
  responseSeq: number | null;
  snapshot: Inventory | null;
  encounter: CardRef | null;
  offer: CardRef[] | null | undefined;
  chosen: string | null;
};

type CardState = {
  acquired: number;
  owner: string | null;
  section: string | null;
  slot: number | null;
  size: 1 | 2 | 3 | null;
  sampled?: CardRef | PlacedCard;
  tag: CardRef['tag'];
  templateId: string | null;
  type: string | null;
  tier: string | null;
  enchant: string | null;
  price: number | null;
  captured?: boolean;
};

type RunState = {
  /** False after an undecodable message, until a GameStateSync gives a new baseline. */
  known: boolean;
  acquisitions: number;
  playerKnown: boolean;
  inventoryKnown: boolean;
  selectionKnown: boolean;
  originals: Map<string, CardState>;
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
  changed: Set<string>;
  inventory: Map<string, CardState>;
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
    acquisitions: 0,
    playerKnown: false,
    inventoryKnown: false,
    selectionKnown: false,
    originals: new Map(),
    day: null,
    hour: null,
    stateName: null,
    encounterId: null,
    selection: [],
    rerollCost: null,
    rerollsRemaining: null,
    free: false,
    player: new Map(),
    changed: new Set(),
    inventory: new Map(),
    cards: new Map()
  };
}

function card(state: RunState, id: string): CardState {
  state.changed.add(id);
  let found = state.cards.get(id);
  if (found?.captured) {
    found = { ...found, captured: false };
    state.cards.set(id, found);
  }
  if (!found) {
    found = {
      owner: null,
      acquired: 0,
      section: null,
      slot: null,
      size: null,
      tag: null,
      templateId: null,
      type: null,
      tier: null,
      enchant: null,
      price: null
    };
    state.cards.set(id, found);
  }
  return found;
}

export function cardRef(state: Pick<RunState, 'cards'>, id: string): CardRef {
  const c = state.cards.get(id);
  return {
    instanceId: id,
    tag: c?.tag ?? null,
    templateId: c?.templateId ?? null,
    type: c?.type ?? null,
    tier: c?.tier ?? null,
    enchant: c?.enchant ?? null,
    price: c?.price ?? null
  };
}

function sizeOf(value: Translated | undefined) {
  switch (enumName(value)) {
    case 'Small':
      return 1;
    case 'Medium':
      return 2;
    case 'Large':
      return 3;
    default:
      return null;
  }
}

function assignOwner(state: RunState, c: CardState, owner: string | null) {
  if (owner === 'Player' && c.owner !== owner)
    c.acquired = ++state.acquisitions;
  c.owner = owner;
}

function placement(
  state: RunState,
  c: CardState,
  record: Translated,
  owner = 'Owner',
  section = 'Section',
  socket = 'Socket'
) {
  const fields = [owner, section, socket].map((key) => field(record, key));
  if (fields.some((v) => v !== null && enumName(v) === null))
    state.inventoryKnown = false;
  assignOwner(state, c, enumName(fields[0]));
  c.section = enumName(fields[1]);
  const position = fields[2];
  c.slot = position instanceof EnumValue ? Number(position.value) : null;
}

function share<T>(old: T[] | null | undefined, next: T[]) {
  return old?.length === next.length && old.every((v, i) => v === next[i])
    ? old
    : next;
}

type Observation = Pick<RunState, 'cards' | 'inventoryKnown'> &
  Pick<Inventory, 'gold' | 'health' | 'level'>;

// Retain only player cards. Later writes copy a captured card, so a moment can
// validate and sort its board when it is opened, even after a sync or transform.
function observe(state: RunState): Observation {
  let cards = state.inventory;
  for (const id of state.changed) {
    const card = state.cards.get(id);
    if (card?.owner !== 'Player' && !cards.has(id)) continue;
    if (cards === state.inventory) cards = new Map(cards);
    if (card?.owner === 'Player') {
      card.captured = true;
      cards.set(id, card);
    } else cards.delete(id);
  }
  state.inventory = cards;
  state.changed.clear();
  return {
    cards,
    inventoryKnown: state.inventoryKnown,
    gold: state.playerKnown ? (state.player.get('Gold') ?? null) : null,
    health: state.playerKnown ? (state.player.get('Health') ?? null) : null,
    level: state.playerKnown ? (state.player.get('Level') ?? null) : null
  };
}

function sample(state: Observation, previous: Inventory | null): Inventory {
  const board: PlacedCard[] = [],
    stash: PlacedCard[] = [],
    skills: CardRef[] = [];
  const invalid = new Set<string>();
  if (state.inventoryKnown)
    for (const [id, c] of state.cards) {
      if (c.owner !== 'Player') continue;
      if (!c.type) {
        invalid.add('Hand');
        invalid.add('Stash');
        invalid.add('Skill');
        continue;
      }
      if (c.type !== 'Item' && c.type !== 'Skill') continue;
      if (c.type === 'Item') {
        if (!c.section) {
          invalid.add('Hand');
          invalid.add('Stash');
          continue;
        }
        if (
          c.slot === null ||
          c.size === null ||
          c.slot < 0 ||
          c.slot + c.size > 10
        ) {
          invalid.add(c.section);
          continue;
        }
      }
      const old = c.sampled;
      const same =
        old &&
        old.tag === c.tag &&
        old.type === c.type &&
        old.templateId === c.templateId &&
        old.tier === c.tier &&
        old.enchant === c.enchant &&
        old.price === c.price;
      if (c.type === 'Skill') {
        const skill = same ? old : cardRef(state, id);
        c.sampled = skill;
        skills.push(skill);
      } else if (c.slot !== null && c.size !== null) {
        const item =
          same && 'slot' in old && old.slot === c.slot && old.size === c.size
            ? old
            : { ...cardRef(state, id), slot: c.slot, size: c.size };
        c.sampled = item;
        if (c.section === 'Hand') board.push(item);
        if (c.section === 'Stash') stash.push(item);
      }
    }
  function section(
    name: string,
    cards: PlacedCard[],
    prior: PlacedCard[] | null | undefined
  ) {
    cards.sort((a, b) => a.slot - b.slot);
    return state.inventoryKnown &&
      !invalid.has(name) &&
      cards.every(
        (v, i) => i === 0 || cards[i - 1].slot + cards[i - 1].size <= v.slot
      )
      ? share(prior, cards)
      : null;
  }
  return {
    board: section('Hand', board, previous?.board),
    stash: section('Stash', stash, previous?.stash),
    skills:
      state.inventoryKnown && !invalid.has('Skill')
        ? share(
            previous?.skills,
            skills.sort(
              (a, b) =>
                (state.cards.get(a.instanceId)?.acquired ?? 0) -
                (state.cards.get(b.instanceId)?.acquired ?? 0)
            )
          )
        : null,
    gold: state.gold,
    health: state.health,
    level: state.level
  };
}

function offer(state: RunState) {
  return state.selectionKnown && state.inventoryKnown
    ? state.selection.map((id) => cardRef(state, id))
    : null;
}

/** CurrentEncounterId holds the encounter's TemplateId, not an instance id. */
function encounterRef(state: RunState): CardRef | null {
  const templateId = state.encounterId?.toLowerCase();
  if (!templateId) return null;
  for (const [id, c] of state.cards)
    if (c.templateId === templateId) return cardRef(state, id);
  return {
    instanceId: '',
    tag: null,
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
  const selection = field(current, 'SelectionSet');
  state.selectionKnown =
    Array.isArray(selection) && selection.every((id) => typeof id === 'string');
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
  state.playerKnown = true;
  applyRun(state, field(data, 'Run'));
  applyRunState(state, field(data, 'CurrentState'));
  for (const [name, value] of enumKeyed(
    field(field(data, 'Player'), 'Attributes')
  )) {
    const n = int(value);
    if (n !== null) state.player.set(name, n);
  }
  state.inventoryKnown = Array.isArray(field(data, 'Cards'));
  for (const snapshot of list(field(data, 'Cards'))) {
    const id = text(field(snapshot, 'InstanceId'));
    if (id === null) {
      state.inventoryKnown = false;
      continue;
    }
    const c = card(state, id);
    placement(state, c, snapshot);
    c.size = sizeOf(field(snapshot, 'Size'));
    if (
      !enumName(field(snapshot, 'Type')) ||
      !text(field(snapshot, 'TemplateId'))
    )
      state.inventoryKnown = false;
    c.templateId = text(field(snapshot, 'TemplateId'))?.toLowerCase() ?? null;
    updateTags(c, snapshot);
    c.type = enumName(field(snapshot, 'Type'));
    c.tier = enumName(field(snapshot, 'Tier'));
    c.enchant = enumName(field(snapshot, 'Enchantment'));
    for (const [name, value] of enumKeyed(field(snapshot, 'Attributes'))) {
      const n = int(value);
      if (name === 'BuyPrice' && n !== null) c.price = n;
    }
  }
}

function updateTags(instance: CardState, snapshot: Translated) {
  const tags = field(snapshot, 'Tags');
  if (!Array.isArray(tags)) return;
  const names = new Set(tags.map(enumName));
  instance.tag = names.has('Merchant')
    ? 'Merchant'
    : names.has('Event')
      ? 'Event'
      : null;
}

function updateTemplate(c: CardState, snapshot: Translated) {
  const templateId = text(field(snapshot, 'TemplateId'))?.toLowerCase();
  if (templateId && templateId !== c.templateId) {
    c.templateId = templateId;
    c.enchant = null;
    c.size = null;
    c.tier = null;
    c.price = null;
    c.tag = null;
  }
  c.type = enumName(field(snapshot, 'Type')) ?? c.type;
}

function applyGameSim(
  state: RunState,
  data: Translated | undefined,
  evidence: Evidence
) {
  const events = field(data, 'Events');
  if (!Array.isArray(events)) state.inventoryKnown = false;
  for (const event of list(events)) {
    if (!(event instanceof TObject)) {
      state.inventoryKnown = false;
      continue;
    }
    const id = text(field(event, 'InstanceId'));
    switch (shortName(event.type)) {
      case 'GameSimEventCardDealt':
      case 'GameSimEventCardSpawned':
        if (id !== null) {
          const c = card(state, id);
          updateTemplate(c, event);
          if (shortName(event.type) === 'GameSimEventCardSpawned')
            placement(state, c, event, 'CombatantId');
        }
        evidence.dealt = true;
        break;
      case 'GameSimEventCardTransformReverted': {
        const original = field(event, 'OriginalCard');
        const originalId = text(field(original, 'InstanceId'));
        for (const key of list(field(event, 'TransformedCardInstanceIds')))
          if (typeof key === 'string') {
            state.changed.add(key);
            state.cards.delete(key);
          }
        if (original && originalId) {
          const saved = state.originals.get(originalId);
          if (saved) state.cards.set(originalId, saved);
          state.originals.delete(originalId);
          const c = card(state, originalId);
          updateTemplate(c, original);
          placement(state, c, original, 'CombatantId');
        } else state.inventoryKnown = false;
        break;
      }
      case 'GameSimEventCardTransformed': {
        const original = text(field(event, 'OriginalInstanceId'));
        if (original) {
          const c = state.cards.get(original);
          if (c) state.originals.set(original, c);
          state.changed.add(original);
          state.cards.delete(original);
        }
        for (const t of list(field(event, 'TransformedCards'))) {
          const tid = text(field(t, 'InstanceId'));
          if (tid === null) continue;
          const c = card(state, tid);
          updateTemplate(c, t);
          placement(state, c, t, 'CombatantId');
        }
        break;
      }
      case 'GameSimEventCardDisposed':
        if (id) card(state, id).owner = null;
        break;
      case 'GameSimEventCardFused': {
        const selected = text(field(event, 'SelectedInstanceId'));
        const into = text(field(event, 'FusedInstanceId'));
        const tier = enumName(field(event, 'NewTier'));
        if (selected !== null && into !== null) {
          if (selected !== into) card(state, selected).owner = null;
          evidence.fused.set(selected, { into, tier });
          const survivor = card(state, into);
          if (tier) survivor.tier = tier;
          survivor.enchant =
            enumName(field(event, 'EnchantmentType')) ?? survivor.enchant;
        }
        break;
      }
      case 'GameSimEventCardPurchased':
        if (id && enumName(field(event, 'CombatantId')))
          placement(
            state,
            card(state, id),
            event,
            'CombatantId',
            'Section',
            'LeftSocketId'
          );
        if (id !== null)
          evidence.purchased.set(id, int(field(event, 'BuyPrice')) ?? 0);
        break;
      case 'GameSimEventCardSold':
        if (id) card(state, id).owner = null;
        if (id !== null)
          evidence.sold.set(id, int(field(event, 'SellPrice')) ?? 0);
        break;
      case 'GameSimEventCardMoved':
        if (id)
          placement(
            state,
            card(state, id),
            event,
            'Owner',
            'ToInventory',
            'ToSocket'
          );
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
        if (id !== null) {
          evidence.equipped.add(id);
          const skill = card(state, id);
          skill.type = 'Skill';
          const owner = enumName(field(event, 'Owner'));
          if (!owner) state.inventoryKnown = false;
          assignOwner(state, skill, owner);
        }
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
      const place = field(update, 'Placement');
      if (place != null) placement(state, c, place);
      c.size = sizeOf(field(update, 'Size')) ?? c.size;
      updateTags(c, update);
      c.enchant = enumName(field(update, 'Enchantment')) ?? c.enchant;
      c.tier = enumName(field(update, 'Tier')) ?? c.tier;
      for (const [name, delta] of enumKeyed(field(update, 'Attributes'))) {
        if (name !== 'BuyPrice') continue;
        // An AttributeDelta of type Delete removes the attribute, e.g. a price.
        if (enumName(field(delta, 'DeltaType')) === 'Delete') c.price = null;
        else {
          const n = int(field(delta, 'Value'));
          if (n !== null) c.price = n;
        }
      }
    }
  applyRunState(state, field(data, 'CurrentState'));
}

type Pending = {
  moment: Moment;
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
        entry.amount = evidence.purchased.get(bought) ?? null;
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
        entry.amount = evidence.sold.get(id) ?? null;
      }
      break;
    case 'SendMoveItem':
      if (id !== null && evidence.moved.has(id)) {
        done = true;
        entry.target = evidence.moved.get(id) ?? null;
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

export type Context = Omit<Placed, 'frames'> & {
  kind: 'context';
  state: string | null;
  encounter: CardRef | null;
  sync: boolean;
};

/**
 * Folds the stream in Seq order into decision entries. Every frame is referenced by exactly
 * the entries it feeds; a frame no rule reads still appears (an `update`), so every frame is
 * reachable from the path.
 */
export async function projectDecisions(
  stream: MessageStream,
  battles: readonly BattleMarker[],
  yieldEvery: number = CHUNK
) {
  const moments = new Map<number, Moment>();
  const openings = new Map<string, Moment>();

  let previous: Inventory | null = null;
  function capture(moment: Moment, observation: Observation | null) {
    Object.defineProperty(moment, 'snapshot', {
      configurable: true,
      enumerable: true,
      get() {
        const snapshot = observation ? sample(observation, previous) : null;
        if (snapshot) previous = snapshot;
        Object.defineProperty(moment, 'snapshot', { value: snapshot });
        return snapshot;
      }
    });
  }
  const contexts: Context[] = [];
  const state = newState();
  const entries: DecisionEntry[] = [];
  const pending = new Map<number, Pending>();
  let selected: CardRef | null = null;
  /** Seqs of the messages that entered a combat state, by state name. */
  const combats: Record<
    'PVPCombat' | 'Combat',
    (Context & { opening: Moment })[]
  > = {
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
      if (frame.command === 'ReestablishSession') {
        state.inventoryKnown = false;
        state.selectionKnown = false;
        state.playerKnown = false;
      }
      const moment: Moment = {
        responseSeq: null,
        snapshot: null,
        encounter:
          state.playerKnown && state.selectionKnown
            ? (selected ?? encounterRef(state))
            : null,
        offer: ['chose', 'bought', 'skill', 'committed'].includes(entry.verb)
          ? offer(state)
          : undefined,
        chosen: null
      };
      moments.set(frame.seq, moment);
      entries.push(entry);
      pending.set(frame.seq, {
        moment,
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
    const data = decoded?.ok ? field(decoded.value, 'Data') : undefined;
    const stateMessage =
      decoded?.ok &&
      (decoded.type === 'NetMessageGameSim' ||
        decoded.type === 'NetMessageGameStateSync');
    const partial =
      stateMessage &&
      (!(data instanceof TObject) ||
        STATE_PARTS.some((part) => data.fields[part] instanceof RawValue));
    if (!decoded?.ok || partial) {
      const sync =
        decoded?.ok &&
        decoded.type === 'NetMessageGameStateSync' &&
        data instanceof TObject;
      if (sync) {
        applySync(state, data);
        selected = null;
        if (decoded.flags.some((f) => f.path.includes('.Cards')))
          state.inventoryKnown = false;
      } else {
        state.inventoryKnown = false;
        state.playerKnown = false;
        state.selectionKnown = false;
      }
      state.known = false;
      entries.push({
        kind: 'undecoded',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        frames: [frame.seq],
        error: decoded?.ok
          ? decoded.flags.map((f) => f.path + ': ' + f.reason).join('; ')
          : (decoded?.error ?? '')
      });
      if (owner) {
        owner.moment.responseSeq = frame.seq;
        capture(owner.moment, sync ? observe(state) : null);
        owner.gold = null;
        owner.health = null;
        settle(owner, state);
      }
      continue;
    }
    const evidence = owner?.evidence ?? emptyEvidence();
    const before = {
      day: state.day,
      hour: state.hour,
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
    if (decoded.flags.some((f) => /\.(Cards|Events)(?:\.|\[)/.test(f.path)))
      state.inventoryKnown = false;
    const baseline = decoded.type === 'NetMessageGameStateSync';
    if (baseline || before.encounterId !== state.encounterId) selected = null;
    const chosen = owner?.entry.card;
    if (
      chosen &&
      owner?.entry.verb === 'chose' &&
      evidence.purchased.has(chosen.instanceId) &&
      chosen.templateId === state.encounterId?.toLowerCase()
    )
      selected = chosen;
    const encounter = selected ?? encounterRef(state);
    if (
      state.known &&
      (baseline ||
        before.day !== state.day ||
        before.hour !== state.hour ||
        before.encounterId !== state.encounterId ||
        before.stateName !== state.stateName)
    ) {
      const context: Context = {
        kind: 'context',
        order: frame.seq,
        day: state.day,
        hour: state.hour,
        state: state.stateName,
        encounter,
        sync: baseline
      };
      contexts.push(context);
      if (
        !baseline &&
        before.stateName !== state.stateName &&
        (state.stateName === 'PVPCombat' || state.stateName === 'Combat')
      ) {
        const opening: Moment = {
          responseSeq: frame.seq,
          snapshot: null,
          encounter,
          offer: undefined,
          chosen: null
        };
        capture(opening, observe(state));
        combats[state.stateName].push({ ...context, opening });
      }
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
        encounter,
        cards: state.selection.map((id) => cardRef(state, id)),
        rerollCost: state.rerollCost,
        rerollsRemaining: state.rerollsRemaining,
        free: state.free
      });
      read = true;
    }
    if (!owner && !baseline) {
      const gold = state.player.get('Gold') ?? null;
      const health = state.player.get('Health') ?? null;
      const known = before.known && state.known;
      if (
        !read ||
        (known && (before.gold !== gold || before.health !== health))
      )
        entries.push({
          kind: 'update',
          order: frame.seq,
          day: state.day,
          hour: state.hour,
          frames: [frame.seq],
          gold:
            known && gold !== null && before.gold !== null
              ? gold - before.gold
              : null,
          health:
            known && health !== null && before.health !== null
              ? health - before.health
              : null
        });
    }
    if (owner) {
      attribute(owner, before, state, baseline);
      settle(owner, state);
      const moment = owner.moment;
      moment.responseSeq = frame.seq;
      capture(moment, observe(state));
      if (owner.entry.verb === 'rerolled') moment.offer = offer(state);
      const pick = argId(owner.args);
      if (
        owner.entry.outcome === 'done' &&
        pick &&
        moment.offer?.some((c) => c.instanceId === pick)
      ) {
        moment.chosen = pick;
        if (owner.entry.verb === 'chose' && selected?.instanceId === pick)
          moment.encounter = selected;
      }
    }
  }

  for (const battle of battles) {
    // A Battle sits right after the message that entered its combat.
    const anchor =
      battle.seq === null
        ? (stream.frames.find((f) => f.timestampMs >= battle.recordedAtMs)
            ?.seq ?? Number.MAX_SAFE_INTEGER)
        : battle.seq;
    const candidates = combats[battle.pvp ? 'PVPCombat' : 'Combat'];
    const matches = candidates.filter(
      (c) =>
        c.order < anchor &&
        (battle.day === null || c.day === battle.day) &&
        (battle.hour === null || c.hour === battle.hour) &&
        (!battle.encounterId ||
          !c.encounter ||
          c.encounter.templateId === battle.encounterId.toLowerCase())
    );
    const entered = matches.length === 1 ? matches[0] : null;
    if (entered) {
      candidates.splice(candidates.indexOf(entered), 1);
      openings.set(battle.id, entered.opening);
    }
    entries.push({
      kind: 'battle',
      order: (entered?.order ?? anchor) + 0.25,
      day: battle.day,
      hour: battle.hour,
      frames: [],
      battleId: battle.id,
      pvp: battle.pvp,
      encounterId: battle.encounterId ?? null
    });
  }
  return {
    moments,
    openings,
    entries: entries.sort((a, b) => a.order - b.order),
    contexts
  };
}
