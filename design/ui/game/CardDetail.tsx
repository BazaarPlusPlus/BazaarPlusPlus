import { useSyncExternalStore } from 'react';
import { FxFace } from './enchantment-fx/Face';
import { CardArt, EnchantLabel, type GameCard } from './Card';
import { CardText, type CardTextLine } from './CardText';
import { enchantments, type CardTier, type Enchantment } from './types';

const DEFINITION_SIZES: Record<string, number> = {
  Small: 1,
  Medium: 2,
  Large: 3
};

// One choice for every card detail and preview on the page, remembered across visits.
const ALL_ENCHANTMENTS = 'bpp.card-detail.all-enchantments';
const subscribers = new Set<() => void>();
let showAll: boolean | undefined;
function allEnchantments() {
  if (showAll === undefined)
    try {
      showAll = localStorage.getItem(ALL_ENCHANTMENTS) === '1';
    } catch {
      showAll = false;
    }
  return showAll;
}
function setAllEnchantments(next: boolean) {
  showAll = next;
  try {
    localStorage.setItem(ALL_ENCHANTMENTS, next ? '1' : '0');
  } catch {
    // Storage can be unavailable; the choice still holds for this page.
  }
  for (const notify of subscribers) notify();
}
function subscribe(notify: () => void) {
  subscribers.add(notify);
  return () => {
    subscribers.delete(notify);
  };
}

export type CardDefinition = {
  title: string | null;
  size: string;
  tags: string[];
  startingTier: CardTier;
  attributes: Partial<
    Record<CardTier, { CooldownMax?: number; AmmoMax?: number }>
  >;
  lines: CardTextLine[];
  enchantments: Record<string, { tags: string[]; lines: CardTextLine[] }>;
};

export type CardDetailLabels = {
  sizes: Record<string, string>;
  enchantments: string;
  /** The switch between the card's own enchantment and every enchantment it can take. */
  allEnchantments: string;
  cooldown: string;
  ammo: string;
  seconds: string;
  unavailable: string;
};

export function CardDetail({
  card,
  definition,
  labels,
  tagLabels,
  enchantLabels,
  tierLabels,
  titleId,
  preview = false
}: {
  card: GameCard;
  definition?: CardDefinition;
  labels: CardDetailLabels;
  tagLabels: Record<string, string>;
  enchantLabels: Record<Enchantment, string>;
  tierLabels: Record<string, string>;
  titleId?: string;
  /** A hover preview is read-only: it follows the enchantment switch but does not show it. */
  preview?: boolean;
}) {
  const all = useSyncExternalStore(subscribe, allEnchantments, () => false);
  const localizedTags = (tags: string[]) =>
    tags.map((tag) => tagLabels[tag]).filter(Boolean);
  const attrs = card.tier ? definition?.attributes[card.tier] : undefined;
  const qualities = Object.keys(definition?.attributes ?? {});
  const lines = (effects: CardTextLine[]) =>
    effects.map((line) => (
      <div key={line.id}>
        {line.tiers.length < qualities.length && (
          <span className="bpp-tier-label">
            {line.tiers.map((t) => tierLabels[t] ?? t).join(' · ')}
          </span>
        )}
        <CardText
          line={line}
          tier={card.tier}
          unavailable={labels.unavailable}
        />
      </div>
    ));
  const effects = Object.entries(definition?.enchantments ?? {});
  const current = effects.filter(([name]) => name === card.enchant);
  const entries = all
    ? [...current, ...effects.filter(([name]) => name !== card.enchant)]
    : current;
  return (
    <article className="bpp-card-detail">
      <header className="bpp-detail-head">
        {card.art && (
          <div
            className="bpp-detail-art"
            data-size={card.size ?? DEFINITION_SIZES[definition?.size ?? '']}
          >
            <CardArt src={card.art} alt="" fallback="" />
            {card.fx && card.enchant && (
              <FxFace source={card.fx} enchant={card.enchant} />
            )}
          </div>
        )}
        <div>
          <h2 id={titleId}>{definition?.title ?? card.name}</h2>
          <p>
            {[
              card.tierLabel,
              definition && (labels.sizes[definition.size] ?? definition.size),
              ...localizedTags(definition?.tags ?? [])
            ]
              .filter(Boolean)
              .join(' · ')}
          </p>
          <EnchantLabel enchant={card.enchant} label={card.enchantLabel} />
          {attrs?.CooldownMax != null && (
            <p>
              {labels.cooldown} {attrs.CooldownMax / 1000}
              {labels.seconds}
            </p>
          )}
          {attrs?.AmmoMax != null && (
            <p>
              {labels.ammo} {attrs.AmmoMax}
            </p>
          )}
        </div>
      </header>
      {definition ? (
        lines(definition.lines)
      ) : (
        <p className="bpp-card-unavailable">{labels.unavailable}</p>
      )}
      {effects.length > 0 && (entries.length > 0 || !preview) && (
        <section>
          <div className="bpp-detail-section-head">
            <h3>{labels.enchantments}</h3>
            {!preview && (
              <label className="bpp-detail-toggle">
                <input
                  type="checkbox"
                  checked={all}
                  onChange={(event) => setAllEnchantments(event.target.checked)}
                />
                {labels.allEnchantments}
              </label>
            )}
          </div>
          {entries.map(([name, effect]) => {
            const enchant = enchantments.find((e) => e === name) ?? null;
            const tags = localizedTags(effect.tags);
            return (
              <section
                key={name}
                className="bpp-detail-enchant"
                data-current={name === card.enchant}
              >
                <h4>
                  <EnchantLabel
                    enchant={enchant}
                    label={enchant ? enchantLabels[enchant] : name}
                  />
                </h4>
                {tags.length > 0 && <p>{tags.join(' · ')}</p>}
                {lines(effect.lines)}
              </section>
            );
          })}
        </section>
      )}
    </article>
  );
}
