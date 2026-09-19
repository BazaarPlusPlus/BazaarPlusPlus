import type { HeroMetricsSegment } from '../features/heroes/hero-metrics-dataset';
import type {
  Locale,
  PrimaryNavigationPage,
  SecondaryNavigationPage,
  SpaPage,
} from '../app/router';

type PrimaryNavCopy = Record<PrimaryNavigationPage, string>;

type SecondaryNavCopy = Record<SecondaryNavigationPage, string>;

type PageTitleKey = SpaPage;

type PageTitleCopy = Record<PageTitleKey, string>;

type ScopeCopy = {
  window: string;
  segment: string;
  hero: string;
  allHero: string;
  segmentLabels: Record<HeroMetricsSegment, string>;
};

type CommonCopy = {
  brand: {
    name: string;
    subtitle: string;
  };
  filters: string;
  metric: string;
  primaryNavAriaLabel: string;
  secondaryNavAriaLabel: string;
  languageLabel: string;
  homeAriaLabel: string;
  lastSync: string;
  footer: {
    madeWith: string;
    love: string;
    by: string;
  };
  loading: {
    title: string;
    body: string;
    progressAriaLabel: string;
  };
  error: {
    title: string;
    unknownMessage: string;
    backToHeroes: string;
  };
  notFound: {
    title: string;
    backToHeroes: string;
  };
  scope: ScopeCopy;
  noValueLabel: string;
};

type HeroStatsCopy = {
  eyebrow: string;
  title: string;
  detailLinkLabel: string;
  trend: {
    winrateTrend: string;
    title: string;
    chartAriaLabel: string;
  };
  snapshot: {
    label: string;
    noData: string;
    title: string;
  };
  tableHeaders: {
    hero: string;
    winRate: string;
    runs: string;
    share: string;
    wins10w: string;
    perfect: string;
    gold: string;
    silver: string;
    bronze: string;
    misfortune: string;
    runShare: string;
    avgDays: string;
  };
  matchups: {
    title: string;
    heroSelectorLabel: string;
    selectedHeroLabel: string;
    lowSampleTag: string;
    sample: string;
    empty: string;
  };
  heroClass: {
    nonCanonicalTag: string;
    nonCanonicalTooltip: string;
    chartExcludedFootnote: string;
  };
  coverage: {
    windowSuffix: string;
    syncedPrefix: string;
    someDaysUnavailable: string;
    noTrendValue: string;
  };
  unavailable: {
    title: string;
    body: string;
  };
  segmentEmpty: {
    title: string;
    body: string;
  };
};

type StatsCopy = {
  heroes: HeroStatsCopy;
};

export type DownloadPageCopy = {
  title: string;
  versionLabel: string;
  versionPending: string;
  versionUnavailable: string;
  versionFailed: string;
  releaseFallbackPrefix: string;
  releaseFallbackLink: string;
  mainlandButtonLabel: string;
  windows: {
    title: string;
    arch: string;
    actionLabel: string;
    mainlandActionLabel: string;
  };
  mac: {
    title: string;
    arch: string;
    actionLabel: string;
    mainlandActionLabel: string;
  };
  noteTitle: string;
  noteParagraphs: string[];
};

export type SupportPageCopy = {
  title: string;
  intro: string;
  closeLabel: string;
  wechat: {
    title: string;
    description: string;
    actionLabel: string;
    regionLabel: string;
    modalTitle: string;
    modalSubtitle: string;
    modalHint: string;
    qrAriaLabel: string;
  };
  kofi: {
    title: string;
    description: string;
    actionLabel: string;
    regionLabel: string;
  };
  supporters: {
    heading: string;
    intro: string;
    unnamedNote: string;
    emptyNote: string;
    errorNote: string;
  };
};

export type TutorialPageCopy = {
  title: string;
  intro: string;
  primaryActionLabel: string;
  secondaryActionLabel: string;
  featureHeading: string;
  features: Array<{
    title: string;
    description: string;
    details: string[];
  }>;
  installation: {
    heading: string;
    steps: Array<{
      title: string;
      description: string;
    }>;
  };
  quickStart: {
    heading: string;
    groups: Array<{
      title: string;
      description: string;
      note?: string;
      items: Array<{
        bindings: Array<{
          key: string;
          action?: string;
        }>;
        title: string;
        description: string;
      }>;
    }>;
  };
};

