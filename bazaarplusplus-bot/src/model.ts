export interface Card {
  id: string;
  name: string;
  enchant: string | null;
  size: number;
  slot: number | null;
  tier: string | null;
}
export interface Battle {
  id: string;
  at: number;
  day: number | null;
  pvp: boolean;
  final: boolean;
  opponent: string | null;
  winner: 'player' | 'opponent' | null;
  cards: Card[] | null;
}
export interface Run {
  id: string;
  account: string;
  hero: string;
  status: string;
  ended: number | null;
  rank: string | null;
  day: number | null;
  wins: number | null;
  losses: number | null;
  initialRating: number | null;
  rating: number | null;
  delta: number | null;
  battles: Battle[];
}
export interface BundleRef {
  bundle_id: string;
  run_id: string;
  sha256: string;
  download_url: string;
  stored_at_ms: number;
}
export interface HeroRow {
  hero: string;
  segment: string;
  runs: { completed: number; scored: number; ten_win: number };
  outcomes: { perfect: number; gold: number; silver: number; bronze: number };
  ten_win_days: { known_count: number; sum_days: number };
  matchups: {
    opponent_hero: string;
    wins: number;
    losses: number;
    decided: number;
  }[];
}
export interface HeroSnapshot {
  schema_version: number;
  kind: string;
  window: { start: string; end: string; days: number };
  days: { day: string; rows: HeroRow[] }[];
}
export interface BuildSnapshot {
  schema_version: number;
  kind: string;
  schemas: { build: string[]; layout: string[]; stats: string[] };
  cards: string[];
  enchantments: (string | null)[];
  heroes: Record<
    string,
    { builds: [number[], number[][], (number | null)[]][] }
  >;
}
