import schemaJson from './game-message-schema.json';
import type { StreamFrame } from './stream';
import type { BattleMarker } from './project';
import { projectDecisions, type Moment, type DecisionEntry } from './project';
import type { GameMessageSchema } from './schema';
import { MessageStream } from './stream';

import { projectVisits, type Visit } from './visits';

export type DecisionPath = {
  moments: Map<number, Moment>;
  openings: Map<string, Moment>;
  stream: MessageStream;
  entries: DecisionEntry[];
  visits: Visit[];
};

/** The decision path of a Run with a Run Message Stream; loaded on demand with the schema. */
export async function buildDecisionPath(detail: {
  stream: readonly StreamFrame[] | null;
  platform: string | null;
  markers: readonly BattleMarker[];
}): Promise<DecisionPath | null> {
  if (!detail.stream) return null;
  const stream = new MessageStream(
    detail.stream,
    schemaJson as GameMessageSchema,
    detail.platform
  );
  const { entries, contexts, moments, openings } = await projectDecisions(
    stream,
    detail.markers
  );
  return {
    moments,
    openings,
    stream,
    entries,
    visits: projectVisits(entries, contexts)
  };
}
