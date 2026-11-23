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
  useNames({
    heroes: { Vanessa: '瓦内莎' },
    names: { 'Item One': '测试物品' }
  });
  const overview = renderOverview(report, highlights, 3);
  const heroes = renderHeroes(report, highlights, 3);
  const players = renderPlayers(highlights);
  for (const html of [overview, heroes, players])
    expect(html).not.toMatch(/\{\{\w+\}\}/);
  expect(heroes).toContain('瓦内莎');
  expect(heroes).toContain('传奇 10胜率');
  expect(heroes).toContain('测试物品');
  expect(heroes).toContain('Fixture Item');
  expect(heroes).toContain('100.0%');
  for (const p of highlights.perfect_leaders) expect(players).toContain(p.name);
  expect(players).toContain('结算时最高日收入');
  expect(players).toContain('结算时最多金币');
});

test('compares all segments to pooled counts and displays only significant changes', () => {
  const row = (
    hero: string,
    completed: number,
    ten_win: number,
    segment = 'non_legend'
  ) => ({
    hero,
    segment,
    runs: { completed, ten_win },
    outcomes: { perfect: 0 }
  });
  const report = buildReport({
    window: { start: '2026-08-08', end: '2026-08-10', days: 3 },
    days: [
      {
        day: '2026-08-10',
        rows: [
          row('Vanessa', 400, 280, 'legend'),
          row('Vanessa', 600, 320),
          row('Mak', 1000, 510),
          row('Dooley', 1000, 390)
        ]
      },
      {
        day: '2026-08-09',
        rows: [
          row('Vanessa', 100, 80),
          row('Mak', 500, 250),
          row('Dooley', 400, 200)
        ]
      },
      {
        day: '2026-08-08',
        rows: [
          row('Vanessa', 900, 420, 'legend'),
          row('Mak', 1500, 750),
          row('Dooley', 600, 300)
        ]
      }
    ]
  });
  expect(report.baselineDays).toBe(2);
  expect(report.runsDelta).toBeCloseTo(0.5);
  expect(report.daily).toEqual([
    { day: '2026-08-08', runs: 3000, tenWinRate: 0.49 },
    { day: '2026-08-09', runs: 1000, tenWinRate: 0.53 },
    { day: '2026-08-10', runs: 3000, tenWinRate: 0.5 }
  ]);
  const [vanessa, mak, dooley] = report.heroes;
  expect(vanessa).toMatchObject({
    hero: 'Vanessa',
    tenWinRate: 0.6,
    legendTenWinRate: 0.7
  });
  expect(vanessa.tenWinDelta).toBeCloseTo(0.1);
  expect(mak).toMatchObject({
    hero: 'Mak',
    tenWinDelta: 0,
    legendTenWinRate: null
  });
  expect(dooley.tenWinDelta).toBeCloseTo(-0.11);

  const overview = renderOverview(report, null, 2);
  expect(overview).toContain('较前 2 日均值');
  expect(overview).toContain('进步最快');
  expect(overview).toContain(
    'height: 33.33333333333333%"><span class="bar-count">1,000'
  );
  expect(overview).toContain('<span>8/9</span>\n  <span>53.0%</span>');
  const heroes = renderHeroes(report, null, 2);
  expect(heroes).toContain('较前 2 日');
  expect(heroes).toContain('<span class="delta">持平</span>');
  expect(heroes).toContain('−11.0 个百分点');

  const noRisingHero = renderOverview(
    { ...report, heroes: [mak, dooley] },
    null,
    2
  );
  expect(noRisingHero).toContain('暂无英雄 10胜率显著上升');
  expect(noRisingHero).not.toContain('进步最快');
});

test('an empty day shows no average and no peak hour', () => {
  const report = buildReport(snapshot);
  const empty = { ...highlights, players: 0, hourly_runs: Array(24).fill(0) };
  const overview = renderOverview(report, empty, 3);
  expect(overview).toContain('人均 — 局');
  expect(overview).not.toContain('NaN');
  expect(overview).not.toContain('高峰');
});
