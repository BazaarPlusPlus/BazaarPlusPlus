import type { CardSize } from './types';

/** A card reference recorded during a Run replay step or offer. */
export type ReplayCardRef = {
  size: CardSize | null;
  tag: 'Merchant' | 'Event' | null;
  instanceId: string;
  templateId: string | null;
  type: string | null;
  tier: string | null;
  enchant: string | null;
  price: number | null;
};

export type ReplayPlacedCard = ReplayCardRef & {
  slot: number;
  size: CardSize;
};

export type ReplaySnapshot = {
  board: readonly ReplayPlacedCard[] | null;
  stash: readonly ReplayPlacedCard[] | null;
  skills: readonly ReplayCardRef[] | null;
  gold: number | null;
  health: number | null;
  level: number | null;
};

export type ReplayMoment = {
  responseSeq: number | null;
  snapshot: ReplaySnapshot | null;
  encounter: ReplayCardRef | null;
  offer: readonly ReplayCardRef[] | null | undefined;
  chosen: string | null;
};

export type ReplayVisitKind =
  | 'shop'
  | 'event'
  | 'monster'
  | 'pvp'
  | 'pedestal'
  | 'encounter'
  | 'activity'
  | 'levelUp'
  | 'loot';

export type ReplayVerb =
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

export type ReplayStep = {
  seq: number;
  verb: ReplayVerb | ReplayVisitKind;
  count: number;
  card: ReplayCardRef | null;
  result: string | null;
  amount: number | null;
  status: 'attempted' | 'noResponse' | null;
  moment: ReplayMoment | null;
};

export type ReplayVisitMeta = {
  order: number;
  day: number | null;
  hour: number | null;
  kind: ReplayVisitKind;
  encounter: ReplayCardRef | null;
  battleId: string | null;
  gold: number | null;
  health: number | null;
  gap: boolean;
};

export type ReplayVisit = {
  visit: ReplayVisitMeta;
  steps: readonly ReplayStep[];
};

export type StreamFrameLike = {
  seq: number;
  message: Uint8Array | null;
};

export type MessageStreamLike<F extends StreamFrameLike = StreamFrameLike> = {
  readonly frames: readonly F[];
  readableFrame(frame: F): string;
  exportFrames(): Promise<string[]>;
};
