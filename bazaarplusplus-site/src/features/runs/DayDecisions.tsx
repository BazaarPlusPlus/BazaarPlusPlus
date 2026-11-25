import { useState } from 'react';
import {
  Badge,
  Button,
  CardChip,
  EnchantLabel,
  VisitRow,
  enchantments
} from '@bpp/ui';

import type { Locale } from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import { heroName } from '../account/account-ui';
import type { RunBattle } from './run-payload';
import type { Visit } from './message-stream/visits';
import type { CardRef } from './message-stream/project';
import { gameCard } from './game-card';
import { useOpenCard } from './CardDetails';
import type { MessageStream } from './message-stream/stream';

type ArtLookup = (templateId: string) => string | null;

/** A card's name by TemplateId, or null when none is known. */
type Names = (templateId: string) => string | null;

function cardName(card: CardRef, names: Names): string | null {
  return card.templateId ? names(card.templateId) : null;
}

/**
 * A card no name is known for: the PvP encounter (GameData has no title for it) reads PvP;
 * any other shows the start of its TemplateId, and the title has all of it.
 */
function shortId(card: CardRef, locale: Locale): string {
  if (card.type === 'PvpEncounter') return getSiteCopy(locale).account.pvp;
  return card.templateId?.slice(0, 8) ?? card.instanceId;
}

function pathCard(
  card: CardRef,
  art: ArtLookup,
  names: Names,
  locale: Locale,
  open: ReturnType<typeof useOpenCard>
) {
  return gameCard(
    card,
    cardName(card, names) ?? shortId(card, locale),
    art,
    locale,
    open
  );
}

function Delta({ value, copy }: { value: number | null; copy: string }) {
  if (!value) return null;
  return (
    <span
      className={`tabular-nums ${value > 0 ? 'text-success' : 'text-text-3'}`}
    >
      {fillCopy(copy, { n: value > 0 ? `+${value}` : `−${-value}` })}
    </span>
  );
}

function download(name: string, parts: Array<Uint8Array | string>) {
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
}

