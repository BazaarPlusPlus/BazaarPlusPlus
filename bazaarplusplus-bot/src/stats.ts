import { HEROES } from './commands';
import type { Card, HeroRow, HeroSnapshot, Run } from './model';

const RANKS = new Set([
  'Bronze',
  'Silver',
  'Gold',
  'Diamond',
  'Master',
  'Masters',
  'Legendary'
]);
export const accepted = (r: Run) =>
  HEROES.includes(r.hero) &&
  RANKS.has(r.rank ?? '') &&
  r.status === 'completed';
export const scored = (r: Run) =>
  r.wins !== null &&
  r.losses !== null &&
  r.wins >= 0 &&
  r.wins <= 10 &&
  r.losses >= 0;
export const tenWin = (r: Run) => scored(r) && r.wins === 10;
export const average = (values: (number | null)[]) => {
  const known = values.filter((x): x is number => x !== null);
  return known.length ? known.reduce((a, b) => a + b, 0) / known.length : null;
};
export function finalCards(run: Run): Card[] | null {
  const finals = run.battles.filter((b) => b.final);
  return finals.length === 1 ? finals[0].cards : null;
}
export function heroBaseline(
  snapshot: HeroSnapshot,
  hero: string,
  segment?: string
): HeroRow {
  const result: HeroRow = {
    hero,
    segment: segment ?? 'all',
    runs: { completed: 0, scored: 0, ten_win: 0 },
    outcomes: { perfect: 0, gold: 0, silver: 0, bronze: 0 },
    ten_win_days: { known_count: 0, sum_days: 0 },
    matchups: []
  };
  for (const day of snapshot.days)
    for (const row of day.rows) {
      if (row.hero !== hero || (segment && row.segment !== segment)) continue;
      for (const k of ['completed', 'scored', 'ten_win'] as const)
        result.runs[k] += row.runs[k];
      for (const k of ['perfect', 'gold', 'silver', 'bronze'] as const)
        result.outcomes[k] += row.outcomes[k];
      result.ten_win_days.known_count += row.ten_win_days.known_count;
      result.ten_win_days.sum_days += row.ten_win_days.sum_days;
      for (const m of row.matchups) {
        let sum = result.matchups.find(
          (x) => x.opponent_hero === m.opponent_hero
        );
        if (!sum) {
          sum = {
            opponent_hero: m.opponent_hero,
            wins: 0,
            losses: 0,
            decided: 0
          };
          result.matchups.push(sum);
        }
        sum.wins += m.wins;
        sum.losses += m.losses;
        sum.decided += m.decided;
      }
    }
  return result;
}
export function summarize(runs: Run[]) {
  const tens = runs.filter(tenWin);
  const battles = runs.flatMap((run) =>
    run.battles.filter((b) => b.pvp && b.winner !== null)
  );
  // Below Legendary the game keeps every rating at 500, so only Legendary Runs have a score.
  const rated = [...runs]
    .sort((a, b) => (a.ended ?? 0) - (b.ended ?? 0) || a.id.localeCompare(b.id))
    .filter(
      (r) =>
        r.rank === 'Legendary' && r.initialRating !== null && r.rating !== null
    );
  return {
    runs: runs.length,
    tenWins: tens.length,
    perfect: tens.filter((r) => r.losses === 0).length,
    meanWins: average(runs.filter(scored).map((r) => r.wins)),
    wins: battles.filter((b) => b.winner === 'player').length,
    losses: battles.filter((b) => b.winner === 'opponent').length,
    rating: rated.length
      ? {
          start: rated[0].initialRating!,
          end: rated.at(-1)!.rating!,
          delta: rated.at(-1)!.rating! - rated[0].initialRating!
        }
      : null
  };
}