type LocalizedSiteCopy = {
  common: CommonCopy;
  primaryNav: PrimaryNavCopy;
  secondaryNav: SecondaryNavCopy;
  pageTitles: PageTitleCopy;
  stats: StatsCopy;
  tutorial: TutorialPageCopy;
  download: DownloadPageCopy;
  support: SupportPageCopy;
};

const zh: LocalizedSiteCopy = {
  common: {
    brand: {
      name: 'BazaarPlusPlus',
      subtitle: 'Bazaar Almanac',
    },
    filters: '筛选',
    metric: '指标',
    primaryNavAriaLabel: '主要导航',
    secondaryNavAriaLabel: '辅助导航',
    languageLabel: '语言',
    homeAriaLabel: 'BazaarPlusPlus 首页',
    lastSync: '最近同步',
    footer: {
      madeWith: 'Made with',
      love: 'love',
      by: 'by',
    },
    loading: {
      title: '正在准备英雄统计',
      body: '正在读取最新数据快照，请稍候。',
      progressAriaLabel: '正在加载英雄统计',
    },
    error: {
      title: '数据暂时不可用',
      unknownMessage: '未知错误',
      backToHeroes: '← 返回英雄概览',
    },
    notFound: {
      title: '页面不存在',
      backToHeroes: '← 返回英雄概览',
    },
    scope: {
      window: '时间窗口',
      segment: '分段',
      hero: '英雄',
      allHero: '全部',
      segmentLabels: {
        all: '全部',
        legend: '传奇',
        non_legend: '非传奇',
      },
    },
    noValueLabel: '暂无数值',
  },
  primaryNav: {
    heroes: '统计',
  },
  secondaryNav: {
    tutorial: '教程',
    download: '下载',
    support: '支持',
  },
  pageTitles: {
    heroes: '英雄统计',
    tutorial: '教程',
    download: '下载',
    support: '支持',
    'not-found': '页面不存在',
  },
  stats: {
    heroes: {
      eyebrow: 'BazaarPlusPlus',
      title: '英雄统计',
      detailLinkLabel: '在 BazaarDB 查看详细统计',
      trend: {
        winrateTrend: '胜率走势与对位',
        title: '近期表现',
        chartAriaLabel: '英雄10胜率趋势图',
      },
      snapshot: {
        label: '当前快照',
        noData: '暂无数据',
        title: '胜率榜单',
      },
      tableHeaders: {
        hero: '英雄',
        winRate: '10胜率',
        runs: '完成局数',
        share: '占比',
        wins10w: '10胜',
        perfect: '完美',
        gold: '黄金',
        silver: '白银',
        bronze: '青铜',
        misfortune: '惨淡',
        runShare: '局数占比',
        avgDays: '平均天数',
      },
      matchups: {
        title: '对位胜率',
        heroSelectorLabel: '选择英雄查看对位',
        selectedHeroLabel: '当前英雄',
        lowSampleTag: '样本不足',
        sample: '场',
        empty: '暂无对位数据',
      },
      heroClass: {
        nonCanonicalTag: '非标准',
        nonCanonicalTooltip: '非标准英雄数据，可能来自特殊对局。',
        chartExcludedFootnote: '非标准英雄不在趋势图中显示。',
      },
      coverage: {
        windowSuffix: ' 窗口',
        syncedPrefix: '同步于 ',
        someDaysUnavailable: '部分日期暂不可用，当前结果只包含已加载日期。',
        noTrendValue: '部分日期没有可计算的趋势点。',
      },
      unavailable: {
        title: '快照暂不可用',
        body: '英雄指标快照暂不可用，请稍后刷新重试。',
      },
      segmentEmpty: {
        title: '该分段暂无数据',
        body: '当前窗口内此分段没有记录，试试其他分段。',
      },
    },
  },
  tutorial: {
    title: 'BazaarPlusPlus 官方指南',
    intro: '从下载安装到实战技巧，带你全面了解 BazaarPlusPlus 的各项核心功能，助你快速上手。',
    primaryActionLabel: '立即下载',
    secondaryActionLabel: '赞助项目',
    featureHeading: '模组主要功能',
    features: [
      {
        title: '图鉴',
        description: '在游戏中随时查阅所有物品与技能，支持多维度的深度检索',
        details: [
          '按英雄、品质、体型，甚至售卖商人进行精细筛选',
          '支持跟随当前游戏天数，显示当前阶段可获取的物品',
        ],
      },
      {
        title: 'BazaarDB 自动上传',
        description: '参与社区数据共建，在结算时自动上传通关截图与阵容数据',
        details: [
          '上传完全在后台进行，且仅在非战斗状态执行，确保游戏流畅',
          '此功能默认关闭，可在设置中手动开启，贡献你的构筑数据',
        ],
      },
      {
        title: '对局历史',
        description: '打开对局历史，回看对局记录、战斗回放和幽灵对战',
        details: [
          '快速找回刚结束的对局和关键战斗',
          '可查看本地保存的战斗，也能继续打开已下载的回放',
        ],
      },
      {
        title: '战斗状态栏',
        description: '在战斗中查看时间、暂停状态和速度控制',
        details: [
          '适合复盘战斗节奏，或在录制时控制播放',
          '速度按钮会在 0.50、0.67 和 1.00 倍之间切换',
        ],
      },
      {
        title: '匿名模式',
        description: '在本地隐藏真实玩家昵称，保护隐私',
        details: ['截图和录制时隐藏本地名称', '直播或分享画面时保持匿名展示'],
      },
      {
        title: '传奇名次显示',
        description: '支持默认、无人知晓、战力爆表和双显模式',
        details: [
          '可按需要隐藏名次、夸张展示战力，或同时显示名次与分数',
          '切换后会刷新当前可见的传说段位排名文本',
        ],
      },
      {
        title: '附魔与升级预览',
        description: '查看物品时提前看见附魔或升级后的变化，减少临场判断成本',
        details: [
          '将附魔后的效果直接整合进物品提示，方便横向对比不同选择',
          '升级预览适合在选择遭遇，调整构筑时，确认升级后的收益',
        ],
      },
      {
        title: '中文模式',
        description: '按你的常用地区术语显示 BazaarPlusPlus 中文界面',
        details: [
          '支持简体中文、台湾繁体和香港繁体的术语习惯',
          '对局历史、设置和提示文案会一起使用对应风格',
        ],
      },
    ],
    installation: {
      heading: '下载与安装指南',
      steps: [
        {
          title: '下载最新安装器',
          description: '进入下载页，按系统选择 Windows .exe 或 macOS .dmg',
        },
        {
          title: '运行安装器',
          description: '请在关闭游戏后运行安装程序。如需更新，建议先卸载旧版本。',
        },
        {
          title: '启动游戏完成初始化',
          description: '安装完成后启动游戏一次，让 BazaarPlusPlus 完成初始化',
        },
        {
          title: '确认模组生效',
          description: '主菜单设置按钮上方会出现图鉴按钮；下方游戏版本信息会显示 BPP version',
        },
      ],
    },
    quickStart: {
      heading: '实战快捷键',
      groups: [
        {
          title: '快捷入口',
          description: '打开面板或进入阵容视图',
          items: [
            {
              bindings: [{ key: 'F8' }],
              title: '历史记录',
              description: '打开对局历史，回看对局记录、战斗回放和幽灵对战',
            },
            {
              bindings: [{ key: 'Tab', action: '打开' }],
              title: '图鉴',
              description: '打开图鉴',
            },
            {
              bindings: [{ key: 'Caps Lock', action: '打开' }],
              title: '十胜阵容',
              description: '对比当前卡组与十胜阵容',
            },
          ],
        },
        {
          title: '按住预览',
          description: '查看物品提示时临时展开额外信息',
          note: '在游戏“设置” -> “游玩设置”中重绑 Ctrl / Shift 预览键位',
          items: [
            {
              bindings: [{ key: 'Ctrl' }],
              title: '附魔预览',
              description: '查看物品附魔后的效果\n未始终显示时按住',
            },
            {
              bindings: [{ key: 'Shift' }],
              title: '升级预览',
              description: '查看升级后的数值变化\n悬停物品时按住',
            },
          ],
        },
      ],
    },
  },
  download: {
    title: '下载 BazaarPlusPlus',
    versionLabel: '最新版本',
    versionPending: '正在获取最新版本…',
    versionUnavailable: '获取失败',
    versionFailed: '暂时无法获取最新版本',
    releaseFallbackPrefix: '可以前往 ',
    releaseFallbackLink: 'GitHub 发布页',
    mainlandButtonLabel: '大陆渠道',
    windows: {
      title: 'Windows',
      arch: 'x64 · Intel / AMD',
      actionLabel: '下载 .exe',
      mainlandActionLabel: 'Windows 大陆渠道',
    },
    mac: {
      title: 'macOS',
      arch: 'Apple Silicon · arm64',
      actionLabel: '下载 .dmg',
      mainlandActionLabel: 'macOS 大陆渠道',
    },
    noteTitle: '安装提示',
    noteParagraphs: [
      '建议先退出游戏再安装。更新时建议先卸载旧版本，再安装新版本',
      '如果安装后未生效，或被安全软件拦截，可以参考故障排查说明，或重新安装游戏与 BazaarPlusPlus',
    ],
  },
  support: {
    title: '支持 BazaarPlusPlus',
    intro: '你的支持会成为 BazaarPlusPlus 持续更新的动力。',
    closeLabel: '关闭',
    wechat: {
      title: '微信赞赏',
      description: '微信扫码，请 BazaarPlusPlus 喝一杯。',
      actionLabel: '查看赞赏码',
      regionLabel: '中国大陆',
      modalTitle: '请喝一杯',
      modalSubtitle: '微信赞赏',
      modalHint: '如果希望出现在支持者名单中，可以在备注里留下你的 ID。',
      qrAriaLabel: '微信赞赏二维码',
    },
    kofi: {
      title: 'Ko-fi',
      description: 'Buy BazaarPlusPlus a drink on Ko-fi.',
      actionLabel: 'Open Ko-fi',
      regionLabel: 'Global',
    },
    supporters: {
      heading: '支持者名单',
      intro: '感谢每一位支持 BazaarPlusPlus 的朋友。',
      unnamedNote: '也感谢所有未署名的支持者。',
      emptyNote: '支持者名单还在整理中，请稍后再来看看。',
      errorNote: '暂时无法加载支持者名单，请稍后再试。',
    },
  },
};

