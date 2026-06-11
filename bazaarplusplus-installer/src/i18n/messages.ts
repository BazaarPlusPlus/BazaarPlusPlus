// Frontend translation catalog. `zh` is the source of truth for the set of
// message keys; `en` is typed `Record<MessageKey, string>`, so TypeScript fails
// the build whenever a translation is missing or stale. Interpolate runtime
// values with `{name}` placeholders and pass them through `formatMessage`.

export type Locale = 'en' | 'zh';

export const defaultLocale: Locale = 'zh';

export const LOCALE_STORAGE_KEY = 'locale';

const zh = {
  // Document
  htmlLang: 'zh-CN',

  // Header / brand
  kicker: '因热爱而生',
  // Label shown on the language button for the CURRENT locale; clicking it
  // switches to the other language, so the zh copy invites English.
  languageToggle: 'Switch to English',
  headerCheckUpdate: '检查更新',
  headerCheckingUpdate: '检查中',
  supportProject: '支持项目',

  // Header social links
  socialXiaohongshu: '小红书',
  socialXiaohongshuTitle: '来小红书找我',
  socialXiaohongshuSubtitle: 'VibeCoding 日常和碎碎念',
  socialDouyin: '抖音',
  socialDouyinTitle: '来抖音找我',
  socialDouyinSubtitle: '短视频、开发切片和日常',
  socialBilibili: 'B 站',
  bilibiliProjectSubtitle: '教程、演示和项目内容',
  bilibiliAuthorSubtitle: 'VibeCoding 和日常碎碎念',

  // Support menu + payment modal
  wechatPay: '微信支付',
  wechatPayOpen: '打开收款码',
  kofiSubtitle: '请作者喝杯咖啡',
  supporterList: '支持者名单',
  supporterListSubtitle: '查看名单',
  wechatPayTagline: '请 Bazaar++ 喝一杯',
  supportLine1: '有你支持，Bazaar++ 会冒出更多好东西',
  supportLine2: '如果愿意，欢迎在备注里留一个支持者 ID',

  // Navigation rail
  navInstall: '安装',
  navHistory: '战绩',
  navStream: '直播',
  navAbout: '关于',

  // Shared actions
  close: '关闭',
  cancel: '取消',
  copy: '复制',
  refresh: '刷新',

  // Page titles
  installTitle: '安装',
  historyTitle: '战绩',
  streamTitle: '直播',
  aboutTitle: '关于',

  // Install page
  installActionsHeading: '安装操作',
  currentStatusHeading: '当前状态',
  gamePathHeading: '游戏路径',
  gamePathEmpty: '未选择 The Bazaar 安装目录',
  notSelected: '未选择',
  chooseAgain: '重新选择',
  recheck: '重新检测',
  actionInstall: '安装',
  actionReinstall: '重新安装',
  actionResetData: '重置本地数据',
  actionNoResettableData: '暂无本地数据',
  actionUninstall: '卸载',
  launchGame: '启动游戏',
  installed: '已安装',
  notInstalled: '未安装',
  ready: '就绪',
  missing: '缺失',
  gameFilesOk: '游戏文件完整',
  gameNotFound: '未找到游戏',
  modReady: '核心组件就绪',
  modNeedsReinstall: '需要重新安装',
  modNotInstalled: '尚未安装',
  installDone: '安装完成',
  resetDataConfirmTitle: '重置本地数据',
  resetDataConfirmBody:
    '这会删除 The Bazaar 安装目录中 BazaarPlusPlusV4 下的本地数据库、截图和战斗回放视频。',
  resetDataConfirmKeepsInstall:
    '不会卸载 The Bazaar，也不会卸载 BazaarPlusPlus 模组。',
  resetDataConfirmGameClosed: '请先退出 The Bazaar，避免数据文件仍被占用。',
  resetDataConfirmAcknowledge: '我知道这些本地数据会被删除。',
  resetDataConfirmAction: '删除本地数据',
  resetDataDone: '本地数据已删除',
  resetDataNothingToDelete: '未找到可重置的本地数据',
  resetDataBlockedByGame: 'The Bazaar 仍在运行。请先退出游戏，再重置本地数据。',
  resetDataPartialFailure:
    '有 {count} 个本地数据项目未能删除。请关闭游戏和直播来源后重试。',
  resetDataFailureDetails: '查看未删除项目',
  resetDataFailureCopy: '复制诊断信息',
  resetDataFailureCopied: '已复制',
  resetDataFailureCopyFailed: '复制失败，请手动选择文本复制',
  uninstallDone: '卸载完成',
  selectGameDirFirst: '请先选择 The Bazaar 安装目录。',
  tempoLaunchHint: '将通过 Tempo Launcher 启动：弹出 Tempo 窗口后请点击 PLAY。',
  tempoLaunchPrepare: '正在准备 Tempo 原生启动…',
  tempoLaunchBackup: '正在备份并临时移除模组文件…',
  tempoLaunchLauncher:
    'Tempo Launcher 已启动，请在 Tempo 窗口中点击 PLAY 继续。',
  tempoLaunchCapture: '已捕获游戏会话，正在切换到模组版本…',
  tempoLaunchRestore: '正在恢复模组文件…',
  tempoLaunchLaunching: '正在以模组模式启动游戏…',
  tempoLaunchDone: '游戏正在启动。',
  tempoLaunchFailed: 'Tempo 启动失败，模组文件已恢复。',
  tempoRestoreFailed:
    'Tempo 启动失败，且模组文件恢复未完成。请查看日志后再重试。',
  tempoCancelLaunch: '取消启动',
  tempoLaunchCancelled: '已取消启动，模组文件已恢复。',
  tempoLaunchInProgress: '已有一次启动正在进行中。',
  tempoGameAlreadyRunning: 'The Bazaar 已在运行，请先关闭游戏再启动。',
  tempoLauncherNotFound: '未找到 Tempo Launcher，请先安装 Tempo Launcher。',
  tempoCaptureTimeout:
    '等待 Tempo 启动游戏超时，请在 Tempo 窗口中点击 PLAY 后重试。',
  tempoInstallNeedsRepair:
    '检测到游戏文件被还原，请先点击"重新安装"修复后再启动。',

  // Install confirmation modal
  installModalTitle: '安装 BazaarPlusPlus',
  tutorialKicker: '使用教程',
  installModalBody:
    '安装会写入 BazaarPlusPlus 与 BepInEx 组件（BepInEx 用于让插件在游戏内运行）。',
  viewTutorial: '查看教程',
  installSteamNotice:
    '安装前请先关闭 Steam，以确保启动项正确写入；如果 Steam 正在运行，请手动退出后再继续。',
  installAcknowledge: '我确认安装插件存在风险，并愿意自行承担相关责任',
  compatModeLabel: '兼容模式（实验性）',
  compatModeDescription:
    '改为从游戏内部注入，不再依赖 Steam 启动脚本。更稳健，但会清空当前启动项；如遇游戏校验/更新还原，需要在此重新安装修复。仅在默认方式无法启动时开启。',
  compatModeForcedNotice:
    'macOS 27 及以上必须使用兼容模式：新版 Steam 不再支持启动脚本，已自动开启且无法关闭。',
  installing: '安装中...',
  confirmInstall: '确认安装',

  // Update check (header)
  updaterPreview: '浏览器预览',
  updaterCurrent: '已是最新',
  headerCheckFailed: '检查失败',
  updateModalKicker: '应用更新',
  updateModalTitle: '发现新版本',
  updateModalBody: 'BazaarPlusPlus {version} 已可用。',
  updateModalLater: '稍后',
  updateInstall: '下载并安装',
  updateNotesLabel: '更新内容',
  updateDownloading: '正在下载…',
  updateInstalling: '正在安装…',
  updateReady: '更新完成，重启后生效',
  updateRestartNow: '立即重启',
  updateError: '自动更新失败',
  updateRetry: '重试',

  // History page
  historyLoading: '读取战绩中',
  noLocalRuns: '暂无本地战绩',
  viewDetail: '查看详情',
  historySummaryRuns: '对局数',
  historySummaryVideos: '视频数',
  historySummaryWinRate: '胜率',
  runResultVictory: '胜利',
  runResultDefeat: '失败',
  runResultAbandoned: '放弃',
  runResultActive: '进行中',
  runMetricProgress: '胜场 / 天数',
  runStatRank: '段位',
  runStatRating: '段位分',

  // Run detail page
  runDetailBack: '返回战绩列表',
  runDetailLoading: '读取详情中',
  runDetailNotFound: '没有找到这局战绩',
  runDetailPlayer: '玩家',
  runStatusCompleted: '已完成',
  runStatusAbandoned: '已放弃',
  runStatusActive: '进行中',
  openScreenshotLocation: '打开截图位置',
  statWinLoss: '胜 / 负',
  statFinalDay: '结束日',
  statFinalRank: '最终段位',
  statFinalRating: '段位分',
  noLocalBattles: '暂无本地战斗记录',
  battleColDay: '天数',
  battleColResult: '结果',
  battleColOpponentHero: '对手英雄',
  battleColOpponentPlayer: '对手玩家',
  battleColRank: '段位',
  battleColRating: '段位分',
  battleColVideo: '视频',
  battleResultWin: '胜',
  battleResultLoss: '负',
  battleResultNeutral: '平局',
  openVideoLocation: '打开视频位置',
  deleteVideo: '删除视频',
  deleteVideoConfirmTitle: '删除视频',
  deleteVideoConfirmBody: '这会永久删除该场战斗的回放视频文件，且无法恢复。',
  deleteVideoConfirmAction: '删除视频',
  noVideo: '无视频',

  // Stream page
  streamModeCurrent: '战斗场数',
  streamModeHero: '完整英雄',
  streamModeHeroHalf: '半高英雄',
  streamOpenOverlay: '打开预览页',
  streamRestart: '重启服务',
  streamObsPlaceholder: '服务启动后显示 OBS Browser Source 地址',
  streamWindowSection: '展示窗口',
  streamWindowLatest: '当前展示最新记录',
  streamWindowOffset: '向前补 {count} 条记录',
  streamMoreHistory: '更多历史',
  streamLessHistory: '更少历史',
  streamOverlayConfig: '叠加层配置',
  streamCropCodeLabel: '裁切代码',
  streamCropCodePlaceholder: '输入裁切代码...',
  streamApplyCrop: '应用裁切代码',
  streamResetCrop: '恢复默认裁切',
  streamOpenSettings: '打开校准页',
  streamObsUrlLabel: 'OBS 地址',
  streamInfoHost: '主机',
  streamInfoPort: '端口',
  streamInfoDb: '数据库',
  streamInfoWindow: '窗口',
  streamStatusError: '叠加层错误',
  streamStatusStarting: '正在启动叠加层',
  streamStatusRunning: '叠加层运行中',
  streamStatusIdle: '叠加层空闲',
  streamStarting: '正在启动本地服务',
  streamIdleDetail: '服务尚未启动',
  streamPortDetail: '端口 {port}',
  streamCopied: 'OBS 地址已复制',
  streamCopyFailed: '复制失败，请手动选择文本复制',
  streamCropSaved: '裁切代码已保存',
  streamCropReset: '裁切设置已恢复默认',
  dbConnected: '数据库已连接',
  dbMissing: '数据库未找到',

  // About page
  aboutAppLabel: '应用',
  aboutBppLabel: '插件',
  aboutCredits: '致谢',
  aboutLicenses: '开源许可'
} as const;

