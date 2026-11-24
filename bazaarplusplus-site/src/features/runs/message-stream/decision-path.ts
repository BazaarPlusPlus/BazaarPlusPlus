import schemaJson from './game-message-schema.json';
import type { RunDetail } from '../run-payload';
import {
  groupByHour,
  projectDecisions,
  type DecisionEntry,
  type HourGroup
} from './project';
import type { GameMessageSchema } from './schema';
import { MessageStream } from './stream';

export type DecisionPath = {
  stream: MessageStream;
  entries: DecisionEntry[];
  hours: HourGroup[];
};

/** The decision path of a Run with a Run Message Stream; loaded on demand with the schema. */
export async function buildDecisionPath(
  detail: RunDetail
): Promise<DecisionPath | null> {
  if (!detail.stream) return null;
  const stream = new MessageStream(
    detail.stream,
    schemaJson as GameMessageSchema,
    detail.platform
  );
  const entries = await projectDecisions(
    stream,
    detail.battles.map((battle) => ({
      id: battle.id,
      // A PvE fight known only from its event carries that event's Seq in its id.
      seq:
        detail.battleSeqs.get(battle.id) ??
        (battle.source === 'pve-event' ? Number(battle.id.slice(6)) : null),
      recordedAtMs: battle.recordedAtMs,
      day: battle.day,
      hour: battle.hour,
      pvp: battle.pvp
    }))
  );
  return { stream, entries, hours: groupByHour(entries) };
}
