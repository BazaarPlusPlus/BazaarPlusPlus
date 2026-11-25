import { useState, type ReactNode } from 'react';
import { Badge, Button, CardChip, CardTile, enchantments } from '@bpp/ui';

import type { Locale } from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import type { DecisionPath as Path } from './message-stream/decision-path';
import type { CardRef, DecisionEntry } from './message-stream/project';
import { gameCard } from './game-card';
import { useOpenCard } from './CardDetails';
import { tradeGold } from './message-stream/trade';
import type { MessageStream } from './message-stream/stream';

type ArtLookup = (templateId: string) => string | null;
type Action = Extract<DecisionEntry, { kind: 'action' }>;

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

/** The frames an entry was read from, as readable JSON; built only when shown. */
function RawFrames({
  stream,
  seqs,
  locale
}: {
  stream: MessageStream;
  seqs: number[];
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account.path;
  return (
    <div className="col-span-2 flex flex-col gap-2">
      {seqs.map((seq) => {
        const frame = stream.frame(seq);
        if (!frame) return null;
        const decoded = stream.message(frame);
        return (
          <div key={seq} className="flex flex-col gap-1">
            {frame.message && (
              <Button
                size="sm"
                variant={decoded && !decoded.ok ? 'secondary' : 'ghost'}
                className="w-fit"
                onClick={() => download(`frame-${seq}.bin`, [frame.message!])}
              >
                {copy.downloadBytes}
              </Button>
            )}
            <pre className="max-h-80 overflow-auto rounded-inner border border-line bg-canvas p-2 font-mono text-micro leading-snug text-text-2">
              {stream.readableFrame(frame)}
            </pre>
          </div>
        );
      })}
    </div>
  );
}

/** One row of an hour: what happened, and a toggle for the frames it was read from. */
function PathRow({
  row,
  stream,
  art,
  names,
  locale,
  renderBattle
}: {
  row: Row;
  stream: MessageStream;
  art: ArtLookup;
  names: Names;
  locale: Locale;
  renderBattle: (battleId: string) => ReactNode;
}) {
  const copy = getSiteCopy(locale).account.path;
  const [open, setOpen] = useState(false);
  const seqs = rowFrames(row);
  return (
    <li className="grid min-w-0 grid-cols-[minmax(0,1fr)_auto] items-start gap-x-3 gap-y-2">
      <div className={`min-w-0 ${seqs.length ? '' : 'col-span-2'}`}>
        <RowBody
          row={row}
          art={art}
          names={names}
          locale={locale}
          renderBattle={renderBattle}
        />
      </div>
      {seqs.length > 0 && (
        <Button
          size="icon-sm"
          variant="ghost"
          aria-label={copy.rawFrames}
          title={copy.rawFrames}
          aria-expanded={open}
          onClick={() => setOpen(!open)}
        >
          <span aria-hidden="true" className="font-mono text-micro">
            {'{ }'}
          </span>
        </Button>
      )}
      {open && <RawFrames stream={stream} seqs={seqs} locale={locale} />}
    </li>
  );
}

function Offer({
  entry,
  art,
  names,
  locale
}: {
  entry: Extract<DecisionEntry, { kind: 'offer' }>;
  art: ArtLookup;
  names: Names;
  locale: Locale;
}) {
  const open = useOpenCard();
  const copy = getSiteCopy(locale).account;
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-small">
        <span className="font-medium text-text-2">{copy.path.offered}</span>
        {entry.encounter && (
          <CardChip
            card={pathCard(entry.encounter, art, names, locale, open)}
            title={entry.encounter.templateId ?? entry.encounter.instanceId}
          />
        )}
        {entry.rerollCost !== null && entry.rerollsRemaining !== null && (
          <span className="text-text-3 tabular-nums">
            {fillCopy(copy.path.reroll, {
              cost: entry.rerollCost,
              left: entry.rerollsRemaining
            })}
          </span>
        )}
      </div>
      <ul className="flex flex-wrap gap-2">
        {entry.cards.map((card) => {
          const display = pathCard(card, art, names, locale, open);
          return (
            <li
              key={card.instanceId}
              className="flex w-16 flex-col gap-1"
              title={card.templateId ?? card.instanceId}
            >
              <CardTile card={display} caption />
              {(entry.free || card.price !== null) && (
                <span className="text-micro text-accent tabular-nums">
                  {entry.free
                    ? copy.path.free
                    : fillCopy(copy.path.gold, { n: card.price ?? 0 })}
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function ActionLine({
  actions,
  art,
  names,
  locale
}: {
  actions: Action[];
  art: ArtLookup;
  names: Names;
  locale: Locale;
}) {
  const open = useOpenCard();
  const copy = getSiteCopy(locale).account;
  const first = actions[0];
  const resultName = (value: string) => {
    const enchant = enchantments.find((name) => name === value);
    return enchant ? copy.enchantments[enchant] : (copy.tiers[value] ?? value);
  };
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-small">
      <span className="font-medium text-text-2">
        {copy.path.verbs[first.verb]}
      </span>
      {actions.map((action) => (
        <span
          key={action.order}
          className="inline-flex min-w-0 items-center gap-x-2"
        >
          {action.card && (
            <CardChip
              card={pathCard(action.card, art, names, locale, open)}
              title={action.card.templateId ?? action.card.instanceId}
            />
          )}
          {action.result && (
            <span className="text-accent">→ {resultName(action.result)}</span>
          )}
          <Gold action={action} locale={locale} />
          <Delta value={action.health} copy={copy.path.health} />
          {action.outcome !== 'done' && (
            <Badge tone="warn">
              {action.outcome === 'no-response'
                ? copy.path.noResponse
                : copy.path.attempted}
            </Badge>
          )}
          {action.error && (
            <span className="text-danger-text">{action.error}</span>
          )}
        </span>
      ))}
    </div>
  );
}

/** The recorded price (0 included), then the balance change only where it differs. */
function Gold({ action, locale }: { action: Action; locale: Locale }) {
  const copy = getSiteCopy(locale).account.path;
  const { amount, net } = tradeGold(action);
  return (
    <>
      {amount !== null && (
        <span className="text-text-3 tabular-nums">
          {fillCopy(copy.gold, { n: amount })}
        </span>
      )}
      <Delta value={net} copy={amount === null ? copy.gold : copy.net} />
    </>
  );
}

type Row =
  | { kind: 'actions'; actions: Action[] }
  | { kind: 'entry'; entry: Exclude<DecisionEntry, Action> };

/** Consecutive moves read as one line. */
function rows(entries: DecisionEntry[]): Row[] {
  const out: Row[] = [];
  for (const entry of entries) {
    const last = out.at(-1);
    if (entry.kind !== 'action') out.push({ kind: 'entry', entry });
    else if (
      last?.kind === 'actions' &&
      entry.verb === 'moved' &&
      last.actions[0].verb === 'moved'
    )
      last.actions.push(entry);
    else out.push({ kind: 'actions', actions: [entry] });
  }
  return out;
}

function rowFrames(row: Row): number[] {
  return row.kind === 'actions'
    ? row.actions.flatMap((a) => a.frames)
    : row.entry.frames;
}

export default function DecisionPath({
  path,
  names,
  art,
  locale,
  renderBattle
}: {
  path: Path;
  names: Names;
  art: ArtLookup;
  locale: Locale;
  renderBattle: (battleId: string) => ReactNode;
}) {
  const copy = getSiteCopy(locale).account;
  const [exporting, setExporting] = useState(false);
  const groups = path.hours;
  const days = new Map<number | null, typeof groups>();
  for (const group of groups) {
    days.set(group.day, [...(days.get(group.day) ?? []), group]);
  }
  return (
    <div className="flex flex-col gap-4">
      <ol className="flex flex-col gap-4">
        {[...days].map(([day, hours]) => (
          <li key={day ?? 'start'} className="flex flex-col gap-2">
            <h2 className="text-body-medium text-text-1 tabular-nums">
              {day === null ? '—' : fillCopy(copy.day, { day })}
            </h2>
            <ol className="flex flex-col gap-2">
              {hours.map((group, index) => (
                <li
                  key={`${group.hour}-${index}`}
                  className="bpp-panel grid gap-2 p-3 sm:grid-cols-[5rem_minmax(0,1fr)] sm:gap-4 sm:p-4"
                >
                  <span className="text-small text-text-3 tabular-nums">
                    {group.hour === null
                      ? '—'
                      : fillCopy(copy.path.hour, { hour: group.hour })}
                  </span>
                  <ol className="flex min-w-0 flex-col gap-3">
                    {rows(group.entries).map((row) => (
                      <PathRow
                        key={
                          rowFrames(row)[0] ??
                          (row.kind === 'entry' && row.entry.kind === 'battle'
                            ? row.entry.battleId
                            : row.kind)
                        }
                        row={row}
                        stream={path.stream}
                        art={art}
                        names={names}
                        locale={locale}
                        renderBattle={renderBattle}
                      />
                    ))}
                  </ol>
                </li>
              ))}
            </ol>
          </li>
        ))}
      </ol>
      <Button
        size="sm"
        variant="ghost"
        className="w-fit"
        busy={exporting}
        onClick={() => {
          setExporting(true);
          void path.stream
            .exportFrames()
            .then((parts) => download('run-message-stream.json', parts))
            .finally(() => setExporting(false));
        }}
      >
        {copy.path.downloadAll}
      </Button>
    </div>
  );
}

function RowBody({
  row,
  art,
  names,
  locale,
  renderBattle
}: {
  row: Row;
  art: ArtLookup;
  names: Names;
  locale: Locale;
  renderBattle: (battleId: string) => ReactNode;
}) {
  const copy = getSiteCopy(locale).account.path;
  if (row.kind === 'actions') {
    return (
      <ActionLine
        actions={row.actions}
        art={art}
        names={names}
        locale={locale}
      />
    );
  }
  const { entry } = row;
  switch (entry.kind) {
    case 'offer':
      return <Offer entry={entry} art={art} names={names} locale={locale} />;
    case 'battle':
      return renderBattle(entry.battleId);
    case 'reconnect':
      return (
        <span className="text-small font-medium text-text-2">
          {copy.verbs.reconnect}
        </span>
      );
    case 'undecoded':
      return (
        <span className="flex flex-wrap items-center gap-2 text-small">
          <Badge tone="bad">{copy.undecoded}</Badge>
          <span className="text-text-3">{entry.error}</span>
        </span>
      );
    case 'update':
      return (
        <span className="flex flex-wrap items-center gap-3 text-small">
          <span className="font-medium text-text-2">{copy.update}</span>
          <Delta value={entry.gold} copy={copy.gold} />
          <Delta value={entry.health} copy={copy.health} />
        </span>
      );
  }
}
