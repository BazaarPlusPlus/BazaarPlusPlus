import { useState } from 'react';
import { Badge, Button, CardChip, VisitRow, enchantments } from '@bpp/ui';

import type { Locale } from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
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
  outcome,
  art,
  names,
  locale
}: {
  visit: Visit;
  outcome?: 'win' | 'loss' | null;
  art: ArtLookup;
  names: Names;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const labels = copy.path;
  const open = useOpenCard();
  const display = (c: CardRef) => pathCard(c, art, names, locale, open);
  const moves = visit.actions.filter(
    (a) => a.verb === 'moved' && a.outcome === 'done'
  ).length;
  const rerolls = visit.actions.filter(
    (a) => a.verb === 'rerolled' && a.outcome === 'done'
  ).length;
  return (
    <VisitRow
      kind={labels.kinds[visit.kind]}
      encounter={visit.encounter ? display(visit.encounter) : undefined}
      outcome={
        <>
          {visit.gap && labels.undecoded}
          {outcome && (
            <Badge tone={outcome === 'win' ? 'ok' : 'bad'}>
              {copy[outcome]}
            </Badge>
          )}
          {visit.actions.map((a) => {
            if (
              a.outcome === 'done' &&
              (['moved', 'rerolled', 'left'].includes(a.verb) ||
                (a.verb === 'chose' &&
                  visit.encounter?.instanceId === a.card?.instanceId))
            )
              return null;
            const enchant = enchantments.find((name) => name === a.result);
            return (
              <span
                className="inline-flex flex-wrap items-center gap-1"
                key={a.order}
              >
                {labels.verbs[a.verb]}
                {a.card && (
                  <CardChip
                    card={display(a.card)}
                    title={a.card.templateId ?? a.card.instanceId}
                  />
                )}
                {a.amount !== null && fillCopy(labels.gold, { n: a.amount })}
                {a.result &&
                  (enchant
                    ? copy.enchantments[enchant]
                    : (copy.tiers[a.result] ?? a.result))}
                {a.outcome !== 'done' && (
                  <Badge tone="warn">
                    {a.outcome === 'no-response'
                      ? labels.noResponse
                      : labels.attempted}
                  </Badge>
                )}
              </span>
            );
          })}
          {moves > 0 && <span>{fillCopy(labels.moves, { n: moves })}</span>}
          {rerolls > 0 && (
            <span>
              {fillCopy(labels.rerolls, { n: rerolls })}{' '}
              {visit.rerollFees.length === rerolls &&
                fillCopy(labels.gold, {
                  n: -visit.rerollFees.reduce((a, b) => a + b, 0) || 0
                })}
            </span>
          )}
          <Delta value={visit.gold} copy={labels.net} />
          <Delta value={visit.health} copy={labels.health} />
        </>
      }
      remaining={
        visit.remaining.length > 0 && (
          <details>
            <summary>{visit.closed ? labels.notTaken : labels.offered}</summary>
            <div className="flex flex-wrap gap-1">
              {visit.remaining.map((c) => (
                <CardChip
                  key={c.instanceId}
                  card={display(c)}
                  title={c.templateId ?? c.instanceId}
                />
              ))}
            </div>
          </details>
        )
      }
    />
  );
}
