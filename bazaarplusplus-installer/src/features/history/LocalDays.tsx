import { useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  DecisionExplorer,
  MomentPanel,
  useDecisionSelection,
  Badge,
  EnchantLabel,
  FrameInspector,
  type GameCard,
  DayCard,
  DecisionVisitRow,
  cardTiers,
  enchantments
} from '@bpp/ui';
import {
  dayMoments,
  presentMoment,
  type MomentIndex,
  combatFacts,
  decisionDays,
  type EventBattle,
  presentVisit,
  type CardRef,
  type MessageStream,
  type Visit
} from '@bpp/runs/decisions';
import { useI18n } from '../../i18n/LocaleProvider';
import type { HistoryBattleRow, HistoryRunDetail } from '../../types/backend';

export function LocalDays({
  detail,
  art,
  empty,
  renderBattles
}: {
  detail: HistoryRunDetail;
  art: Record<string, string | undefined>;
  empty: ReactNode;
  renderBattles: (rows: (HistoryBattleRow | EventBattle)[]) => ReactNode;
}) {
  const { t, locale } = useI18n();
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
    visits: Visit[] | null;
    index?: MomentIndex;
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
            visits: path?.visits ?? [],
            index: path
              ? { moments: path.moments, openings: path.openings }
              : undefined,
            stream: path?.stream
          });
      })
      .catch(() => {
        if (active) setResult({ input, visits: null });
      });
    return () => {
      active = false;
    };
  }, [input]);
  const visits = result?.input === input ? result.visits : undefined;
  const fights = [...detail.battles, ...input.eventBattles].sort(
    (a, b) => timeOf(a) - timeOf(b)
  );
  const days = decisionDays(fights, (b) => b.day, visits ?? []);
  const cards = new Map(
    detail.battles.flatMap((b) =>
      [
        ...(b.player_board ?? []),
        ...(b.player_skills ?? []),
        ...(b.opponent_board ?? []),
        ...(b.opponent_skills ?? [])
      ].map((c) => [c.template_id.toLowerCase(), c] as const)
    )
  );
  function display(c: CardRef) {
    const id = c.templateId ?? c.instanceId;
    const known = cards.get(id.toLowerCase());
    const file = art[id.toLowerCase()];
    const tier = cardTiers.find((v) => v === c.tier) ?? null;
    const enchant = enchantments.find((v) => v === c.enchant) ?? null;
    return {
      name: known
        ? (locale === 'zh' && known.name_zh) || known.name
        : c.type === 'PvpEncounter'
          ? t('path_pvp')
          : id.slice(0, 8),
      art:
        file && /^[0-9a-f]{8,64}\.webp$/.test(file)
          ? 'https://cdn.bazaarplusplus.com/card-art/' + file
          : null,
      tier,
      tierLabel: tier ? t(`rank${tier}`) : '',
      enchant,
      enchantLabel: enchant ? t(`enchant${enchant}`) : ''
    };
  }
  const failed = visits === null || input.section.state === 'unreadable';
  return (
    <>
      {failed && <p role="status">{t('path_error')}</p>}
      {!days.length &&
        (input.section.state !== 'available' || visits !== undefined ? (
          empty
        ) : (
          <p>{t('runDetailLoading')}</p>
        ))}
      <ol className="flex flex-col gap-3">
        {days.map(({ day, battles: rows, visits: decisions }) => {
          const moments = dayMoments(decisions, result?.index);
          return (
            <DayCard
              key={day ?? 'unknown'}
              title={
                day === null ? t('path_unknownDay') : t('path_day', { n: day })
              }
              battles={rows.length ? renderBattles(rows) : null}
              decisionsLabel={t('path_decisions')}
              decisions={
                input.section.state === 'available' &&
                !failed &&
                (decisions.length ? (
                  <DecisionExplorer
                    ids={[...moments.keys()]}
                    previous={t('path_previous')}
                    next={t('path_next')}
                    render={(seq) => {
                      const row = moments.get(seq);
                      if (!row) return null;
                      return (
                        <MomentPanel
                          view={presentMoment(row.moment, display)}
                          reroll={row.verb === 'rerolled'}
                          title={[
                            row.visit.day !== null &&
                              t('path_day', { n: row.visit.day }),
                            row.visit.hour !== null &&
                              t('path_hour', { n: row.visit.hour }),
                            t(`path_${row.verb}`)
                          ]
                            .filter(Boolean)
                            .join(' · ')}
                          labels={{
                            board: t('path_board'),
                            stash: t('path_stash'),
                            skills: t('battleSkills'),
                            unknown: t('path_unknown'),
                            empty: t('path_empty'),
                            pickedFrom: t('path_pickedFrom'),
                            chosen: t('path_chosen'),
                            goldTotal: t('path_goldTotal'),
                            healthTotal: t('path_healthTotal'),
                            level: t('path_level'),
                            resultOffer: t('path_resultOffer')
                          }}
                        />
                      );
                    }}
                  >
                    {decisions.map((v) => (
                      <LocalVisit
                        key={v.battleId ?? v.order}
                        opening={
                          result?.index?.openings.get(v.battleId ?? '')
                            ?.responseSeq ?? undefined
                        }
                        v={v}
                        display={display}
                        fight={rows.find(
                          (b) =>
                            ('battle_id' in b ? b.battle_id : b.id) ===
                            v.battleId
                        )}
                      />
                    ))}
                  </DecisionExplorer>
                ) : (
                  <p>
                    {t(
                      visits === undefined
                        ? 'runDetailLoading'
                        : 'path_noVisits'
                    )}
                  </p>
                ))
              }
            />
          );
        })}
      </ol>
      {result?.input === input && result.stream && (
        <LocalFrames stream={result.stream} />
      )}
    </>
  );
}

