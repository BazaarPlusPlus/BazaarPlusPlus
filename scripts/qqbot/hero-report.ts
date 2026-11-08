// Renders the daily report as three PNGs and sends them to the groups that
// `heroReport` in groups.json names:
//   1. 今日概览: key numbers, the seven-day runs and the hero callouts;
//   2. 英雄与阵容: the hero table and the popular Ten-Win items, skills and
//      enchantments;
//   3. 玩家榜: top Ten-Win players and the day's records.
// Hero numbers come from the published hero snapshot (heroes/latest.json); the
// rest from the Daily Highlights `bazaarlens highlights` prints, which name
// players and never reach a public bucket. Without --highlights the report is
// the first two images without their Daily Highlights parts.
//
//   bun scripts/workspace.ts run qqbot -- bun scripts/qqbot/hero-report.ts \
//     [--day YYYY-MM-DD] [--highlights highlights.json] [--out-dir dir]
//     [--send test|report]
//
// The report covers the snapshot's newest Source Day (window.end, a UTC date).
// --day waits until window.end is that day, because the CDN can serve the
// previous object for a minute after an upload. --send test sends to the test
// group only, --send report to every `heroReport` group; without --send it only writes
// the images. Layouts are hero-report.html, hero-report-heroes.html and
// hero-report-players.html with hero-report.css; names are the game's
// Simplified Chinese ones zh-names.ts publishes to the CDN; values come from design/tokens.json through the installer's
// generated tokens.css.
import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { createClient, groupsNamed, loadGroups, remedy } from './client.ts';
import type { QQError } from './client.ts';

const SNAPSHOT_URL =
  'https://cdn.bazaarplusplus.com/metrics/v6/heroes/latest.json';
const root = path.resolve(import.meta.dirname, '../..');

// The fields of heroes/latest.json this report reads
// (bazaarplusplus-analyzer/contracts/v6/heroes.schema.json).
interface HeroRow {
  hero: string;
  segment: string;
  runs: { completed: number; ten_win: number };
  outcomes: { perfect: number };
}

export interface HeroSnapshot {
  window: { start: string; end: string; days: number };
  days: { day: string; rows: HeroRow[] }[];
}

// The output of `bazaarlens highlights` (bazaarplusplus-analyzer/src/bazaarlens/highlights.py).
interface Named {
  name: string;
  hero: string | null;
}
export interface Highlights {
  day: string;
  players: number;
  ten_win_runs: number;
  perfect_runs: number;
  /** Runs by the UTC hour they started, 0 to 23. */
  hourly_runs: number[];
  ten_win_leaders: (Named & { ten_wins: number; runs: number })[];
  perfect_leaders: (Named & { perfect: number })[];
  ten_win_cards: (Named & { runs: number })[];
  ten_win_skills: (Named & { runs: number })[];
  ten_win_enchantments: (Named & { runs: number })[];
  records: Record<
    | 'legend_rating'
    | 'legend_gain'
    | 'ten_win_streak'
    | 'ten_win_heroes'
    | 'income'
    | 'gold'
    | 'fewest_items'
    | 'diamond_items'
    | 'enchanted_items',
    (Named & { value: number }) | null
  >;
}

export interface HeroLine {
  hero: string;
  runs: number;
  share: number | null;
  tenWinRate: number | null;
  /** Change of tenWinRate from the day before, as a fraction. */
  tenWinDelta: number | null;
}

export interface HeroReport {
  day: string;
  runs: number;
  /** Relative change of runs from the day before. */
  runsDelta: number | null;
  tenWins: number;
  perfect: number;
  /** Runs per day of the window, oldest first. */
  daily: { day: string; runs: number }[];
  heroes: HeroLine[];
}

const ratio = (a: number, b: number) => (b ? a / b : null);

interface HeroSum {
  runs: number;
  tenWin: number;
  perfect: number;
}

// Sums legend and non_legend into the `all` segment, per hero.
function byHero(rows: HeroRow[]): Map<string, HeroSum> {
  const heroes = new Map<string, HeroSum>();
  for (const row of rows) {
    const sum = heroes.get(row.hero) ?? { runs: 0, tenWin: 0, perfect: 0 };
    sum.runs += row.runs.completed;
    sum.tenWin += row.runs.ten_win;
    sum.perfect += row.outcomes.perfect;
    heroes.set(row.hero, sum);
  }
  return heroes;
}

