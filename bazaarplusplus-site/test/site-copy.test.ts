import { describe, expect, test } from 'vitest';

import { BAZAARDB_META_URL, getPageTitle, getSiteCopy } from '../src/content/site-copy';

function collectStrings(value: unknown): string[] {
  if (typeof value === 'string') {
    return [value];
  }

  if (Array.isArray(value)) {
    return value.flatMap(collectStrings);
  }

  if (value && typeof value === 'object') {
    return Object.values(value).flatMap(collectStrings);
  }

  return [];
}

describe('site copy', () => {
  test('builds localized browser tab titles for routed pages', () => {
    expect(getPageTitle('heroes', 'en')).toBe('Hero Stats | BazaarPlusPlus');
    expect(getPageTitle('tutorial', 'en')).toBe('Tutorial | BazaarPlusPlus');
    expect(getPageTitle('download', 'en')).toBe('Download | BazaarPlusPlus');
    expect(getPageTitle('support', 'zh')).toBe('支持 | BazaarPlusPlus');
    expect(getPageTitle('not-found', 'en')).toBe('Page Not Found | BazaarPlusPlus');
  });

  test('exposes header brand copy from localized site copy', () => {
    expect(getSiteCopy('zh').common.brand).toEqual({
      name: 'BazaarPlusPlus',
      subtitle: 'Bazaar Almanac',
    });
    expect(getSiteCopy('en').common.brand).toEqual({
      name: 'BazaarPlusPlus',
      subtitle: 'Bazaar Almanac',
    });
  });

  test('provides localized shared dashboard copy', () => {
    const zhCopy = getSiteCopy('zh');
    const enCopy = getSiteCopy('en');

    expect(zhCopy.common.footer).toEqual(enCopy.common.footer);
    expect(zhCopy.common.loading).toEqual({
      title: '正在准备英雄统计',
      body: '正在读取最新数据快照，请稍候。',
      progressAriaLabel: '正在加载英雄统计',
    });
    expect(enCopy.common.loading).toEqual({
      title: 'Preparing hero stats',
      body: 'Reading the latest data snapshot. This should only take a moment.',
      progressAriaLabel: 'Loading hero stats',
    });
    expect(zhCopy.common.scope.segmentLabels.legend).toBe('传奇');
    expect(zhCopy.secondaryNav.tutorial).toBe('教程');
    expect(zhCopy.tutorial.intro).toBe(
      '从下载安装到实战技巧，带你全面了解 BazaarPlusPlus 的各项核心功能，助你快速上手。'
    );
    expect(zhCopy.tutorial.installation.steps[0]?.title).toBe('下载最新安装器');
    expect(zhCopy.tutorial.installation.steps[3]?.description).toBe(
      '主菜单设置按钮上方会出现图鉴按钮；下方游戏版本信息会显示 BPP version'
    );
    expect(
      zhCopy.tutorial.features.find((feature) => feature.title === '附魔与升级预览')?.details
    ).toEqual([
      '将附魔后的效果直接整合进物品提示，方便横向对比不同选择',
      '升级预览适合在选择遭遇，调整构筑时，确认升级后的收益',
    ]);
    expect(zhCopy.tutorial.features.find((feature) => feature.title === '匿名模式')).toMatchObject({
      description: '在本地隐藏真实玩家昵称，保护隐私',
      details: ['截图和录制时隐藏本地名称', '直播或分享画面时保持匿名展示'],
    });
    expect(zhCopy.tutorial.quickStart.heading).toBe('实战快捷键');
    expect(zhCopy.tutorial.quickStart.groups.map((group) => group.title)).toEqual([
      '快捷入口',
      '按住预览',
    ]);
    expect(zhCopy.tutorial.quickStart.groups[0]?.items.map((item) => item.bindings)).toEqual([
      [{ key: 'F8' }],
      [{ key: 'Tab', action: '打开' }],
      [{ key: 'Caps Lock', action: '打开' }],
    ]);
    expect(zhCopy.tutorial.quickStart.groups[1]?.items.map((item) => item.bindings)).toEqual([
      [{ key: 'Ctrl' }],
      [{ key: 'Shift' }],
    ]);
    expect(zhCopy.tutorial.quickStart.groups[1]?.note).toBe(
      '在游戏“设置” -> “游玩设置”中重绑 Ctrl / Shift 预览键位'
    );

    expect(enCopy.common.scope.segmentLabels.non_legend).toBe('Non-Legend');
    expect(zhCopy.stats.heroes.eyebrow).toBe('BazaarPlusPlus');
    expect(enCopy.stats.heroes.eyebrow).toBe('Bazaar Almanac');
    expect(zhCopy.stats.heroes.title).toBe('英雄统计');
    expect(enCopy.stats.heroes.title).toBe('Hero stats');
    expect(zhCopy.stats.heroes.trend.winrateTrend).toBe('胜率走势与对位');
    expect(zhCopy.stats.heroes.trend.title).toBe('近期表现');
    expect(enCopy.stats.heroes.trend.title).toBe('Recent performance');
    expect(zhCopy.stats.heroes.snapshot.title).toBe('胜率榜单');
    expect(zhCopy.stats.heroes.detailLinkLabel).toBe('在 BazaarDB 查看详细统计');
    expect(enCopy.stats.heroes.detailLinkLabel).toBe('View detailed stats on BazaarDB');
    expect(enCopy.stats.heroes.tableHeaders.winRate).toBe('10W rate');
    expect(zhCopy.stats.heroes.matchups.title).toBe('对位胜率');
    expect(zhCopy.stats.heroes.matchups.selectedHeroLabel).toBe('当前英雄');
    expect(enCopy.stats.heroes.matchups.selectedHeroLabel).toBe('Selected hero');
    expect(zhCopy.stats.heroes.tableHeaders.misfortune).toBe('惨淡');
    expect(enCopy.stats.heroes.tableHeaders.misfortune).toBe('Misfortune');
    expect(enCopy.secondaryNav.tutorial).toBe('Tutorial');
    expect(enCopy.tutorial.intro).toBe(
      'From installation to in-game combat controls, master the core features of BazaarPlusPlus and elevate your gameplay.'
    );
    expect(enCopy.tutorial.installation.steps[3]?.description).toBe(
      'On the main menu, a Codex button appears above the Settings button, and the game version text below shows BPP version.'
    );
    expect(enCopy.tutorial.quickStart.heading).toBe('Combat Hotkeys');
    expect(enCopy.tutorial.quickStart.groups.map((group) => group.title)).toEqual([
      'Entry controls',
      'Preview holds',
    ]);
    expect(enCopy.tutorial.quickStart.groups[0]?.items.map((item) => item.bindings)).toEqual([
      [{ key: 'F8' }],
      [{ key: 'Tab', action: 'Open' }],
      [{ key: 'Caps Lock', action: 'Open' }],
    ]);
    expect(enCopy.tutorial.quickStart.groups[1]?.items.map((item) => item.bindings)).toEqual([
      [{ key: 'Ctrl' }],
      [{ key: 'Shift' }],
    ]);
    expect(enCopy.tutorial.quickStart.groups[1]?.note).toBe(
      'Rebind Ctrl / Shift preview hotkeys in Settings -> Gameplay Settings.'
    );
    expect(enCopy.tutorial.features.map((feature) => feature.title)).toEqual([
      'Card Collection',
      'BazaarDB Auto Upload',
      'Run History',
      'Combat Controls',
      'Anonymous Mode',
      'Legendary Rank Display',
      'Enchant and upgrade previews',
      'Chinese Terminology',
    ]);
    expect(
      enCopy.tutorial.features.find((feature) => feature.title === 'Anonymous Mode')
    ).toMatchObject({
      description: 'Show the local player name as Anonymous.',
      details: [
        'Hide your local name in screenshots and recordings.',
        'Stay anonymous while streaming or sharing footage.',
      ],
    });
  });

  test('uses Chinese punctuation in Chinese copy', () => {
    const strings = collectStrings(getSiteCopy('zh'));
    const stringsWithAsciiCommas = strings.filter((value) => value.includes(','));

    expect(stringsWithAsciiCommas).toEqual([]);
  });

  test('marks BazaarDB outbound links with BazaarPlusPlus UTM source', () => {
    expect(BAZAARDB_META_URL).toBe('https://bazaardb.gg/run/meta?utm_source=bazaarplusplus');
  });

  test('keeps WeChat localized and Ko-fi global in Chinese support copy', () => {
    const zhCopy = getSiteCopy('zh');

    expect(zhCopy.support.wechat.description).toBe('微信扫码，请 BazaarPlusPlus 喝一杯。');
    expect(zhCopy.support.wechat.modalTitle).toBe('请喝一杯');
    expect(zhCopy.support.kofi.description).toBe('Buy BazaarPlusPlus a drink on Ko-fi.');
    expect(zhCopy.support.kofi.actionLabel).toBe('Open Ko-fi');
    expect(zhCopy.support.kofi.regionLabel).toBe('Global');
  });
});