export function StreamDebug({
  stream,
  locale
}: {
  stream: MessageStream;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account.path;
  const [open, setOpen] = useState(false);
  const [seq, setSeq] = useState(stream.frames[0]?.seq);
  const [exporting, setExporting] = useState(false);
  const frame = stream.frame(seq);
  return (
    <details
      className="bpp-panel p-3 text-small"
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary className="cursor-pointer">{copy.rawFrames}</summary>
      {open && (
        <div className="flex flex-col items-start gap-2">
          <label>
            {copy.frame}{' '}
            <select
              value={seq}
              onChange={(e) => setSeq(Number(e.target.value))}
            >
              {stream.frames.map((f) => (
                <option key={f.seq}>{f.seq}</option>
              ))}
            </select>
          </label>
          {frame && (
            <pre className="max-h-80 max-w-full overflow-auto text-small">
              {stream.readableFrame(frame)}
            </pre>
          )}
          {frame?.message && (
            <Button
              size="sm"
              onClick={() => download(`frame-${seq}.bin`, [frame.message!])}
            >
              {copy.downloadBytes}
            </Button>
          )}
          <Button
            size="sm"
            busy={exporting}
            onClick={() => {
              setExporting(true);
              void stream
                .exportFrames()
                .then((parts) => download('run-message-stream.json', parts))
                .finally(() => setExporting(false));
            }}
          >
            {copy.downloadAll}
          </Button>
        </div>
      )}
    </details>
  );
}

export function DecisionVisit({
  visit,
  battle,
  art,
  names,
  locale
}: {
  visit: Visit;
  battle?: RunBattle;
  art: ArtLookup;
  names: Names;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const labels = copy.path;
  const open = useOpenCard();
  const display = (c: CardRef) => pathCard(c, art, names, locale, open);
  const outcome = battle?.outcome;
  const fightId = battle?.encounterId;
  const encounter = visit.encounter
    ? display(visit.encounter)
    : fightId
      ? gameCard(
          { templateId: fightId, tier: null, enchant: null },
          names(fightId) ?? fightId.slice(0, 8),
          art,
          locale,
          open
        )
      : undefined;
  let title = labels.kinds[visit.kind];
  if (visit.kind === 'pvp') {
    if (battle?.opponentHero) title += ` · ${heroName(battle.opponentHero)}`;
  } else if (encounter && visit.kind !== 'levelUp') {
    title = fillCopy(
      visit.kind === 'monster' ? labels.fought : labels.visited,
      { name: encounter.name }
    );
  }
  const cardChip = (card: CardRef) => (
    <CardChip
      card={display(card)}
      title={card.templateId ?? card.instanceId}
      placeholder
    />
  );
  const moves = visit.actions.filter(
    (a) => a.verb === 'moved' && a.outcome === 'done'
  );
  const rerolls = visit.actions.filter(
    (a) => a.verb === 'rerolled' && a.outcome === 'done'
  );
  return (
    <VisitRow
      title={title}
      hour={
        visit.hour == null ? null : fillCopy(labels.hour, { n: visit.hour })
      }
      encounter={encounter}
      totals={
        <>
          {outcome && (
            <Badge tone={outcome === 'win' ? 'ok' : 'bad'}>
              {copy[outcome]}
            </Badge>
          )}
          <Delta value={visit.gold} copy={labels.net} />
          <Delta value={visit.health} copy={labels.health} />
        </>
      }
    >
      {visit.gap && labels.undecoded}
      {visit.actions.map((a) => {
        let verb = labels.verbs[a.verb];
        let amount = a.amount;
        let card = a.card;
        if (a.outcome === 'done') {
          if (
            a.verb === 'left' ||
            (a.verb === 'chose' &&
              encounter &&
              visit.encounter?.instanceId === a.card?.instanceId)
          )
            return null;
          if (a.verb === 'moved') {
            if (a !== moves[0]) return null;
            verb = fillCopy(labels.moves, { n: moves.length });
            card = null;
          }
          if (a.verb === 'rerolled') {
            if (a !== rerolls[0]) return null;
            verb = fillCopy(labels.rerolls, { n: rerolls.length });
            amount =
              visit.rerollFees.length === rerolls.length
                ? -visit.rerollFees.reduce((sum, fee) => sum + fee, 0)
                : null;
          }
        }
        const enchant = enchantments.find((name) => name === a.result);
        return (
          <div className="bpp-decision-row" key={a.order}>
            <span>{verb}</span>
            <div className="bpp-decision-card">
              {card && cardChip(card)}
              {a.result && (
                <span className="inline-flex items-center gap-1">
                  →{' '}
                  {enchant ? (
                    <EnchantLabel
                      enchant={enchant}
                      label={copy.enchantments[enchant]}
                    />
                  ) : (
                    (copy.tiers[a.result] ?? a.result)
                  )}
                </span>
              )}
              {a.outcome !== 'done' && (
                <Badge tone="warn">
                  {a.outcome === 'no-response'
                    ? labels.noResponse
                    : labels.attempted}
                </Badge>
              )}
            </div>
            {!!amount && (
              <span className="text-text-3 tabular-nums">
                {fillCopy(labels.gold, { n: amount })}
              </span>
            )}
          </div>
        );
      })}
      {visit.remaining.length > 0 && (
        <details className="bpp-visit-options">
          <summary>
            {fillCopy(visit.closed ? labels.notTaken : labels.offered, {
              n: visit.remaining.length
            })}
          </summary>
          {visit.remaining.map((c) => (
            <div className="bpp-decision-row" key={c.instanceId}>
              <div className="bpp-decision-card">{cardChip(c)}</div>
            </div>
          ))}
        </details>
      )}
    </VisitRow>
  );
}