function LocalFrames({ stream }: { stream: MessageStream }) {
  const { t } = useI18n();
  return (
    <FrameInspector
      frames={stream.frames}
      read={(frame) => stream.readableFrame(frame)}
      exportFrames={() => stream.exportFrames()}
      labels={{
        rawFrames: t('path_rawFrames'),
        frame: t('path_frame'),
        downloadBytes: t('path_downloadBytes'),
        downloadAll: t('path_downloadAll')
      }}
      download={(name, parts) => {
        const url = URL.createObjectURL(
          new Blob(parts as BlobPart[], {
            type:
              typeof parts[0] === 'string'
                ? 'application/json'
                : 'application/octet-stream'
          })
        );
        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        link.click();
        URL.revokeObjectURL(url);
      }}
    />
  );
}

function timeOf(b: HistoryBattleRow | EventBattle) {
  return 'battle_id' in b ? b.recorded_at_ms : b.recordedAtMs;
}
function LocalVisit({
  opening,
  v,
  display,
  fight
}: {
  opening?: number;
  v: Visit;
  display: (card: CardRef) => GameCard;
  fight?: HistoryBattleRow | EventBattle;
}) {
  const { t } = useI18n();
  const facts =
    fight &&
    ('battle_id' in fight
      ? {
          encounterId: fight.encounter_id,
          opponentHero: fight.opponent_hero,
          outcome: fight.result
        }
      : {
          encounterId: fight.encounterId,
          opponentHero: null,
          outcome: fight.outcome
        });
  const outcome = facts?.outcome;
  const selection = useDecisionSelection();
  const view = presentVisit(
    v,
    facts,
    display,
    (key, params) => t(`path_${key}`, params),
    selection?.active,
    opening
  );
  return (
    <DecisionVisitRow
      view={view}
      outcome={
        (outcome === 'win' || outcome === 'loss') && (
          <Badge tone={outcome === 'win' ? 'ok' : 'bad'}>
            {t(outcome === 'win' ? 'battleResultWin' : 'battleResultLoss')}
          </Badge>
        )
      }
      renderResult={(value) => {
        const enchant = enchantments.find((e) => e === value);
        const tier = cardTiers.find((e) => e === value);
        return enchant ? (
          <EnchantLabel enchant={enchant} label={t(`enchant${enchant}`)} />
        ) : tier ? (
          t(`rank${tier}`)
        ) : (
          value
        );
      }}
    />
  );
}