const total = (m: Map<string, HeroSum>, key: keyof HeroSum) =>
  [...m.values()].reduce((sum, h) => sum + h[key], 0);

export function buildReport(snapshot: HeroSnapshot): HeroReport {
  // Partitions are newest first; the window holds consecutive days.
  const [today, before] = snapshot.days;
  if (!today || today.day !== snapshot.window.end)
    throw new Error(`the snapshot has no partition for ${snapshot.window.end}`);
  const now = byHero(today.rows);
  const then = before ? byHero(before.rows) : new Map<string, HeroSum>();
  const runs = total(now, 'runs');
  const previousRuns = total(then, 'runs');
  const heroes = [...now].map(([hero, h]): HeroLine => {
    const rate = ratio(h.tenWin, h.runs);
    const old = then.get(hero);
    const oldRate = old ? ratio(old.tenWin, old.runs) : null;
    return {
      hero,
      runs: h.runs,
      share: ratio(h.runs, runs),
      tenWinRate: rate,
      tenWinDelta: rate !== null && oldRate !== null ? rate - oldRate : null
    };
  });
  heroes.sort(
    (a, b) => (b.tenWinRate ?? -1) - (a.tenWinRate ?? -1) || b.runs - a.runs
  );
  return {
    day: today.day,
    runs,
    runsDelta: previousRuns ? runs / previousRuns - 1 : null,
    tenWins: total(now, 'tenWin'),
    perfect: total(now, 'perfect'),
    daily: snapshot.days
      .map((d) => ({ day: d.day, runs: total(byHero(d.rows), 'runs') }))
      .reverse(),
    heroes
  };
}

