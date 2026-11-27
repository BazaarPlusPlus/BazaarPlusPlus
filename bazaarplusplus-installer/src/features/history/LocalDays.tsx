import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode
} from 'react';
import {
  DayCard,
  DecisionReplay,
  StreamFrames,
  fillTerm,
  useCardCatalog,
  type ReplayBattle
} from '@bazaarplusplus/ui/game';
import {
  combatFacts,
  decisionDays,
  type EventBattle,
  type MessageStream,
  type ReplayVisit
} from '@bpp/runs/decisions';
import { useI18n } from '../../i18n/LocaleProvider';
import type { HistoryBattleRow, HistoryRunDetail } from '../../types/backend';

export function LocalDays({
  detail,
  empty,
  renderBattles
}: {
  detail: HistoryRunDetail;
  empty: ReactNode;
  renderBattles: (rows: (HistoryBattleRow | EventBattle)[]) => ReactNode;
}) {
  const { t, locale } = useI18n();
  const { terms } = useCardCatalog();
  const input = useMemo(() => {
    const captured = detail.battles.map((b) => ({
      id: b.battle_id,
      recordedAtMs: b.recorded_at_ms,
      day: b.day,
      hour: b.hour,
      encounterId: b.encounter_id,
      pvp: b.combat_kind.trim().toLowerCase().startsWith('pvp')
    }));
    const section = detail.decision_path;
    const { markers, eventBattles } = combatFacts(
      captured,
      section.state === 'available' ? section.events : []
    );
    return {
      section,
      eventBattles,
      markers
    };
  }, [detail]);
  const [result, setResult] = useState<{
    input: typeof input;
    replay: ReplayVisit[] | null;
    stream?: MessageStream;
  }>();
  useEffect(() => {
    const section = input.section;
    if (section.state !== 'available') return;
    let active = true;
    void import('@bpp/runs/decision-path')
      .then((module) =>
        module.buildDecisionPath({
          platform: null,
          markers: input.markers,
          stream: section.frames.map((f) => ({
            seq: f.seq,
            timestampMs: f.timestamp_ms,
            command: f.command,
            commandArgsJson: f.command_args_json,
            respondsToSeq: f.responds_to_seq,
            message:
              f.message_base64 === null
                ? null
                : Uint8Array.from(atob(f.message_base64), (c) =>
                    c.charCodeAt(0)
                  )
          }))
        })
      )
      .then((path) => {
        if (active)
          setResult({
            input,
            replay: path?.replay ?? [],
            stream: path?.stream
          });
      })
      .catch(() => {
        if (active) setResult({ input, replay: null });
      });
    return () => {
      active = false;
    };
  }, [input]);
  const replay = result?.input === input ? result.replay : undefined;
  const fights = useMemo(
    () =>
      [...detail.battles, ...input.eventBattles].sort(
        (a, b) => timeOf(a) - timeOf(b)
      ),
    [detail, input]
  );
  const days = decisionDays(fights, (b) => b.day);
  const cards = useMemo(
    () =>
      new Map(
        detail.battles.flatMap((b) =>
          [
            ...(b.player_board ?? []),
            ...(b.player_skills ?? []),
            ...(b.opponent_board ?? []),
            ...(b.opponent_skills ?? [])
          ].map((c) => [c.template_id.toLowerCase(), c] as const)
        )
      ),
    [detail]
  );
  // The Battle snapshots' own names, for a card the catalog does not have.
  const names = useCallback(
    (id: string) => {
      const known = cards.get(id.toLowerCase());
      return known ? (locale === 'zh' && known.name_zh) || known.name : null;
    },
    [cards, locale]
  );
  const battles = useMemo(
    () =>
      new Map<string, ReplayBattle>(
        fights.map((b) =>
          'battle_id' in b
            ? [
                b.battle_id,
                {
                  encounterId: b.encounter_id,
                  opponentHero: b.opponent_hero,
                  outcome:
                    b.result === 'win' || b.result === 'loss' ? b.result : null
                }
              ]
            : [
                b.id,
                {
                  encounterId: b.encounterId,
                  opponentHero: null,
                  outcome: b.outcome
                }
              ]
        )
      ),
    [fights]
  );
  const failed = replay === null || input.section.state === 'unreadable';
  return (
    <>
      {failed && <p role="status">{terms.replay.error}</p>}
      {!days.length && empty}
      <ol className="flex flex-col gap-3">
        {days.map(({ day, battles: rows }) => (
          <DayCard
            key={day ?? 'unknown'}
            title={
              day === null
                ? terms.replay.unknownDay
                : fillTerm(terms.day, { n: day })
            }
            battles={renderBattles(rows)}
          />
        ))}
      </ol>
      {input.section.state === 'available' && !failed && (
        <section className="bpp-panel flex flex-col gap-3 p-4">
          <h2 className="m-0 text-base">{terms.replay.decisions}</h2>
          {replay?.length ? (
            <DecisionReplay replay={replay} battles={battles} names={names} />
          ) : (
            <p>
              {replay === undefined
                ? t('runDetailLoading')
                : terms.replay.noVisits}
            </p>
          )}
        </section>
      )}
      {result?.input === input && result.stream && (
        <StreamFrames stream={result.stream} />
      )}
    </>
  );
}

function timeOf(b: HistoryBattleRow | EventBattle) {
  return 'battle_id' in b ? b.recorded_at_ms : b.recordedAtMs;
}
