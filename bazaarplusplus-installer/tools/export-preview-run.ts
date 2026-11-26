// Prints src/api/preview-run.json: the site's fixed real Run as the Run Detail Browser
// Preview shows. Run from bazaarplusplus-installer:
// `bun tools/export-preview-run.ts > src/api/preview-run.json && bun x vp fmt src/api/preview-run.json`.
import { decodeRunPayload } from '../../bazaarplusplus-site/src/features/runs/run-payload.ts';
import type {
  HistoryBattleCard,
  HistoryRunDetail
} from '../src/types/generated/commands.ts';

const source = '../bazaarplusplus-site/test/fixtures/run-stream.payload.b64';
// The site's FIXTURE_RUN_ID, the Run id inside the fixture.
const FIXTURE_RUN_ID = '00000000-0000-4000-8000-000000000001';
const encoded = await (
  await fetch(new URL('../' + source, import.meta.url))
).text();
const run = await decodeRunPayload(
  Uint8Array.from(atob(encoded.trim()), (c) => c.charCodeAt(0)),
  FIXTURE_RUN_ID
);
if (!run.stream) throw new Error('The fixture Run has no Run Message Stream');

type RunCards = (typeof run.battles)[number]['hand'];
const cards = (set: RunCards): HistoryBattleCard[] | null =>
  set.status === 'missing'
    ? null
    : set.cards.map((c) => ({
        instance_id: c.instanceId,
        template_id: c.templateId,
        name: c.name ?? c.templateId,
        name_zh: null,
        size: c.size,
        slot: c.slot,
        tier: c.tier,
        enchant: c.enchant
      }));

const captured = run.battles.filter((b) => b.source === 'battle');
const seqs = new Map(run.markers.map((m) => [m.id, m.seq]));
const events = [
  ...captured.flatMap((b) => {
    const seq = seqs.get(b.id);
    return b.pvp && seq != null
      ? [
          {
            seq,
            timestamp_ms: b.recordedAtMs,
            kind: 'pvp_combat_recorded',
            body: JSON.stringify({ battle_id: b.id })
          }
        ]
      : [];
  }),
  ...run.battles
    .filter((b) => b.source === 'pve-event')
    .map((b) => ({
      seq: Number(b.id.slice('event-'.length)),
      timestamp_ms: b.recordedAtMs,
      kind: 'pve_combat_recorded',
      body: JSON.stringify({
        day: b.day,
        hour: b.hour,
        encounter_id: b.encounterId,
        ...(b.outcome === 'win' ? { winner: 'Player' } : {}),
        ...(b.outcome === 'loss' ? { loser: 'Player' } : {})
      })
    }))
].sort((a, b) => a.seq - b.seq);

const last = captured.at(-1);
const detail: HistoryRunDetail = {
  run: {
    run_id: run.runId,
    hero: run.hero ?? 'Vanessa',
    game_mode: 'Ranked',
    started_at_utc: new Date(run.stream[0].timestampMs).toISOString(),
    ended_at_utc: new Date(
      run.stream[run.stream.length - 1].timestampMs
    ).toISOString(),
    status: 'completed',
    result: 'completed',
    victories: captured.filter((b) => b.pvp && b.outcome === 'win').length,
    losses: captured.filter((b) => b.pvp && b.outcome === 'loss').length,
    final_day: last?.day ?? null,
    final_player_rank: null,
    final_player_rating: null,
    final_player_rating_delta: null,
    screenshot_id: null,
    player_name: null
  },
  battles: captured.map((b) => ({
    recorded_at_ms: b.recordedAtMs,
    encounter_id: b.encounterId,
    combat_kind: b.pvp ? 'PvpCombat' : 'PveCombat',
    battle_id: b.id,
    day: b.day,
    hour: b.hour,
    result: b.outcome ?? 'unknown',
    opponent_hero: b.opponentHero,
    opponent_name: null,
    opponent_rank: null,
    opponent_rating: null,
    player_board: cards(b.hand),
    player_skills: cards(b.skills),
    opponent_board: null,
    opponent_skills: null,
    video: null
  })),
  decision_path: {
    state: 'available',
    frames: run.stream.map((f) => ({
      seq: f.seq,
      timestamp_ms: f.timestampMs,
      kind: f.message ? 'stream_message' : 'stream_command',
      message_base64: f.message
        ? btoa(String.fromCharCode(...f.message))
        : null,
      command: f.command,
      command_args_json: f.commandArgsJson,
      responds_to_seq: f.respondsToSeq
    })),
    events
  }
};
console.log(JSON.stringify(detail));
