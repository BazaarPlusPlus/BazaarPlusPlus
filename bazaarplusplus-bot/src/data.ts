import { UserError, type Names } from './commands';
import type { Env } from './env';
import type { BuildSnapshot, BundleRef, HeroSnapshot } from './model';

const CDN = 'https://cdn.bazaarplusplus.com';
export async function json<T>(
  url: string,
  signal: AbortSignal,
  token?: string
): Promise<T> {
  const response = await fetch(url, {
    signal,
    headers: token ? { Authorization: `Bearer ${token}` } : undefined
  });
  if (!response.ok) throw new Error(`Data request failed (${response.status})`);
  return response.json<T>();
}
export const names = (signal: AbortSignal) =>
  json<Names>(`${CDN}/mod-data/zh-names/zh-names.json`, signal);
export const heroes = (signal: AbortSignal) =>
  json<HeroSnapshot>(`${CDN}/metrics/v6/heroes/latest.json`, signal);
export type CardArt = Record<string, string>;
export const CARD_ART = `${CDN}/card-art`;
export const cardArt = (signal: AbortSignal) =>
  json<{ cards: CardArt }>(`${CARD_ART}/index.json`, signal).then(
    (index) => index.cards
  );
export const builds = (signal: AbortSignal) =>
  json<BuildSnapshot>(`${CDN}/metrics/v6/builds/latest.json`, signal);
interface Player {
  account_id: string;
  display_name: string;
}
export interface BundlePage {
  window: { stored_from_ms: number; stored_before_ms: number };
  items: BundleRef[];
  next_after: { stored_at_ms: number; bundle_id: string } | null;
}
export class Data {
  constructor(
    private readonly env: Env,
    private readonly signal: AbortSignal
  ) {}
  private get<T>(path: string, parameters: Record<string, string | number>) {
    const url = new URL(path, this.env.SERVER_ORIGIN);
    for (const [k, v] of Object.entries(parameters))
      url.searchParams.set(k, String(v));
    return json<T>(url.href, this.signal, this.env.BOT_STATS_TOKEN);
  }
  page(
    account: string,
    from: number,
    before: number,
    cursor?: BundlePage['next_after']
  ) {
    return this.get<BundlePage>('/bot/bundles', {
      account_id: account,
      stored_from_ms: from,
      stored_before_ms: before,
      ...(cursor
        ? {
            after_stored_at_ms: cursor.stored_at_ms,
            after_bundle_id: cursor.bundle_id
          }
        : {})
    });
  }
  async bind(
    member: string,
    name: string,
    from: number,
    before: number
  ): Promise<string> {
    let player: Player;
    if (name.startsWith('account:')) {
      const id = name.slice(8);
      if (!/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/.test(id))
        throw new UserError('游戏账号 ID 格式不正确。');
      if (!(await this.page(id, from, before)).items.length)
        throw new UserError('这个账号最近3天没有上传记录，请先完成一局。');
      player = { account_id: id, display_name: name };
    } else {
      const found = await this.get<{ players: Player[]; has_more: boolean }>(
        '/bot/players',
        { name }
      );
      if (!found.players.length)
        throw new UserError(
          '最近3天没有找到这个游戏名。请确认拼写并完成一局；名字索引未收录时，可用「绑定 account:游戏账号ID」。'
        );
      if (found.players.length > 1 || found.has_more)
        throw new UserError(
          `有多个同名账号，请用「绑定 account:账号ID」选择：\n${found.players.map((p) => p.account_id).join('\n')}`
        );
      player = found.players[0];
    }
    await this.env.DB.prepare(
      'INSERT INTO bindings(member_openid, account_id, display_name) VALUES (?1, ?2, ?3) ON CONFLICT(member_openid) DO UPDATE SET account_id=excluded.account_id, display_name=excluded.display_name'
    )
      .bind(member, player.account_id, player.display_name)
      .run();
    return `已绑定 ${player.display_name}。其他人现在可以查询这个账号。发送「战绩」查看，发送「解绑」解除。`;
  }
  async unbind(member: string): Promise<string> {
    await this.env.DB.prepare('DELETE FROM bindings WHERE member_openid=?1')
      .bind(member)
      .run();
    return '已解除你的绑定。';
  }
  async resolve(member: string, name?: string): Promise<Player> {
    if (!name) {
      const player = await this.env.DB.prepare(
        'SELECT account_id, display_name FROM bindings WHERE member_openid=?1'
      )
        .bind(member)
        .first<Player>();
      if (!player)
        throw new UserError(
          '先 @我 发送「绑定 游戏名」，之后 @我 就能查战绩。'
        );
      return player;
    }
    let candidates: Player[];
    if (name.startsWith('account:'))
      candidates = [{ account_id: name.slice(8), display_name: name }];
    else {
      const recent = await this.get<{ players: Player[] }>('/bot/players', {
        name
      });
      const saved = await this.env.DB.prepare(
        'SELECT DISTINCT account_id, display_name FROM bindings WHERE display_name=?1'
      )
        .bind(name)
        .all<Player>();
      candidates = [
        ...new Map(
          [...saved.results, ...recent.players].map((p) => [p.account_id, p])
        ).values()
      ];
    }
    const visible: Player[] = [];
    for (const p of candidates)
      if (
        await this.env.DB.prepare(
          'SELECT 1 FROM bindings WHERE account_id=?1 LIMIT 1'
        )
          .bind(p.account_id)
          .first()
      )
        visible.push(p);
    if (!visible.length) throw new UserError('这个玩家没有绑定，不能查询。');
    if (visible.length > 1)
      throw new UserError(
        `有多个已绑定的同名玩家，请使用「战绩 account:账号ID」：\n${visible.map((p) => p.account_id).join('\n')}`
      );
    return visible[0];
  }
}
