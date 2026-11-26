import type { ArtLookup } from './card-art';
import { cardTiers, enchantments, type CardSize, type GameCard } from '@bpp/ui';
import type { Locale } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import { createElement } from 'react';
import { CardDefinition, type useOpenCard } from './CardDetails';

export function gameCard(
  card: {
    templateId: string | null;
    tier: string | null;
    enchant: string | null;
    size?: number | null;
  },
  name: string,
  art: ArtLookup,
  locale: Locale,
  open?: ReturnType<typeof useOpenCard>
): GameCard {
  const copy = getSiteCopy(locale).account;
  const enchant = enchantments.find((value) => value === card.enchant) ?? null;
  const display = {
    name,
    fx:
      enchant && card.templateId
        ? art.fx?.(card.templateId, enchant === 'Tiny', card.size ?? undefined)
        : undefined,
    art: card.templateId ? art(card.templateId) : null,
    tier: cardTiers.find((value) => value === card.tier) ?? null,
    tierLabel: card.tier ? (copy.tiers[card.tier] ?? card.tier) : '',
    enchant,
    enchantLabel: enchant ? copy.enchantments[enchant] : (card.enchant ?? ''),
    size:
      ([1, 2, 3] as const).find((n): n is CardSize => n === card.size) ?? null
  };
  return {
    ...display,
    preview: card.templateId
      ? createElement(CardDefinition, {
          templateId: card.templateId,
          card: display,
          locale,
          preview: true
        })
      : undefined,
    onOpen: open
      ? () => open({ templateId: card.templateId, card: display })
      : undefined
  };
}
