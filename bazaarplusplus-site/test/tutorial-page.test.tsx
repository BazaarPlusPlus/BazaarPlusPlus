import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';

import App from '../src/app/App';

describe('Tutorial route', () => {
  let originalUrl: string;

  beforeEach(() => {
    originalUrl = window.location.href;
  });

  afterEach(() => {
    window.history.replaceState({}, '', originalUrl);
    vi.restoreAllMocks();
  });

  test('renders the English mod overview and installation guide at /tutorial', async () => {
    window.history.replaceState({}, '', '/tutorial?lang=en');

    render(<App />);

    expect(
      screen.getByRole('heading', { level: 1, name: /BazaarPlusPlus Official Guide/ })
    ).toBeInTheDocument();
    expect(screen.getByText(/From installation to in-game combat controls/)).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 2, name: 'What the mod adds' })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Run History' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Anonymous Mode' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Legendary Rank Display' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Enchant and upgrade previews' })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Combat Controls' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Chinese Terminology' })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Open run history to review saved runs, combat replays, and ghost battles/)
    ).toBeInTheDocument();
    expect(screen.getByText(/Show the local player name as Anonymous/)).toBeInTheDocument();
    expect(
      screen.getByText(/Hide your local name in screenshots and recordings/)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Stay anonymous while streaming or sharing footage/)
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Toggle Anonymous Mode to hide the local player name/)
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/This matches the in-game Anonymous Mode entry/)
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(/Supports normal, privacy-friendly, high-power, and rank-plus-rating views/)
    ).toBeInTheDocument();
    expect(screen.queryByText(/Choose how your Legendary rank appears/)).not.toBeInTheDocument();
    expect(screen.getByText(/high-power, and rank-plus-rating views/)).toBeInTheDocument();
    expect(screen.getByText(/Your rank display updates immediately/)).toBeInTheDocument();
    expect(
      screen.getByText(/Preview how an item changes after enchantment or upgrade/)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Upgrade previews help evaluate shop buys, rewards, and build changes/)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Show combat time, pause state, and speed controls during fights/)
    ).toBeInTheDocument();
    expect(screen.getByText(/regional terminology you prefer/)).toBeInTheDocument();
    expect(
      screen.getByText(/Launch the game once so BazaarPlusPlus can finish setup/)
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /Codex button appears above the Settings button, and the game version text below shows BPP version/
      )
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: 'Lobby and personalization tools' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: 'Background upload and ghost battles' })
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 2, name: 'Installation Guide' })
    ).toBeInTheDocument();
    expect(screen.queryByText('Before you install')).not.toBeInTheDocument();
    expect(screen.getByText('Download latest installer')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Combat Hotkeys' })).toBeInTheDocument();
    expect(screen.getByText('Entry controls')).toBeInTheDocument();
    expect(screen.getByText('Preview holds')).toBeInTheDocument();
    expect(screen.getByText('Enchant preview')).toBeInTheDocument();
    expect(screen.getByText('Upgrade preview')).toBeInTheDocument();
    expect(screen.getByText('Ten-Win Build')).toBeInTheDocument();
    expect(screen.getByText('Codex')).toBeInTheDocument();
    expect(
      screen.getByText(/Rebind Ctrl \/ Shift preview hotkeys in Settings -> Gameplay Settings/)
    ).toBeInTheDocument();
    expect(screen.getByText('Caps Lock')).toBeInTheDocument();
    expect(screen.getByText('Tab')).toBeInTheDocument();
    expect(screen.queryByText('A / D')).not.toBeInTheDocument();
    expect(screen.queryByText('W / S')).not.toBeInTheDocument();
    expect(screen.getByText(/Open the codex/)).toBeInTheDocument();
    expect(screen.queryByText('Switch view')).not.toBeInTheDocument();
    expect(screen.queryByText('Switch display')).not.toBeInTheDocument();
    expect(screen.queryByText('Switch build')).not.toBeInTheDocument();
    expect(document.body).toHaveTextContent(/Compare current set with Ten-Win builds/);
    expect(
      screen.queryByText(/Compare the selected set with Ten-Win builds/)
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Open build view and compare the selected set with Ten-Win builds/)
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(
        /Open build view, then switch between the selected set, Ten-Win Build, and matched builds/
      )
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Switch between the selected set, Ten-Win Build, and matched candidates/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/W \/ S browses candidates/)).not.toBeInTheDocument();
    expect(screen.queryByText(/W \/ S.*Settings/)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/press 2 to view Ten-Win Build recommendations/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/Up \/ Down/)).not.toBeInTheDocument();
    expect(screen.queryByText('Settings dock')).not.toBeInTheDocument();
    expect(screen.queryByText(/processed frames/)).not.toBeInTheDocument();
    expect(screen.queryByText(/native game flow/)).not.toBeInTheDocument();
    expect(screen.queryByText(/This matches the in-game/)).not.toBeInTheDocument();
    expect(screen.queryByText(/settings dock/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: 'Game History' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: 'Legendary Position' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: 'Combat Status Bar' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: 'Chinese Locale' })
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/default, hidden, fixed-number/)).not.toBeInTheDocument();
    expect(screen.queryByText(/position-plus-rating modes/)).not.toBeInTheDocument();
    expect(screen.queryByText(/tooltip workflow/)).not.toBeInTheDocument();
    expect(screen.queryByText(/hold Ctrl for enchant previews/)).not.toBeInTheDocument();
    expect(screen.queryByText(/local SQLite/)).not.toBeInTheDocument();
    expect(screen.queryByText(/HistoryPanel/)).not.toBeInTheDocument();
    expect(screen.queryByText(/BepInEx\/config/)).not.toBeInTheDocument();
    expect(screen.queryByText(/runtime dependencies/)).not.toBeInTheDocument();
    expect(screen.queryByText(/BepInEx mod for The Bazaar/)).not.toBeInTheDocument();
    expect(screen.queryByText(/current mod repository/)).not.toBeInTheDocument();
    expect(screen.queryByText(/BepInEx 5 and The Bazaar/)).not.toBeInTheDocument();
    expect(
      screen.queryByText('BepInEx 5 is installed in the game directory.')
    ).not.toBeInTheDocument();
    expect(screen.queryByText('Advanced manual install')).not.toBeInTheDocument();
    expect(screen.queryByText(/release-page instructions/)).not.toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 3 })).toHaveLength(15);
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Data and network behavior' })
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download Now' })).toHaveAttribute(
      'href',
      '/download?lang=en'
    );
    expect(screen.getByRole('link', { name: 'Support the Project' })).toHaveAttribute(
      'href',
      '/support?lang=en'
    );
    expect(document.title).toBe('Tutorial | BazaarPlusPlus');
  });

  test('renders localized Chinese tutorial copy', () => {
    window.history.replaceState({}, '', '/tutorial');

    render(<App />);

    expect(
      screen.getByRole('heading', { level: 1, name: /BazaarPlusPlus 官方指南/ })
    ).toBeInTheDocument();
    expect(screen.getByText(/从下载安装到实战技巧/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: '模组主要功能' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: '对局历史' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: '匿名模式' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: '传奇名次显示' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: '附魔与升级预览' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: '战斗状态栏' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: '中文模式' })).toBeInTheDocument();
    const runHistoryCard = screen
      .getByRole('heading', { level: 3, name: '对局历史' })
      .closest('article');
    expect(runHistoryCard).not.toBeNull();
    const runHistory = within(runHistoryCard as HTMLElement);
    expect(
      runHistory.getByText(/打开对局历史，回看对局记录、战斗回放和幽灵对战/)
    ).toBeInTheDocument();
    expect(runHistory.getByText(/快速找回刚结束的对局和关键战斗/)).toBeInTheDocument();
    expect(screen.getByText(/在本地隐藏真实玩家昵称，保护隐私/)).toBeInTheDocument();
    expect(screen.getByText(/截图和录制时隐藏本地名称/)).toBeInTheDocument();
    expect(screen.getByText(/直播或分享画面时保持匿名展示/)).toBeInTheDocument();
    expect(screen.queryByText(/截图、录制或直播时隐藏名称/)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/本地玩家名显示为 Anonymous\n截图、录制或直播时隐藏名称/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/开启匿名模式后/)).not.toBeInTheDocument();
    expect(screen.queryByText(/适合截图、录制或直播时隐藏本地名称/)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/入口对应游戏内的 Anonymous Mode \/ 匿名模式/)
    ).not.toBeInTheDocument();
    expect(screen.getByText(/支持默认、无人知晓、战力爆表和双显模式/)).toBeInTheDocument();
    expect(
      screen.queryByText(/调整传说段位排名显示，支持默认、无人知晓、战力爆表和双显模式/)
    ).not.toBeInTheDocument();
    expect(screen.getByText(/可按需要隐藏名次、夸张展示战力/)).toBeInTheDocument();
    expect(screen.queryByText(/调整 Legendary 排名相关显示/)).not.toBeInTheDocument();
    expect(screen.getByText(/查看物品时提前看见附魔或升级后的变化/)).toBeInTheDocument();
    expect(
      screen.getByText(/将附魔后的效果直接整合进物品提示，方便横向对比不同选择/)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/升级预览适合在选择遭遇，调整构筑时，确认升级后的收益/)
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/附魔预览会把附魔后的数值与效果补进物品提示/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/购物、选奖励/)).not.toBeInTheDocument();
    expect(screen.queryByText(/快速确认升级后的收益/)).not.toBeInTheDocument();
    expect(screen.getByText(/在战斗中查看时间、暂停状态和速度控制/)).toBeInTheDocument();
    expect(screen.getByText(/按你的常用地区术语显示 BazaarPlusPlus 中文界面/)).toBeInTheDocument();
    expect(screen.getByText(/支持简体中文、台湾繁体和香港繁体的术语习惯/)).toBeInTheDocument();
    expect(screen.getByText(/让 BazaarPlusPlus 完成初始化/)).toBeInTheDocument();
    expect(screen.getByText(/设置按钮上方会出现图鉴按钮/)).toBeInTheDocument();
    expect(screen.getByText(/下方游戏版本信息会显示 BPP version/)).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: '大厅与个性化设置' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 3, name: '后台上传与 Ghost Battles' })
    ).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: '下载与安装指南' })).toBeInTheDocument();
    expect(screen.queryByText('安装前确认')).not.toBeInTheDocument();
    expect(screen.getByText('下载最新安装器')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: '实战快捷键' })).toBeInTheDocument();
    expect(
      screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)
    ).toEqual(['模组主要功能', '实战快捷键', '下载与安装指南']);
    expect(screen.getByText('快捷入口')).toBeInTheDocument();
    expect(screen.getByText('按住预览')).toBeInTheDocument();
    expect(screen.getByText('附魔预览')).toBeInTheDocument();
    expect(screen.getByText('升级预览')).toBeInTheDocument();
    expect(screen.getByText(/查看物品附魔后的效果/)).toBeInTheDocument();
    expect(screen.getByText(/未始终显示时按住/)).toBeInTheDocument();
    expect(screen.getByText(/查看升级后的数值变化/)).toBeInTheDocument();
    expect(screen.getByText(/悬停物品时按住/)).toBeInTheDocument();
    expect(
      screen.queryByText(/未始终显示时按住，查看物品附魔后的提示变化/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/悬停物品时按住，查看升级后的数值和效果/)).not.toBeInTheDocument();
    expect(screen.getByText('十胜阵容')).toBeInTheDocument();
    expect(screen.getAllByText('图鉴')).toHaveLength(2);
    expect(
      screen.getByText(/在游戏“设置” -> “游玩设置”中重绑 Ctrl \/ Shift 预览键位/)
    ).toBeInTheDocument();
    expect(screen.getByText('Caps Lock')).toBeInTheDocument();
    expect(screen.getByText('Tab')).toBeInTheDocument();
    expect(screen.queryByText('A / D')).not.toBeInTheDocument();
    expect(screen.queryByText('W / S')).not.toBeInTheDocument();
    expect(screen.getAllByText('打开')).toHaveLength(2);
    expect(screen.queryByText('切换视图')).not.toBeInTheDocument();
    expect(screen.queryByText('切换展示')).not.toBeInTheDocument();
    expect(screen.queryByText('切换阵容')).not.toBeInTheDocument();
    expect(screen.getByText(/对比当前卡组与十胜阵容/)).toBeInTheDocument();
    expect(screen.getByText('打开图鉴')).toBeInTheDocument();
    expect(screen.queryByText(/打开阵容视图，对比当前卡组与十胜阵容/)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/打开阵容视图后，切换当前卡组、十胜阵容和候选阵容/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/切换当前卡组、十胜阵容并浏览候选/)).not.toBeInTheDocument();
    expect(screen.queryByText(/W \/ S 用于浏览候选/)).not.toBeInTheDocument();
    expect(screen.queryByText(/W \/ S.*设置/)).not.toBeInTheDocument();
    expect(screen.queryByText(/再按 2 查看十胜阵容位置/)).not.toBeInTheDocument();
    expect(screen.queryByText(/↑ \/ ↓/)).not.toBeInTheDocument();
    expect(screen.queryByText('设置坞')).not.toBeInTheDocument();
    expect(screen.queryByText(/设置坞/)).not.toBeInTheDocument();
    expect(screen.queryByText(/入口对应游戏内/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Game History \/ 对局历史/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Legendary Position \/ 传奇名次显示/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Combat Status Bar \/ 战斗状态栏/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Chinese Locale \/ 中文模式/)).not.toBeInTheDocument();
    expect(screen.queryByText(/runs/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Ghost 回放/)).not.toBeInTheDocument();
    expect(screen.queryByText(/CN、TW 和 HK/)).not.toBeInTheDocument();
    expect(screen.queryByText(/已处理帧数/)).not.toBeInTheDocument();
    expect(screen.queryByText(/tooltip 路径/)).not.toBeInTheDocument();
    expect(screen.queryByText(/提示工作流/)).not.toBeInTheDocument();
    expect(screen.queryByText(/按住 Ctrl 查看附魔预览/)).not.toBeInTheDocument();
    expect(screen.queryByText(/本地 SQLite/)).not.toBeInTheDocument();
    expect(screen.queryByText(/HistoryPanel/)).not.toBeInTheDocument();
    expect(screen.queryByText(/BepInEx\/config/)).not.toBeInTheDocument();
    expect(screen.queryByText(/运行时依赖/)).not.toBeInTheDocument();
    expect(screen.queryByText(/面向《The Bazaar》的 BepInEx 模组/)).not.toBeInTheDocument();
    expect(screen.queryByText(/当前 BazaarPlusPlus 模组仓库/)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/请先确认已经安装《The Bazaar》和 BepInEx 5/)
    ).not.toBeInTheDocument();
    expect(screen.queryByText('游戏目录中已经安装 BepInEx 5')).not.toBeInTheDocument();
    expect(screen.queryByText('高级手动安装')).not.toBeInTheDocument();
    expect(screen.queryByText(/发布页的手动安装说明/)).not.toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 3 })).toHaveLength(15);
    expect(
      screen.queryByRole('heading', { level: 2, name: '数据与网络行为' })
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: '立即下载' })).toHaveAttribute('href', '/download');
    expect(screen.getByRole('link', { name: '赞助项目' })).toHaveAttribute('href', '/support');
  });
});
