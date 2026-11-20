import aliases from '../../bazaarplusplus-analyzer/contracts/v6/hero-aliases.json';
import schema from '../../bazaarplusplus-analyzer/contracts/v6/heroes.schema.json';

export const HEROES: readonly string[] = schema.$defs.row.properties.hero.enum;
export const canonicalHero = (value: string): string =>
  (aliases.aliases as Record<string, string>)[value.trim()] ?? value.trim();
export interface Names {
  cards?: Record<string, string>;
  heroes: Record<string, string>;
  names: Record<string, string>;
}
export class UserError extends Error {}
export interface Command {
  kind: '帮助' | '状态' | '下载' | '英雄' | '绑定' | '解绑' | '战绩';
  name?: string;
  hero?: string;
}
export const HELP = `@我：查自己最近 3 天战绩
@我 绑定 游戏名：第一次先绑定
@我 战绩 游戏名：查别人
@我 英雄 瓦内莎：英雄数据
@我 下载 / 状态 / 解绑`;
function heroFor(value: string, names: Names): string | undefined {
  return HEROES.find(
    (hero) =>
      hero.toLowerCase() === canonicalHero(value).toLowerCase() ||
      names.heroes[hero] === value ||
      (hero === 'TheDragons' && /^(双龙|the dragons)$/i.test(value))
  );
}
const unquote = (value: string) => value.replace(/^["“](.*)["”]$/, '$1');
export function parseCommand(content: string, names: Names): Command {
  const text = content
    .replace(/^\s*<@!?[^>]+>\s*/, '')
    .trim()
    .replace(/^\//, '');
  if (!text) return { kind: '战绩' };
  const firstSpace = text.search(/\s/);
  const verb = firstSpace < 0 ? text : text.slice(0, firstSpace);
  const rest = firstSpace < 0 ? '' : text.slice(firstSpace).trim();
  if (!['帮助', '状态', '下载', '英雄', '绑定', '解绑', '战绩'].includes(verb))
    throw new UserError(HELP);
  const kind = verb as Command['kind'];
  if (kind === '绑定') {
    if (!rest) throw new UserError('发送「绑定 游戏名」，例如：绑定 风车小镇');
    return { kind, name: unquote(rest) };
  }
  if (kind === '英雄') {
    const hero = heroFor(rest, names);
    if (!hero)
      throw new UserError(
        `请选择英雄：${HEROES.map((h) => names.heroes[h] ?? h).join('、')}`
      );
    return { kind, hero };
  }
  if (kind === '战绩') return rest ? { kind, name: unquote(rest) } : { kind };
  if (rest) throw new UserError(`「${kind}」不需要参数。`);
  return { kind };
}
export const DAY = 86_400_000;
export function queryWindow(now: number) {
  const today = Math.floor((now + DAY / 3) / DAY) * DAY - DAY / 3;
  return { from: today - 2 * DAY, before: now };
}
export function beijingDate(value: number): string {
  return new Date(value + DAY / 3).toISOString().slice(0, 10);
}
