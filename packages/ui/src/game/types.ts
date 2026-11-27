export type CardSize = 1 | 2 | 3;
export const cardTiers = [
  'Bronze',
  'Silver',
  'Gold',
  'Diamond',
  'Legendary'
] as const;
export type CardTier = (typeof cardTiers)[number];
export const enchantments = [
  'Deadly',
  'Ethereal',
  'Fiery',
  'Golden',
  'Heavy',
  'Icy',
  'Mystical',
  'Obsidian',
  'Radiant',
  'Restorative',
  'Shielded',
  'Shiny',
  'Tiny',
  'Toxic',
  'Turbo',
  'Mossy'
] as const;
export type Enchantment = (typeof enchantments)[number];
