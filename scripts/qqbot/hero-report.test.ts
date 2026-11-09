import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';

import {
  buildReport,
  renderHeroes,
  renderOverview,
  renderPlayers,
  useNames,
  type HeroSnapshot,
  type Highlights
} from './hero-report.ts';

// The analyzer's shared goldens: heroes/latest.json and the Daily Highlights of
// the same one-day window.
const analyzer = path.resolve(
  import.meta.dirname,
  '../../bazaarplusplus-analyzer'
);
const read = <T>(file: string) =>
  JSON.parse(fs.readFileSync(path.join(analyzer, file), 'utf8')) as T;
const snapshot = read<HeroSnapshot>('contracts/v6/fixtures/heroes.latest.json');
const highlights = read<Highlights>(
  'tests/fixtures/pipeline-golden.highlights.json'
);

test('the three images read the analyzer goldens and fill every value', () => {
  const report = buildReport(snapshot);
  expect(report.day).toBe(snapshot.window.end);
  expect(highlights.day).toBe(report.day);
  expect(report.runs).toBe(
    snapshot.days[0].rows.reduce((sum, row) => sum + row.runs.completed, 0)
  );
  expect(report.runsDelta).toBeNull();
  useNames({ heroes: { Vanessa: '瓦内莎' }, names: {} });
  const overview = renderOverview(report, highlights, 3);
  const heroes = renderHeroes(report, highlights, 3);
  const players = renderPlayers(highlights);
  for (const html of [overview, heroes, players])
    expect(html).not.toMatch(/\{\{\w+\}\}/);
  // Hero names are the game's Simplified Chinese ones.
  expect(heroes).toContain('瓦内莎');
  for (const card of highlights.ten_win_cards)
    expect(heroes).toContain(card.name);
  for (const p of highlights.ten_win_leaders) expect(players).toContain(p.name);
});
