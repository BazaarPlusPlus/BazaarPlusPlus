import { beijingDate, type Names } from './commands';
import { CARD_ART, type CardArt } from './data';
import type { BuildSnapshot, Card, HeroSnapshot, Run } from './model';
import { finalCards, heroBaseline, scored, summarize } from './stats';

export const escape = (v: string | number | null | undefined): string =>
  String(v ?? '').replace(
    /[&<>"']/g,
    (c) =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[
        c
      ]!
  );
const n = (v: number | null) =>
  v === null ? '—' : v.toFixed(1).replace(/\.0$/, '');
const signed = (v: number) => `${v > 0 ? '+' : ''}${n(v)}`;
const pct = (a: number, b: number) =>
  b ? `${Math.round((a / b) * 100)}%` : '—';
const label = (hero: string, names: Names) => names.heroes[hero] ?? hero;
const nameOf = (name: string, names: Names) => {
  const english = names.cards?.[name] ?? name;
  return (
    names.names[english] ??
    (/^[0-9a-f-]{36}$/i.test(english) ? '物品资料待更新' : english)
  );
};
const span = (from: number, before: number) => {
  const first = beijingDate(from);
  const last = beijingDate(before - 1);
  return first === last ? first : `${first} 至 ${last.slice(5)}`;
};
const RANKS: Record<string, string> = {
  Bronze: '青铜',
  Silver: '白银',
  Gold: '黄金',
  Diamond: '钻石',
  Legendary: '传奇'
};
const TIERS = ['Bronze', 'Silver', 'Gold', 'Diamond', 'Legendary'];
const RESULT = ['#D8B7FF', '#E5C777', '#D8DEE8', '#C9A08A', '#A88A80'];
const resultColour = (r: Run) =>
  RESULT[
    !scored(r)
      ? 4
      : r.wins === 10
        ? r.losses === 0
          ? 0
          : 1
        : r.wins! >= 7
          ? 2
          : r.wins! >= 4
            ? 3
            : 4
  ];
const side = (value: string, caption: string, colour = '') =>
  `<div class="side"><b${colour ? ` style="color:${colour}"` : ''}>${value}</b><small>${escape(caption)}</small></div>`;
const css = `*{box-sizing:border-box}body{margin:0;background:#0b0805;color:#f2e3bf;font:400 14px/1.4 "Noto Sans CJK SC","PingFang SC","WenQuanYi Zen Hei","Microsoft YaHei",sans-serif;font-variant-numeric:tabular-nums lining-nums;-webkit-font-smoothing:antialiased}b{font-weight:700}main{width:max-content;padding:12px}.shell{display:grid;gap:8px;min-width:560px;padding:16px;border:1px solid rgba(178,134,64,.22);border-radius:16px;background:radial-gradient(circle at 100% 0,rgba(217,170,88,.1),transparent 30%),linear-gradient(180deg,#140d07,#090704)}header{display:grid;gap:10px;margin-bottom:6px}.top{display:flex;align-items:baseline;justify-content:space-between;gap:16px;font-size:12px;line-height:1;color:rgba(236,219,184,.55)}.brand{font-size:13px;font-weight:700;letter-spacing:.06em;color:#ee8e1f}h1{margin:0;font-size:26px;line-height:1.2;font-weight:700;overflow-wrap:anywhere}.stats{display:grid;grid-template-columns:repeat(4,1fr);border:1px solid rgba(178,134,64,.14);border-radius:10px;background:rgba(255,236,200,.03)}.stat{display:grid;justify-items:center;row-gap:6px;padding:12px 8px}.stat+.stat{border-left:1px solid rgba(178,134,64,.14)}.stat b{font-size:24px;line-height:1;white-space:nowrap}.stat small{font-size:12px;line-height:1;color:rgba(236,219,184,.55)}.stat b.up{color:#9ecc8a}.stat b.down{color:#e6664f}.row{display:grid;grid-template-columns:56px 1fr;align-items:center;column-gap:10px;padding:6px;border:1px solid rgba(178,134,64,.14);border-radius:10px;background:rgba(255,236,200,.03)}.side{display:grid;justify-items:center;row-gap:6px}.side b{font-size:28px;line-height:1}.side small{font-size:12px;line-height:1;color:rgba(236,219,184,.55);white-space:nowrap}.stage{display:flex;justify-content:center;align-items:center;gap:4px}.stage p{margin:0;font-size:13px;color:rgba(236,219,184,.5)}.card{--h:100px;flex:none;height:var(--h);border:2px solid #3a3733;border-radius:7px;background:#1d1c1a center/100% 100% no-repeat;overflow:hidden;display:flex;flex-direction:column;justify-content:flex-end}.s1{width:calc(var(--h) / 2)}.s2{width:var(--h)}.s3{width:calc(var(--h) * 1.5)}.card span{padding:2px 4px;background:rgba(0,0,0,.66);color:#fff;font-size:11px;line-height:1.3;overflow-wrap:anywhere}.card b{display:block;font-weight:400;color:#ffc759}.t-Bronze{border-color:#de966e}.t-Silver{border-color:#c0c0c0}.t-Gold{border-color:#ffd700}.t-Diamond{border-color:#00ffff}.t-Legendary{border-color:#ff4500}.note{margin:4px 0 0;font-size:12px;color:rgba(236,219,184,.5)}`;
function page(title: string, top: string, head: string, body: string): string {
  return `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=720"><title>${escape(title)}</title><style>${css}</style><main><section class="shell"><header><div class="top"><div class="brand">BazaarPlusPlus</div><span>${escape(top)}</span></div><h1>${escape(title)}</h1>${head}</header>${body}</section></main></html>`;
}
const stat = (title: string, value: string, tone = '') =>
  `<div class="stat"><b${tone ? ` class="${tone}"` : ''}>${value}</b><small>${title}</small></div>`;
// Stretch the square art to the in-game card proportions. The enchantment is separate from the art.
function cardHtml(c: Card, names: Names, art: CardArt): string {
  const name = !art[c.id] && c.name ? escape(nameOf(c.name, names)) : '';
  const enchant = c.enchant ? `<b>${escape(nameOf(c.enchant, names))}</b>` : '';
  const style = art[c.id]
    ? ` style="background-image:url('${CARD_ART}/${escape(art[c.id])}')"`
    : '';
  return `<div class="card s${Math.min(Math.max(c.size, 1), 3)} t-${escape(c.tier)}"${style}>${name || enchant ? `<span>${name}${enchant}</span>` : ''}</div>`;
}
const row = (head: string, cards: Card[] | null, names: Names, art: CardArt) =>
  `<div class="row">${head}<div class="stage">${cards ? cards.map((c) => cardHtml(c, names, art)).join('') : '<p>这局未保留最终阵容</p>'}</div></div>`;
// Build snapshots omit card names, so their boards rely on Card Art.
function layoutCards(snapshot: BuildSnapshot, layout: number[][]): Card[] {
  return layout
    .map(([card, slot, tier, enchant, size]) => ({
      id: snapshot.cards[card],
      name: '',
      enchant: snapshot.enchantments[enchant] ?? null,
      size,
      slot,
      tier: TIERS[tier - 1] ?? null
    }))
    .sort((a, b) => a.slot - b.slot);
}
export interface ReportInput {
  name: string;
  runs: Run[];
  from: number;
  before: number;
  names: Names;
  art: CardArt;
  skipped: number;
}
export function statsText(
  input: ReportInput,
  s = summarize(input.runs)
): string {
  return `${input.name} · ${span(input.from, input.before)}（北京时间）\n完成 ${s.runs} 局，十胜 ${s.tenWins}/${s.runs}，完美 ${s.perfect} 局，平均 ${n(s.meanWins)} 胜\nPvP ${s.wins}胜 ${s.losses}负（${pct(s.wins, s.wins + s.losses)}）${s.rating ? `\n分数 ${s.rating.start} → ${s.rating.end}（${signed(s.rating.delta)}）` : ''}${input.skipped ? `\n有 ${input.skipped} 份 Bundle 无法读取，以上是不完整统计。` : ''}`;
}
export const ROWS = 10;
export function statsHtml(input: ReportInput): string {
  const s = summarize(input.runs);
  const names = input.names;
  const last = input.runs.at(-1)!;
  const rank = s.rating
    ? stat(
        '传奇分数',
        signed(s.rating.delta),
        s.rating.delta >= 0 ? 'up' : 'down'
      )
    : stat('段位', RANKS[last.rank ?? ''] ?? '—');
  const head = `<div class="stats">${stat('对局', String(s.runs))}${stat('十胜', String(s.tenWins))}${stat('PvP 胜率', pct(s.wins, s.wins + s.losses))}${rank}</div>`;
  const rows = input.runs
    .toReversed()
    .slice(0, ROWS)
    .map((r) =>
      row(
        side(
          scored(r) ? String(r.wins) : '—',
          label(r.hero, names),
          resultColour(r)
        ),
        finalCards(r),
        names,
        input.art
      )
    )
    .join('');
  const notes = [
    input.runs.length > ROWS ? `显示最近 ${ROWS} 局` : '',
    input.skipped
      ? `有 ${input.skipped} 份 Bundle 无法读取，以上是不完整统计`
      : ''
  ].filter(Boolean);
  return page(
    input.name,
    `${span(input.from, input.before)} · 北京时间`,
    head,
    `${rows}${notes.length ? `<p class="note">${notes.join(' · ')}</p>` : ''}`
  );
}
export function heroHtml(
  hero: string,
  snapshot: HeroSnapshot,
  builds: BuildSnapshot | null,
  names: Names,
  art: CardArt
): string {
  const all = heroBaseline(snapshot, hero);
  const rate = (segment?: string) => {
    const s = segment ? heroBaseline(snapshot, hero, segment) : all;
    return pct(s.runs.ten_win, s.runs.completed);
  };
  const head = `<div class="stats">${stat('对局', String(all.runs.completed))}${stat('十胜率', rate())}${stat('传奇十胜率', rate('legend'))}${stat('非传奇十胜率', rate('non_legend'))}</div>`;
  const rows = (builds?.heroes[hero]?.builds.slice(0, 3) ?? [])
    .map((b) =>
      row(
        side(String(b[2][1] ?? '—'), '次十胜', RESULT[1]),
        layoutCards(builds!, b[1]),
        names,
        art
      )
    )
    .join('');
  return page(
    label(hero, names),
    `${snapshot.window.start} 至 ${snapshot.window.end.slice(5)} · UTC`,
    head,
    rows || '<p class="note">暂无热门十胜阵容</p>'
  );
}