const en: LocalizedSiteCopy = {
  common: {
    brand: {
      name: 'BazaarPlusPlus',
      subtitle: 'Bazaar Almanac',
    },
    filters: 'Filters',
    metric: 'Metric',
    primaryNavAriaLabel: 'Primary',
    secondaryNavAriaLabel: 'Secondary',
    languageLabel: 'Language',
    homeAriaLabel: 'BazaarPlusPlus home',
    lastSync: 'last sync',
    footer: {
      madeWith: 'Made with',
      love: 'love',
      by: 'by',
    },
    loading: {
      title: 'Preparing hero stats',
      body: 'Reading the latest data snapshot. This should only take a moment.',
      progressAriaLabel: 'Loading hero stats',
    },
    error: {
      title: 'Stats are temporarily unavailable',
      unknownMessage: 'Unknown error',
      backToHeroes: '← Back to hero overview',
    },
    notFound: {
      title: 'Page not found',
      backToHeroes: '← Back to hero overview',
    },
    scope: {
      window: 'Window',
      segment: 'Segment',
      hero: 'Hero',
      allHero: 'All',
      segmentLabels: {
        all: 'All',
        legend: 'Legend',
        non_legend: 'Non-Legend',
      },
    },
    noValueLabel: 'No value',
  },
  primaryNav: {
    heroes: 'Stats',
  },
  secondaryNav: {
    tutorial: 'Tutorial',
    download: 'Download',
    support: 'Support',
  },
  pageTitles: {
    heroes: 'Hero Stats',
    tutorial: 'Tutorial',
    download: 'Download',
    support: 'Support',
    'not-found': 'Page Not Found',
  },
  stats: {
    heroes: {
      eyebrow: 'Bazaar Almanac',
      title: 'Hero stats',
      detailLinkLabel: 'View detailed stats on BazaarDB',
      trend: {
        winrateTrend: 'win rate and matchups',
        title: 'Recent performance',
        chartAriaLabel: 'Hero 10-win rate chart',
      },
      snapshot: {
        label: 'Snapshot',
        noData: 'No data',
        title: 'Win-rate ledger',
      },
      tableHeaders: {
        hero: 'Hero',
        winRate: '10W rate',
        runs: 'Runs',
        share: 'Share',
        wins10w: '10W wins',
        perfect: 'Perfect',
        gold: 'Gold',
        silver: 'Silver',
        bronze: 'Bronze',
        misfortune: 'Misfortune',
        runShare: 'Run share',
        avgDays: 'Avg days',
      },
      matchups: {
        title: 'Matchups',
        heroSelectorLabel: 'Choose hero for matchups',
        selectedHeroLabel: 'Selected hero',
        lowSampleTag: 'low sample',
        sample: 'battles',
        empty: 'No matchup data yet',
      },
      heroClass: {
        nonCanonicalTag: 'Non-standard',
        nonCanonicalTooltip: 'Non-standard hero data, possibly from special runs.',
        chartExcludedFootnote: 'Non-standard heroes are excluded from the trend chart.',
      },
      coverage: {
        windowSuffix: ' window',
        syncedPrefix: 'synced ',
        someDaysUnavailable: 'Some days are unavailable; this view includes loaded days only.',
        noTrendValue: 'Some days have no calculable trend point.',
      },
      unavailable: {
        title: 'Snapshot unavailable',
        body: 'The hero metrics snapshot is unavailable. Refresh to try again.',
      },
      segmentEmpty: {
        title: 'No data in this segment',
        body: 'No runs were recorded for this segment in the window. Try another segment.',
      },
    },
  },
  tutorial: {
    title: 'BazaarPlusPlus Official Guide',
    intro:
      'From installation to in-game combat controls, master the core features of BazaarPlusPlus and elevate your gameplay.',
    primaryActionLabel: 'Download Now',
    secondaryActionLabel: 'Support the Project',
    featureHeading: 'What the mod adds',
    features: [
      {
        title: 'Card Collection',
        description:
          'Browse all items and skills in-game with powerful, multi-dimensional filters.',
        details: [
          'Filter precisely by Hero, Tier, Size, or even the specific Merchant.',
          'Sync with your current run day to see exactly what is available to you.',
        ],
      },
      {
        title: 'BazaarDB Auto Upload',
        description:
          'Contribute to community data by automatically uploading your end-of-run boards and stats.',
        details: [
          'Uploads run silently in the background only when out of combat to ensure zero lag.',
          'This feature is opt-in and disabled by default. Turn it on in settings to help build the database.',
        ],
      },
      {
        title: 'Run History',
        description: 'Open run history to review saved runs, combat replays, and ghost battles.',
        details: [
          'Return to recent runs and key fights without leaving the game flow.',
          'Saved local battles and downloaded replays can open from history.',
        ],
      },
      {
        title: 'Combat Controls',
        description: 'Show combat time, pause state, and speed controls during fights.',
        details: [
          'Useful for reviewing combat pacing or controlling playback while recording.',
          'Speed buttons cycle between 0.50x, 0.67x, and 1.00x.',
        ],
      },
      {
        title: 'Anonymous Mode',
        description: 'Show the local player name as Anonymous.',
        details: [
          'Hide your local name in screenshots and recordings.',
          'Stay anonymous while streaming or sharing footage.',
        ],
      },
      {
        title: 'Legendary Rank Display',
        description: 'Supports normal, privacy-friendly, high-power, and rank-plus-rating views.',
        details: [
          'Hide the rank, use a high-power display, or show rank and rating together.',
          'Your rank display updates immediately after switching modes.',
        ],
      },
      {
        title: 'Enchant and upgrade previews',
        description:
          'Preview how an item changes after enchantment or upgrade before committing to the choice.',
        details: [
          'Enchant previews add post-enchant values and effects to item tooltips for easier comparison.',
          'Upgrade previews help evaluate shop buys, rewards, and build changes before spending resources.',
        ],
      },
      {
        title: 'Chinese Terminology',
        description: 'Display BazaarPlusPlus Chinese UI with the regional terminology you prefer.',
        details: [
          'Supports Mainland, Taiwan, and Hong Kong terminology styles.',
          'History, settings, and item tips follow the selected style.',
        ],
      },
    ],
    installation: {
      heading: 'Installation Guide',
      steps: [
        {
          title: 'Download latest installer',
          description:
            'Open the downloads page and choose the Windows .exe or macOS .dmg for your system.',
        },
        {
          title: 'Run the installer',
          description:
            'Close the game first. For updates, uninstall the old build before installing the new one.',
        },
        {
          title: 'Start the game once',
          description: 'Launch the game once so BazaarPlusPlus can finish setup.',
        },
        {
          title: 'Confirm the mod loaded',
          description:
            'On the main menu, a Codex button appears above the Settings button, and the game version text below shows BPP version.',
        },
      ],
    },
    quickStart: {
      heading: 'Combat Hotkeys',
      groups: [
        {
          title: 'Entry controls',
          description: 'Open panels or move into build preview mode.',
          items: [
            {
              bindings: [{ key: 'F8' }],
              title: 'History',
              description: 'Open recent runs, battles, and board snapshots from the lobby.',
            },
            {
              bindings: [{ key: 'Tab', action: 'Open' }],
              title: 'Codex',
              description: 'Open the codex.',
            },
            {
              bindings: [{ key: 'Caps Lock', action: 'Open' }],
              title: 'Ten-Win Build',
              description: 'Compare current set with Ten-Win builds.',
            },
          ],
        },
        {
          title: 'Preview holds',
          description: 'Temporarily expand item tooltip details while inspecting items.',
          note: 'Rebind Ctrl / Shift preview hotkeys in Settings -> Gameplay Settings.',
          items: [
            {
              bindings: [{ key: 'Ctrl' }],
              title: 'Enchant preview',
              description: 'Hold when always-show is off to reveal enchant changes on items.',
            },
            {
              bindings: [{ key: 'Shift' }],
              title: 'Upgrade preview',
              description: 'Hold while checking an item to preview upgraded stats and effects.',
            },
          ],
        },
      ],
    },
  },
  download: {
    title: 'Download BazaarPlusPlus',
    versionLabel: 'Current version',
    versionPending: 'Fetching latest version…',
    versionUnavailable: 'Unavailable',
    versionFailed: 'Cannot reach the latest version right now',
    releaseFallbackPrefix: 'Try ',
    releaseFallbackLink: 'GitHub Release',
    mainlandButtonLabel: 'CN mirror',
    windows: {
      title: 'Windows',
      arch: 'x64 · Intel / AMD',
      actionLabel: 'Download .exe',
      mainlandActionLabel: 'Windows mainland mirror',
    },
    mac: {
      title: 'macOS',
      arch: 'Apple Silicon · arm64',
      actionLabel: 'Download .dmg',
      mainlandActionLabel: 'macOS mainland mirror',
    },
    noteTitle: 'Install notes',
    noteParagraphs: [
      'Quit the game before installing. To update, uninstall the previous build first, then run the new installer.',
      'If the install does not take effect or gets blocked by security software, check the troubleshooting notes or reinstall the game and BazaarPlusPlus.',
    ],
  },
  support: {
    title: 'Support BazaarPlusPlus',
    intro: 'Your support helps keep BazaarPlusPlus going.',
    closeLabel: 'Close',
    wechat: {
      title: 'WeChat Pay',
      description: 'Scan to buy BazaarPlusPlus a drink.',
      actionLabel: 'Show QR code',
      regionLabel: 'CN',
      modalTitle: 'Buy BazaarPlusPlus a drink',
      modalSubtitle: 'WeChat Pay',
      modalHint: 'Leave your ID in the payment note if you want to appear in the Supporters list.',
      qrAriaLabel: 'WeChat Pay QR code',
    },
    kofi: {
      title: 'Ko-fi',
      description: 'Buy BazaarPlusPlus a drink on Ko-fi.',
      actionLabel: 'Open Ko-fi',
      regionLabel: 'Global',
    },
    supporters: {
      heading: 'Roll call',
      intro: 'Thanks for backing BazaarPlusPlus. Your support keeps the project moving further',
      unnamedNote: 'And thanks to everyone who supported BazaarPlusPlus without leaving a name',
      emptyNote: 'The roll call is still being gathered — check back soon',
      errorNote: 'Could not load the roll call right now — try again in a moment',
    },
  },
};

const messages: Record<Locale, LocalizedSiteCopy> = { zh, en };

export function getSiteCopy(locale: Locale): LocalizedSiteCopy {
  return messages[locale];
}

export function getPageTitle(page: PageTitleKey, locale: Locale): string {
  return `${messages[locale].pageTitles[page]} | BazaarPlusPlus`;
}

export const KOFI_URL = 'https://ko-fi.com/cauyxy';
export const BAZAARDB_META_URL = 'https://bazaardb.gg/run/meta?utm_source=bazaarplusplus';
export const BAZAARDB_ICON_PATH = '/bazaardb-icon.ico';