export type MessageKey = keyof typeof zh;

const en: Record<MessageKey, string> = {
  htmlLang: 'en',

  kicker: 'Born of Passion',
  languageToggle: '切换中文',
  headerCheckUpdate: 'Check Updates',
  headerCheckingUpdate: 'Checking',
  supportProject: 'Support',

  socialXiaohongshu: 'Xiaohongshu',
  socialXiaohongshuTitle: 'Find me on Xiaohongshu',
  socialXiaohongshuSubtitle: 'VibeCoding notes and daily life',
  socialDouyin: 'Douyin',
  socialDouyinTitle: 'Find me on Douyin',
  socialDouyinSubtitle: 'Short videos, dev clips, and daily life',
  socialBilibili: 'Bilibili',
  bilibiliProjectSubtitle: 'Tutorials, demos, and project content',
  bilibiliAuthorSubtitle: 'VibeCoding and daily notes',

  wechatPay: 'WeChat Pay',
  wechatPayOpen: 'Open QR code',
  kofiSubtitle: 'Buy the author a coffee',
  supporterList: 'Supporters',
  supporterListSubtitle: 'View the list',
  wechatPayTagline: 'Buy Bazaar++ a drink',
  supportLine1: 'With your support, Bazaar++ keeps growing new things',
  supportLine2: 'If you like, leave a supporter ID in the note',

  navInstall: 'Install',
  navHistory: 'History',
  navStream: 'Stream',
  navAbout: 'About',

  close: 'Close',
  cancel: 'Cancel',
  copy: 'Copy',
  refresh: 'Refresh',

  installTitle: 'Install',
  historyTitle: 'History',
  streamTitle: 'Stream',
  aboutTitle: 'About',

  installActionsHeading: 'Actions',
  currentStatusHeading: 'Current Status',
  gamePathHeading: 'Game Path',
  gamePathEmpty: 'No The Bazaar install directory selected',
  notSelected: 'Not selected',
  chooseAgain: 'Choose again',
  recheck: 'Re-detect',
  actionInstall: 'Install',
  actionReinstall: 'Reinstall',
  actionResetData: 'Reset Local Data',
  actionNoResettableData: 'No Local Data',
  actionUninstall: 'Uninstall',
  launchGame: 'Launch Game',
  installed: 'Installed',
  notInstalled: 'Not Installed',
  ready: 'Ready',
  missing: 'Missing',
  gameFilesOk: 'Game files OK',
  gameNotFound: 'Game not found',
  modReady: 'Core components ready',
  modNeedsReinstall: 'Reinstall required',
  modNotInstalled: 'Not installed yet',
  installDone: 'Install complete',
  resetDataConfirmTitle: 'Reset Local Data',
  resetDataConfirmBody:
    'This deletes the local database, screenshots, and combat replay videos under BazaarPlusPlusV4 in The Bazaar install directory.',
  resetDataConfirmKeepsInstall:
    'This does not uninstall The Bazaar or the BazaarPlusPlus mod.',
  resetDataConfirmGameClosed:
    'Quit The Bazaar first so data files are not held open.',
  resetDataConfirmAcknowledge: 'I understand this local data will be deleted.',
  resetDataConfirmAction: 'Delete Local Data',
  resetDataDone: 'Local data deleted',
  resetDataNothingToDelete: 'No resettable local data found',
  resetDataBlockedByGame:
    'The Bazaar is still running. Quit the game before resetting local data.',
  resetDataPartialFailure:
    '{count} local data item(s) could not be deleted. Close the game and stream sources, then try again.',
  resetDataFailureDetails: 'Show undeleted items',
  resetDataFailureCopy: 'Copy Diagnostics',
  resetDataFailureCopied: 'Copied',
  resetDataFailureCopyFailed: 'Copy failed. Select the text and copy manually.',
  uninstallDone: 'Uninstall complete',
  selectGameDirFirst: 'Select The Bazaar install directory first.',
  tempoLaunchHint:
    'Launching goes through Tempo Launcher: click PLAY in the Tempo window when it appears.',
  tempoLaunchPrepare: 'Preparing native Tempo launch…',
  tempoLaunchBackup: 'Backing up and temporarily removing mod files…',
  tempoLaunchLauncher:
    'Tempo Launcher started. Click PLAY in Tempo to continue.',
  tempoLaunchCapture:
    'Captured the game session, switching to the modded build…',
  tempoLaunchRestore: 'Restoring mod files…',
  tempoLaunchLaunching: 'Launching the modded game…',
  tempoLaunchDone: 'The Bazaar is starting.',
  tempoLaunchFailed: 'Tempo launch failed. Mod files were restored.',
  tempoRestoreFailed:
    'Tempo launch failed while restoring mod files. Check the app logs before retrying.',
  tempoCancelLaunch: 'Cancel launch',
  tempoLaunchCancelled: 'Launch cancelled. Mod files were restored.',
  tempoLaunchInProgress: 'A launch is already in progress.',
  tempoGameAlreadyRunning:
    'The Bazaar is already running. Close it before launching.',
  tempoLauncherNotFound:
    'Tempo Launcher was not found. Install Tempo Launcher first.',
  tempoCaptureTimeout:
    'Timed out waiting for Tempo to start the game. Click PLAY in Tempo, then try again.',
  tempoInstallNeedsRepair:
    'Game files were reverted. Click Reinstall to repair before launching.',

  installModalTitle: 'Install BazaarPlusPlus',
  tutorialKicker: 'Tutorial',
  installModalBody:
    'Installation writes the BazaarPlusPlus and BepInEx components (BepInEx lets the plugin run inside the game).',
  viewTutorial: 'View tutorial',
  installSteamNotice:
    'Please close Steam before installing so the launch options are written correctly. If Steam is running, quit it manually before continuing.',
  installAcknowledge:
    'I understand installing the plugin carries risk and accept responsibility for it.',
  compatModeLabel: 'Compatibility mode (experimental)',
  compatModeDescription:
    'Inject from inside the game instead of relying on the Steam launch script. More robust, but it clears your current launch options; if a game verify/update reverts it, reinstall here to repair. Enable only if the default launch fails.',
  compatModeForcedNotice:
    'Required on macOS 27+: the new Steam client no longer supports the launch script, so this is enabled automatically and cannot be turned off.',
  installing: 'Installing...',
  confirmInstall: 'Confirm Install',

  updaterPreview: 'Preview mode',
  updaterCurrent: 'Up to date',
  headerCheckFailed: 'Check failed',
  updateModalKicker: 'App Update',
  updateModalTitle: 'Update Available',
  updateModalBody: 'BazaarPlusPlus {version} is available.',
  updateModalLater: 'Later',
  updateInstall: 'Download & Install',
  updateNotesLabel: "What's new",
  updateDownloading: 'Downloading…',
  updateInstalling: 'Installing…',
  updateReady: 'Update ready — restart to apply',
  updateRestartNow: 'Restart Now',
  updateError: 'Update failed',
  updateRetry: 'Retry',

  historyLoading: 'Loading runs',
  noLocalRuns: 'No local runs yet',
  viewDetail: 'View details',
  historySummaryRuns: 'Runs',
  historySummaryVideos: 'Videos',
  historySummaryWinRate: 'Win Rate',
  runResultVictory: 'VICTORY',
  runResultDefeat: 'DEFEAT',
  runResultAbandoned: 'ABANDONED',
  runResultActive: 'ACTIVE',
  runMetricProgress: 'Wins / Days',
  runStatRank: 'Rank',
  runStatRating: 'Rating',

  runDetailBack: 'Back to History',
  runDetailLoading: 'Loading details',
  runDetailNotFound: 'This run was not found',
  runDetailPlayer: 'Player',
  runStatusCompleted: 'Completed',
  runStatusAbandoned: 'Abandoned',
  runStatusActive: 'Active',
  openScreenshotLocation: 'Open screenshot location',
  statWinLoss: 'Wins / Losses',
  statFinalDay: 'Final Day',
  statFinalRank: 'Final Rank',
  statFinalRating: 'Rating',
  noLocalBattles: 'No local battle records',
  battleColDay: 'Day',
  battleColResult: 'Result',
  battleColOpponentHero: 'Opponent Hero',
  battleColOpponentPlayer: 'Opponent Player',
  battleColRank: 'Rank',
  battleColRating: 'Rating',
  battleColVideo: 'Video',
  battleResultWin: 'WIN',
  battleResultLoss: 'LOSS',
  battleResultNeutral: 'DRAW',
  openVideoLocation: 'Open video location',
  deleteVideo: 'Delete video',
  deleteVideoConfirmTitle: 'Delete Video',
  deleteVideoConfirmBody:
    'This permanently deletes the replay video file for this battle and cannot be undone.',
  deleteVideoConfirmAction: 'Delete Video',
  noVideo: 'No video',

  streamModeCurrent: 'Battle Count',
  streamModeHero: 'Full Hero',
  streamModeHeroHalf: 'Half Hero',
  streamOpenOverlay: 'Open Preview',
  streamRestart: 'Restart Service',
  streamObsPlaceholder:
    'The OBS Browser Source URL appears after the service starts',
  streamWindowSection: 'Display Window',
  streamWindowLatest: 'Showing the latest record',
  streamWindowOffset: 'Back {count} record(s)',
  streamMoreHistory: 'More History',
  streamLessHistory: 'Less History',
  streamOverlayConfig: 'Overlay Config',
  streamCropCodeLabel: 'Crop code',
  streamCropCodePlaceholder: 'Enter crop code...',
  streamApplyCrop: 'Apply Crop Code',
  streamResetCrop: 'Reset Crop',
  streamOpenSettings: 'Open Calibration',
  streamObsUrlLabel: 'OBS URL',
  streamInfoHost: 'Host',
  streamInfoPort: 'Port',
  streamInfoDb: 'DB',
  streamInfoWindow: 'Window',
  streamStatusError: 'Overlay Error',
  streamStatusStarting: 'Overlay Starting',
  streamStatusRunning: 'Overlay Running',
  streamStatusIdle: 'Overlay Idle',
  streamStarting: 'Starting local service',
  streamIdleDetail: 'Service not started',
  streamPortDetail: 'Port {port}',
  streamCopied: 'OBS URL copied',
  streamCopyFailed: 'Copy failed. Select the text and copy manually.',
  streamCropSaved: 'Crop code saved',
  streamCropReset: 'Crop settings reset to default',
  dbConnected: 'DB Connected',
  dbMissing: 'DB Missing',

  aboutAppLabel: 'App',
  aboutBppLabel: 'BPP',
  aboutCredits: 'Credits',
  aboutLicenses: 'Licenses'
};

export const messages: Record<Locale, Record<MessageKey, string>> = { zh, en };

export type TranslateParams = Record<string, string | number>;

export function formatMessage(
  locale: Locale,
  key: MessageKey,
  params?: TranslateParams
): string {
  let text = messages[locale][key];
  if (!params) {
    return text;
  }
  for (const [name, value] of Object.entries(params)) {
    text = text.replaceAll(`{${name}}`, String(value));
  }
  return text;
}

export function resolveInitialLocale(): Locale {
  if (typeof window === 'undefined') {
    return defaultLocale;
  }
  const saved = window.localStorage.getItem(LOCALE_STORAGE_KEY);
  return saved === 'en' || saved === 'zh' ? saved : defaultLocale;
}
