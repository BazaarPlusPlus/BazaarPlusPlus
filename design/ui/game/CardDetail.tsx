import { CardArt, EnchantLabel, type GameCard } from './Card';
import { CardText, type CardTextLine } from './CardText';
import { enchantments, type CardTier, type Enchantment } from './types';

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
  titleId
}: {
  card: GameCard;
  definition?: CardDefinition;
  labels: CardDetailLabels;
  tagLabels: Record<string, string>;
  enchantLabels: Record<Enchantment, string>;
  tierLabels: Record<string, string>;
  titleId?: string;
}) {
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
  const entries = card.enchant
    ? [
        ...effects.filter(([name]) => name === card.enchant),
        ...effects.filter(([name]) => name !== card.enchant)
      ]
    : effects;
  return (
    <article className="bpp-card-detail">
      <header className="bpp-detail-head">
        {card.art && (
          <div className="bpp-detail-art">
            <CardArt src={card.art} alt="" fallback="" />
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
      {entries.length > 0 && (
        <section>
          <h3>{labels.enchantments}</h3>
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
