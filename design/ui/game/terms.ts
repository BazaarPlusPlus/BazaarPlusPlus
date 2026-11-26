import type { CardDetailLabels } from './CardDetail';
import type { CardTier, Enchantment } from './types';

export type GameLocale = 'en' | 'zh';

/** The game's words both products show: card terms and the Run replay's labels. */
export type GameTerms = {
  tiers: Record<CardTier, string>;
  enchantments: Record<Enchantment, string>;
  card: CardDetailLabels & { close: string };
  day: string;
  pvp: string;
  win: string;
  loss: string;
  skills: string;
  loading: string;
  replay: {
    decisions: string;
    board: string;
    stash: string;
    unknown: string;
    empty: string;
    pickedFrom: string;
    chosen: string;
    goldTotal: string;
    healthTotal: string;
    level: string;
    resultOffer: string;
    previous: string;
    next: string;
    unknownDay: string;
    hour: string;
    visited: string;
    fought: string;
    noVisits: string;
    moves: string;
    gold: string;
    net: string;
    health: string;
    attempted: string;
    noResponse: string;
    undecoded: string;
    error: string;
    frame: string;
    rawFrames: string;
    downloadBytes: string;
    downloadAll: string;
    kinds: Record<
      | 'shop'
      | 'event'
      | 'monster'
      | 'pvp'
      | 'pedestal'
      | 'encounter'
      | 'activity'
      | 'levelUp'
      | 'loot',
      string
    >;
    verbs: Record<
      | 'start'
      | 'reconnect'
      | 'chose'
      | 'bought'
      | 'picked'
      | 'sold'
      | 'rerolled'
      | 'moved'
      | 'skill'
      | 'upgraded'
      | 'enchanted'
      | 'committed'
      | 'left'
      | 'other',
      string
    >;
  };
};

