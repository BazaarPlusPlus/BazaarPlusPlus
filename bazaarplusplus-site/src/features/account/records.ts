import type { AccountRecord } from './account-api';

/** Ten wins is a full run in The Bazaar, whatever the losses. */
const TEN_WINS = 10;

export type HeroRecord = {
  hero: string;
  runs: number;
  avgVictories: number;
  tenWins: number;
};

export type RatingPoint = {
  endedAtMs: number;
  rating: number;
  rank: string | null;
};

export type RecordStats = {
  runs: number;
  avgVictories: number;
  tenWins: number;
  /** The sum of each Run's rating change: the net change over the window. */
  ratingChange: number | null;
  heroes: HeroRecord[];
  /** Each Run's final rating, oldest first. */
  rating: RatingPoint[];
};

export type RecordDay = { dayStartMs: number; runs: AccountRecord[] };

function average(values: number[]): number {
  return values.length === 0
    ? 0
    : values.reduce((sum, value) => sum + value, 0) / values.length;
}

/** Newest first, by when each Run ended. */
function newestFirst(runs: readonly AccountRecord[]): AccountRecord[] {
  return [...runs].sort(
    (a, b) => b.ended_at_ms - a.ended_at_ms || (a.run_id < b.run_id ? 1 : -1)
  );
}

function wins(run: AccountRecord): number {
  return run.victories ?? 0;
}

export function recordStats(runs: readonly AccountRecord[]): RecordStats {
  const byHero = new Map<string, AccountRecord[]>();
  for (const run of runs) {
    if (run.hero == null) continue;
    byHero.set(run.hero, [...(byHero.get(run.hero) ?? []), run]);
  }
  const deltas = runs.flatMap((run) =>
    run.final_player_rating_delta == null ? [] : [run.final_player_rating_delta]
  );
  return {
    runs: runs.length,
    avgVictories: average(runs.map(wins)),
    tenWins: runs.filter((run) => wins(run) >= TEN_WINS).length,
    ratingChange:
      deltas.length === 0 ? null : deltas.reduce((sum, d) => sum + d, 0),
    heroes: [...byHero]
      .map(([hero, heroRuns]) => ({
        hero,
        runs: heroRuns.length,
        avgVictories: average(heroRuns.map(wins)),
        tenWins: heroRuns.filter((run) => wins(run) >= TEN_WINS).length
      }))
      .sort((a, b) => b.runs - a.runs || a.hero.localeCompare(b.hero)),
    rating: newestFirst(runs)
      .reverse()
      .flatMap((run) =>
        run.final_player_rating == null
          ? []
          : [
              {
                endedAtMs: run.ended_at_ms,
                rating: run.final_player_rating,
                rank: run.final_player_rank
              }
            ]
      )
  };
}

/** Runs grouped by the local calendar day they ended, newest day first. */
export function groupByDay(runs: readonly AccountRecord[]): RecordDay[] {
  const days: RecordDay[] = [];
  for (const run of newestFirst(runs)) {
    const ended = new Date(run.ended_at_ms);
    const dayStartMs = new Date(
      ended.getFullYear(),
      ended.getMonth(),
      ended.getDate()
    ).getTime();
    const last = days.at(-1);
    if (last?.dayStartMs === dayStartMs) last.runs.push(run);
    else days.push({ dayStartMs, runs: [run] });
  }
  return days;
}
