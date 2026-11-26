# CardDetail

Use CardDetail inside a Dialog to inspect an item or skill from a Run. Each enchantment takes one row: its pill, then its effect, with every effect starting at the same edge. The heading identifies the card with its art, captured tier and enchantment. Size, tags and static tier attributes come from Card Text. Cooldowns read in seconds.

CardText preserves localized prose and keyword labels. Read numeric values from left to right in applicable tier order, with the captured tier bold and underlined. Keep percent signs with their values. A missing translation or unresolved value displays the caller’s unavailable label.

List each enchantment with its own effect lines and added tags. The captured enchantment appears first with a leading rule.