export const gameTerms: Record<GameLocale, GameTerms> = {
  en: {
    tiers: {
      Bronze: 'Bronze',
      Silver: 'Silver',
      Gold: 'Gold',
      Diamond: 'Diamond',
      Legendary: 'Legendary'
    },
    enchantments: {
      Deadly: 'Deadly',
      Ethereal: 'Ethereal',
      Fiery: 'Fiery',
      Golden: 'Golden',
      Heavy: 'Heavy',
      Icy: 'Icy',
      Mystical: 'Mystical',
      Obsidian: 'Obsidian',
      Radiant: 'Radiant',
      Restorative: 'Restorative',
      Shielded: 'Shielded',
      Shiny: 'Shiny',
      Tiny: 'Tiny',
      Toxic: 'Toxic',
      Turbo: 'Turbo',
      Mossy: 'Mossy'
    },
    card: {
      close: 'Close',
      enchantments: 'Enchantments',
      allEnchantments: 'Show all enchantments',
      sizes: { Small: 'Small', Medium: 'Medium', Large: 'Large' },
      ammo: 'Ammo',
      cooldown: 'Cooldown',
      seconds: 's',
      unavailable: 'Unavailable'
    },
    day: 'Day {n}',
    pvp: 'PvP',
    win: 'Win',
    loss: 'Loss',
    skills: 'Skills',
    loading: 'Loading…',
    replay: {
      decisions: 'Run replay',
      board: 'Board',
      stash: 'Stash',
      unknown: 'State unavailable',
      empty: 'Empty',
      pickedFrom: 'Picked from',
      chosen: 'Chosen',
      goldTotal: 'Gold',
      healthTotal: 'Health',
      level: 'Level',
      resultOffer: 'Offered',
      previous: 'Previous',
      next: 'Next',
      unknownDay: 'Unknown day',
      hour: 'Hour {n}',
      visited: 'Visited {name}',
      fought: 'Fought {name}',
      noVisits: 'No decisions recorded for this run.',
      moves: 'Moved ×{n}',
      gold: '{n} gold',
      net: 'net {n} gold',
      health: '{n} health',
      attempted: 'Not applied',
      noResponse: 'No response',
      undecoded: 'Unreadable message',
      error: 'Couldn’t read this run’s message stream.',
      frame: 'Frame sequence',
      rawFrames: 'Raw frames',
      downloadBytes: 'Download bytes',
      downloadAll: 'Download all frames',
      kinds: {
        shop: 'Shop',
        event: 'Event',
        monster: 'Monster',
        pvp: 'PvP',
        pedestal: 'Pedestal',
        encounter: 'Encounter',
        activity: 'Between encounters',
        levelUp: 'Level up',
        loot: 'Loot'
      },
      verbs: {
        start: 'Started',
        reconnect: 'Reconnected',
        chose: 'Chose',
        bought: 'Bought',
        picked: 'Took',
        sold: 'Sold',
        rerolled: 'Rerolled',
        moved: 'Moved',
        skill: 'Learned',
        upgraded: 'Upgraded',
        enchanted: 'Enchanted',
        committed: 'Committed',
        left: 'Left',
        other: 'Command'
      }
    }
  },
  zh: {
    tiers: {
      Bronze: '青铜',
      Silver: '白银',
      Gold: '黄金',
      Diamond: '钻石',
      Legendary: '传奇'
    },
    enchantments: {
      Deadly: '致命',
      Ethereal: '空灵',
      Fiery: '炽焰',
      Golden: '黄金',
      Heavy: '沉重',
      Icy: '寒冰',
      Mystical: '神秘',
      Obsidian: '黑曜石',
      Radiant: '辉耀',
      Restorative: '回复',
      Shielded: '护盾',
      Shiny: '闪亮',
      Tiny: '迷你',
      Toxic: '毒素',
      Turbo: '疾速',
      Mossy: '长青'
    },
    card: {
      close: '关闭',
      enchantments: '附魔',
      allEnchantments: '显示全部附魔',
      sizes: { Small: '小型', Medium: '中型', Large: '大型' },
      ammo: '弹药',
      cooldown: '冷却',
      seconds: '秒',
      unavailable: '不可用'
    },
    day: '第 {n} 天',
    pvp: 'PvP',
    win: '胜',
    loss: '负',
    skills: '技能',
    loading: '正在读取…',
    replay: {
      decisions: '整局回放',
      board: '阵容',
      stash: '储物箱',
      unknown: '状态未知',
      empty: '空',
      pickedFrom: '选择范围',
      chosen: '已选',
      goldTotal: '金币',
      healthTotal: '生命',
      level: '等级',
      resultOffer: '备选',
      previous: '上一步',
      next: '下一步',
      unknownDay: '日期未知',
      hour: '第 {n} 小时',
      visited: '拜访 {name}',
      fought: '迎战 {name}',
      noVisits: '没有记录这一局的决策。',
      moves: '移动 {n} 次',
      gold: '{n} 金币',
      net: '净 {n} 金币',
      health: '{n} 生命',
      attempted: '未生效',
      noResponse: '无响应',
      undecoded: '无法读取的消息',
      error: '未能读取这局的消息流。',
      frame: '消息序号',
      rawFrames: '原始消息',
      downloadBytes: '下载字节',
      downloadAll: '下载全部消息',
      kinds: {
        shop: '商店',
        event: '事件',
        monster: '怪物',
        pvp: 'PvP',
        pedestal: '祭坛',
        encounter: '遭遇',
        activity: '遭遇之间',
        levelUp: '升级',
        loot: '战利品'
      },
      verbs: {
        start: '开局',
        reconnect: '重新连接',
        chose: '选择',
        bought: '购买',
        picked: '获得',
        sold: '出售',
        rerolled: '刷新',
        moved: '移动',
        skill: '学会',
        upgraded: '升级',
        enchanted: '附魔',
        committed: '提交',
        left: '离开',
        other: '指令'
      }
    }
  }
};

/** Fills `{name}` placeholders; an unknown one stays as written. */
export function fillTerm(
  template: string,
  values: Record<string, string | number>
): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    key in values ? String(values[key]) : match
  );
}
