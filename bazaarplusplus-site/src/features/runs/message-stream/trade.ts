import type { DecisionEntry } from './project';

/**
 * What a row shows for gold: the recorded price or sell price (0 included), and the balance
 * change only where it differs from that price; for other actions, the balance change.
 */
export function tradeGold(action: Extract<DecisionEntry, { kind: 'action' }>): {
  amount: number | null;
  net: number | null;
} {
  const expected =
    action.amount === null
      ? null
      : action.verb === 'sold'
        ? action.amount
        : action.verb === 'bought' || action.verb === 'picked'
          ? -action.amount
          : null;
  if (expected === null)
    return { amount: null, net: action.gold === 0 ? null : action.gold };
  return {
    amount: action.amount,
    net: action.gold !== null && action.gold !== expected ? action.gold : null
  };
}