const ENTITIES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;'
};
const escape = (text: string) =>
  text.replace(/[&<>"']/g, (c) => ENTITIES[c] ?? c);
const count = (value: number) => value.toLocaleString('en-US');
const percent = (value: number | null) =>
  value === null ? '—' : `${(value * 100).toFixed(1)}%`;
const trend = (value: number | null) => {
  if (value === null) return '<span class="delta">—</span>';
  const sign = value > 0 ? '+' : value < 0 ? '−' : '';
  const tone = value > 0 ? 'up' : value < 0 ? 'down' : '';
  return `<span class="delta ${tone}">${sign}${percent(Math.abs(value))}</span>`;
};

// The game's Simplified Chinese names (zh-names.ts); a missing name stays English.
const NAMES_URL =
  'https://cdn.bazaarplusplus.com/mod-data/zh-names/zh-names.json';
export interface ZhNames {
  heroes: Record<string, string>;
  names: Record<string, string>;
}
let ZH: ZhNames = { heroes: {}, names: {} };
export function useNames(names: ZhNames): void {
  ZH = names;
}
const heroName = (id: string) => ZH.heroes[id] ?? id;
const zhName = (english: string) => ZH.names[english] ?? english;

// The hero-* token of each canonical hero; unknown heroes use hero-unknown.
const HERO_TOKENS: Record<string, string> = {
  Stelle: 'stelle',
  Mak: 'mak',
  Jules: 'jules',
  Dooley: 'dooley',
  Karnok: 'karnok',
  Pygmalien: 'pygmalien',
  Vanessa: 'vanessa',
  TheDragons: 'dragons'
};
const heroColor = (hero: string | null) =>
  `--hero-color: var(--hero-${(hero && HERO_TOKENS[hero]) || 'unknown'})`;
const hero = (id: string | null) =>
  id
    ? `<span class="hero" style="${heroColor(id)}"><span class="dot"></span>${escape(heroName(id))}</span>`
    : '<span class="muted">—</span>';
const WEEKDAYS = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];
const date = (day: string) => {
  const [y, m, d] = day.split('-').map(Number);
  const weekday = WEEKDAYS[new Date(Date.UTC(y, m - 1, d)).getUTCDay()];
  return `${y} 年 ${m} 月 ${d} 日 ${weekday}`;
};
const meter = (value: number, max: number, text: string) =>
  `<div class="meter"><span class="track"><span class="fill" style="width: ${max ? (value / max) * 100 : 0}%"></span></span><span>${text}</span></div>`;

function fill(template: string, values: Record<string, string>): string {
  const read = (file: string) => fs.readFileSync(path.join(root, file), 'utf8');
  const all: Record<string, string> = {
    tokens: read('bazaarplusplus-installer/src/styles/tokens.css'),
    styles: read('scripts/qqbot/hero-report.css'),
    mark: read('design/assets/Logos/bpp-mark.svg'),
    cover: `data:image/jpeg;base64,${fs
      .readFileSync(
        path.join(root, 'design/assets/Covers/the-bazaar/library_hero.jpg')
      )
      .toString('base64')}`,
    ...values
  };
  return read(`scripts/qqbot/${template}`).replace(
    /\{\{(\w+)\}\}/g,
    (_, key: string) => {
      if (!(key in all)) throw new Error(`${template}: no value ${key}`);
      return all[key];
    }
  );
}

const tile = (label: string, value: string, sub: string) =>
  `<div class="panel tile"><span class="label">${label}</span><span class="value">${value}</span><span class="sub">${sub}</span></div>`;
const callout = (label: string, id: string | null, value: string) =>
  `<div class="panel callout" style="${heroColor(id)}"><span class="label">${label}</span><span class="name">${id ? escape(heroName(id)) : '—'}</span><span class="value">${value}</span></div>`;
const table = (rows: string) =>
  `<div class="panel"><table><tbody>${rows}</tbody></table></div>`;

// Beijing time is UTC+8; the UTC day runs from 08:00 to 08:00 the next day.
const BEIJING_OFFSET = 8;

function hours(hourly: number[]): string {
  const beijing = hourly.map((_, i) => ({
    hour: (i + BEIJING_OFFSET) % 24,
    runs: hourly[i]
  }));
  const max = Math.max(...hourly);
  const peak = beijing.find((h) => h.runs === max);
  const bars = beijing
    .map(
      (h) =>
        `<div class="hour${h === peak ? ' peak' : ''}"><span class="bar" style="height: ${max ? (h.runs / max) * 100 : 0}%"></span><span class="tick">${h.hour % 4 === 0 ? h.hour : ''}</span></div>`
    )
    .join('');
  return `<section>
  <div class="head"><h2>对局时段</h2><span class="note">北京时间，高峰 ${peak?.hour ?? '—'}:00–${((peak?.hour ?? 0) + 1) % 24}:00</span></div>
  <div class="panel hours">${bars}</div>
</section>`;
}

export function renderOverview(
  report: HeroReport,
  highlights: Highlights | null,
  pages: number
): string {
  const tenWinRate = percent(ratio(report.tenWins, report.runs));
  const tiles = [
    tile('对局', count(report.runs), `较前日 ${trend(report.runsDelta)}`),
    highlights
      ? tile(
          '玩家',
          count(highlights.players),
          `人均 ${(report.runs / highlights.players).toFixed(1)} 局`
        )
      : tile('10胜率', tenWinRate, '全部英雄'),
    tile('十胜', count(report.tenWins), `10胜率 ${tenWinRate}`),
    tile(
      '完美十胜',
      count(report.perfect),
      `10 胜 0 负，占十胜 ${percent(ratio(report.perfect, report.tenWins))}`
    )
  ].join('');
  const max = Math.max(...report.daily.map((d) => d.runs));
  const chart = report.daily
    .map((d) => {
      const [, m, day] = d.day.split('-').map(Number);
      const today = d.day === report.day;
      return `<div class="day${today ? ' today' : ''}">${today ? `<span>${count(d.runs)}</span>` : ''}<span class="bar" style="height: ${max ? (d.runs / max) * 100 : 0}%"></span><span>${m}/${day}</span></div>`;
    })
    .join('');
  const popular = [...report.heroes].sort((a, b) => b.runs - a.runs)[0];
  const strongest = report.heroes[0];
  const rising = report.heroes
    .filter((h) => h.tenWinDelta !== null)
    .sort((a, b) => (b.tenWinDelta ?? 0) - (a.tenWinDelta ?? 0))[0];
  const callouts = [
    callout(
      '最热门',
      popular?.hero ?? null,
      `局数占比 ${percent(popular?.share ?? null)}`
    ),
    callout(
      '10胜率最高',
      strongest?.hero ?? null,
      `10胜率 ${percent(strongest?.tenWinRate ?? null)}`
    ),
    callout(
      '进步最快',
      rising?.hero ?? null,
      rising ? `10胜率 ${trend(rising.tenWinDelta)}` : '暂无前一天数据'
    )
  ].join('');
  return fill('hero-report.html', {
    date: date(report.day),
    pages: String(pages),
    tiles,
    chart,
    hours: highlights ? hours(highlights.hourly_runs) : '',
    callouts
  });
}

function popular(
  title: string,
  note: string,
  items: (Named & { runs: number })[],
  withHero: boolean
): string {
  const max = Math.max(0, ...items.map((c) => c.runs));
  const rows = items
    .map(
      (c, i) => `<tr>
  <td class="rank">${i + 1}</td>
  <td class="strong">${escape(zhName(c.name))}</td>
  ${withHero ? `<td>${hero(c.hero)}</td>` : ''}
  <td class="num">${meter(c.runs, max, count(c.runs))}</td>
</tr>`
    )
    .join('\n');
  return `<section>
  <div class="head"><h2>${title}</h2><span class="note">${note}</span></div>
  ${table(rows)}
</section>`;
}

function compact(title: string, items: (Named & { runs: number })[]): string {
  const rows = items
    .map(
      (c, i) => `<tr>
  <td class="rank">${i + 1}</td>
  <td class="strong">${escape(zhName(c.name))}</td>
  <td class="num muted">${count(c.runs)}</td>
</tr>`
    )
    .join('\n');
  return `<section><h2>${title}</h2>${table(rows)}</section>`;
}

export function renderHeroes(
  report: HeroReport,
  highlights: Highlights | null,
  pages: number
): string {
  const maxShare = Math.max(...report.heroes.map((h) => h.share ?? 0));
  const heroes = report.heroes
    .map(
      (h) => `<tr>
  <td>${hero(h.hero)}</td>
  <td class="num strong">${percent(h.tenWinRate)}</td>
  <td class="num">${trend(h.tenWinDelta)}</td>
  <td class="num">${meter(h.share ?? 0, maxShare, percent(h.share))}</td>
</tr>`
    )
    .join('\n');
  const builds = highlights
    ? [
        popular(
          '十胜热门物品',
          `出现在十胜阵容中的局数，共 ${count(highlights.ten_win_runs)} 局十胜`,
          highlights.ten_win_cards,
          true
        ),
        `<section><div class="split">${compact('热门技能', highlights.ten_win_skills)}${compact('热门附魔', highlights.ten_win_enchantments)}</div></section>`
      ].join('\n')
    : '';
  return fill('hero-report-heroes.html', {
    date: date(report.day),
    pages: String(pages),
    heroes,
    builds
  });
}

// Each record: its label and how its value reads.
const RECORDS: [keyof Highlights['records'], string, (v: number) => string][] =
  [
    ['legend_rating', '最高传奇分', count],
    ['legend_gain', '传奇分涨幅最多', (v) => `+${count(v)}`],
    ['ten_win_streak', '最长连续十胜', (v) => `${v} 连胜`],
    ['ten_win_heroes', '十胜英雄最多', (v) => `${v} 个英雄`],
    ['income', '单局最高收入', count],
    ['gold', '单局最多金币', count],
    ['fewest_items', '最少物品十胜', (v) => `${v} 件物品`],
    ['diamond_items', '钻石物品最多', (v) => `${v} 件钻石`],
    ['enchanted_items', '附魔物品最多', (v) => `${v} 件附魔`]
  ];

export function renderPlayers(highlights: Highlights): string {
  const leaders = highlights.ten_win_leaders
    .map((p, i) => {
      const rank = i + 1;
      const label =
        rank <= 3 ? `<span class="medal">${rank}</span>` : String(rank);
      return `<tr class="rank-${rank}">
  <td class="rank${rank <= 3 ? ' top' : ''}">${label}</td>
  <td><span class="player">${escape(p.name || '—')}</span></td>
  <td>${hero(p.hero)}</td>
  <td class="num strong">${count(p.ten_wins)}</td>
  <td class="num muted">${count(p.runs)}</td>
</tr>`;
    })
    .join('\n');
  const perfect = highlights.perfect_leaders
    .map(
      (p) => `<tr>
  <td><span class="player">${escape(p.name || '—')}</span></td>
  <td>${hero(p.hero)}</td>
  <td class="num strong">${count(p.perfect)} 次</td>
</tr>`
    )
    .join('\n');
  const records = RECORDS.map(([key, label, format]) => {
    const item = highlights.records[key];
    if (!item) return '';
    return `<div class="panel callout" style="${heroColor(item.hero)}"><span class="label">${label}</span><span class="name">${format(item.value)}</span><span class="value">${escape(item.name || '—')} · ${escape(item.hero ? heroName(item.hero) : '—')}</span></div>`;
  }).join('');
  return fill('hero-report-players.html', {
    date: date(highlights.day),
    players: count(highlights.players),
    perfectRuns: count(highlights.perfect_runs),
    leaders,
    perfect,
    records
  });
}

async function fetchSnapshot(day: string | undefined): Promise<HeroSnapshot> {
  const deadline = Date.now() + 5 * 60_000;
  for (;;) {
    // A unique query skips the CDN cache.
    const res = await fetch(`${SNAPSHOT_URL}?t=${Date.now()}`, {
      cache: 'no-store'
    });
    if (!res.ok) throw new Error(`${SNAPSHOT_URL} returned ${res.status}`);
    const snapshot = (await res.json()) as HeroSnapshot;
    if (!day || snapshot.window.end === day) return snapshot;
    if (Date.now() > deadline)
      throw new Error(
        `${SNAPSHOT_URL} still ends on ${snapshot.window.end}, not ${day}`
      );
    await new Promise((r) => setTimeout(r, 15_000));
  }
}

async function screenshot(pages: string[]): Promise<Buffer[]> {
  const { chromium } = await import('playwright');
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({
      viewport: { width: 720, height: 400 },
      deviceScaleFactor: 2
    });
    const images: Buffer[] = [];
    for (const html of pages) {
      await page.setContent(html, { waitUntil: 'load' });
      images.push(await page.locator('main').screenshot({ type: 'png' }));
    }
    return images;
  } finally {
    await browser.close();
  }
}

