import { cardTiers, enchantments, type GameCard } from '@bpp/ui';
import type { Locale } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';

export function gameCard(
  card: {
    templateId: string | null;
    tier: string | null;
    enchant: string | null;
  },
  name: string,
  art: (templateId: string) => string | null,
  locale: Locale
): GameCard {
  const copy = getSiteCopy(locale).account;
  const enchant = enchantments.find((value) => value === card.enchant) ?? null;
  return {
    name,
    art: card.templateId ? art(card.templateId) : null,
    tier: cardTiers.find((value) => value === card.tier) ?? null,
    tierLabel: card.tier ? (copy.tiers[card.tier] ?? card.tier) : '',
    enchant,
    enchantLabel: enchant ? copy.enchantments[enchant] : (card.enchant ?? '')
  };
}