async function main() {
  const { values: args } = parseArgs({
    options: {
      day: { type: 'string' },
      highlights: { type: 'string' },
      'out-dir': { type: 'string', default: '.' },
      send: { type: 'string' }
    }
  });
  if (args.day && !/^\d{4}-\d{2}-\d{2}$/.test(args.day))
    throw new Error('--day must be YYYY-MM-DD');
  if (args.send && !['test', 'report'].includes(args.send))
    throw new Error('--send must be test or report');
  const report = buildReport(await fetchSnapshot(args.day));
  const zhNames = await fetch(NAMES_URL);
  if (!zhNames.ok) throw new Error(`${NAMES_URL} returned ${zhNames.status}`);
  useNames((await zhNames.json()) as ZhNames);
  const highlights = args.highlights
    ? (JSON.parse(fs.readFileSync(args.highlights, 'utf8')) as Highlights)
    : null;
  if (highlights && highlights.day !== report.day)
    throw new Error(
      `highlights are for ${highlights.day}, the snapshot ends on ${report.day}`
    );
  const pageCount = highlights ? 3 : 2;
  const pages = [
    renderOverview(report, highlights, pageCount),
    renderHeroes(report, highlights, pageCount)
  ];
  if (highlights) pages.push(renderPlayers(highlights));
  const images = await screenshot(pages);
  const names = ['overview', 'heroes', 'players']
    .slice(0, images.length)
    .map((part) => `daily-${report.day}-${part}.png`);
  fs.mkdirSync(args['out-dir'], { recursive: true });
  images.forEach((png, i) => {
    fs.writeFileSync(path.join(args['out-dir'], names[i]), png);
    console.log(`wrote ${names[i]} (${png.length} bytes)`);
  });
  if (!args.send) return;

  const qq = createClient();
  const config = loadGroups();
  let failed = 0;
  const groups =
    args.send === 'test'
      ? [config.test]
      : groupsNamed(config, config.heroReport);
  for (const g of groups) {
    try {
      for (const [i, png] of images.entries()) {
        const { file_info } = await qq.uploadImage(g.openid, png, names[i]);
        await qq.sendMedia(g.openid, file_info);
      }
      console.log(`${g.name}: sent ${images.length} images`);
    } catch (caught) {
      const e = caught as QQError;
      failed++;
      console.error(`${g.name}: ${e.message}`);
      const hint = remedy(e.code, g.name);
      if (hint) console.error(hint);
    }
  }
  if (failed) process.exitCode = 1;
}

if (import.meta.main) await main();
