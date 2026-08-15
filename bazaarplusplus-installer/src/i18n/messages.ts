// Frontend translation catalog. `zh` is the source of truth for the set of
// message keys; `en` is typed `Record<MessageKey, string>`, so TypeScript fails
// the build whenever a translation is missing or stale. Interpolate runtime
// values with `{name}` placeholders and pass them through `formatMessage`.
//
// Count-dependent English wording uses `{name|singular|plural}`, which selects
// on the numeric value of the `name` parameter. Chinese has no plural form, so
// zh copy simply omits the token.
//
// Product vocabulary, keep it consistent:
//   BazaarPlusPlus 本体 = 插件 / plugin
//   插件 + BepInEx 等一起写入游戏目录的文件 = 组件 / components
//   本地 HTTP 服务 = 直播服务 / stream service
//   直播画面上的那一层 = 叠加层 / overlay

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
  primaryNavigation: '主导航',

  // Window controls (Windows custom chrome)
  windowControls: '窗口控制',
  minimizeWindow: '最小化窗口',
  maximizeWindow: '最大化窗口',
  restoreWindow: '还原窗口',
  closeWindow: '关闭窗口',
  hideToTrayWhileStreaming: '隐藏到托盘（直播服务仍在运行）',

  // Header social links
  socialXiaohongshu: '小红书',
  socialXiaohongshuTitle: '来小红书找我',
  socialXiaohongshuSubtitle: 'VibeCoding 日常和碎碎念',
  socialDouyin: '抖音',
  socialDouyinTitle: '来抖音找我',
  socialDouyinSubtitle: '短视频、开发切片和日常',
  socialBilibili: 'B 站',
  bilibiliProjectSubtitle: '教程、演示和项目内容',
  bilibiliAuthorSubtitle: 'BazaarLine 作者',
  bilibiliCoreDevSubtitle: 'CoreDev',

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
  notifications: '通知',
  cancel: '取消',
  copy: '复制',
  refresh: '刷新',
  retry: '重试',
  problemDiagnostics: '查看诊断信息',

  // Page titles
  installTitle: '安装',
  historyTitle: '战绩',
  streamTitle: '直播',
  aboutTitle: '关于',

  // Install page
  gamePathEmpty: '未选择 The Bazaar 游戏目录',
  installDetecting: '正在检测 The Bazaar 与 BazaarPlusPlus',
  installRefreshing: '正在重新检测安装状态',
  actionChooseDirectory: '选择游戏目录',
  actionInstall: '安装',
  actionReinstall: '重新安装',
  actionResetData: '删除本地数据',
  actionNoResettableData: '暂无本地数据',
  actionResetBepinex: '删除 BepInEx 文件夹',
  actionUninstall: '卸载',
  launchGame: '启动游戏',
  installed: '已安装',
  notInstalled: '未安装',
  ready: '就绪',
  modNeedsReinstall: '需要重新安装',
  installDone: '安装完成',
  resetDataConfirmTitle: '删除本地数据',
  resetDataTarget: '目标：{path} 内的 BazaarPlusPlusV5 文件夹',
  resetDataConfirmBody:
    '这会删除 The Bazaar 游戏目录中 BazaarPlusPlusV5 下的本地数据库、截图和战斗回放视频。这包括尚未上传的对局数据。',
  resetDataConfirmKeepsInstall:
    '不会卸载 The Bazaar，也不会卸载 BazaarPlusPlus 插件。',
  resetDataConfirmGameClosed: '请先退出 The Bazaar，避免数据文件仍被占用。',
  resetDataConfirmAcknowledge: '我知道这些本地数据会被删除。',
  resetDataConfirmAction: '删除本地数据',
  resetDataRunning: '正在删除目标本地数据…',
  resetDataDone: '本地数据已删除',
  resetDataNothingToDelete: '未找到可删除的本地数据',
  resetDataBlockedByGame: 'The Bazaar 仍在运行。请先退出游戏，再删除本地数据。',
  resetDataPartialFailure:
    '有 {count} 个本地数据项目未能删除。请关闭游戏和直播来源后重试。',
  resetDataFailureDetails: '查看未删除项目',
  resetDataFailureCopy: '复制诊断信息',
  resetDataFailureCopied: '已复制',
  resetDataFailureCopyFailed: '复制失败，请手动选择文本复制',
  resetBepinexConfirmTitle: '删除 BepInEx 文件夹',
  resetBepinexTarget: '目标：{path} 内的 BepInEx 文件夹',
  resetBepinexConfirmBody:
    '这会删除 The Bazaar 游戏目录中的整个 BepInEx 文件夹。',
  resetBepinexConfirmOtherMods:
    '文件夹内的所有内容都会被删除，包括你安装的其他 BepInEx 插件。',
  resetBepinexConfirmReinstall:
    '删除后 BazaarPlusPlus 将不再生效，需要重新点击“安装”来恢复。',
  resetBepinexConfirmGameClosed: '请先退出 The Bazaar，避免文件仍被占用。',
  resetBepinexConfirmAcknowledge:
    '我知道整个 BepInEx 文件夹（含其他插件）会被删除。',
  resetBepinexConfirmAction: '删除 BepInEx 文件夹',
  resetBepinexRunning: '正在删除目标 BepInEx 文件夹…',
  resetBepinexDone: 'BepInEx 文件夹已删除',
  resetBepinexNothingToDelete: '未找到 BepInEx 文件夹',
  resetBepinexBlockedByGame:
    'The Bazaar 仍在运行。请先退出游戏，再删除 BepInEx 文件夹。',
  resetBepinexPartialFailure: '有 {count} 个项目未能删除。请关闭游戏后重试。',
  operationCannotBeCancelled: '操作已开始，完成前无法取消或关闭此窗口。',
  uninstallConfirmTitle: '卸载 BazaarPlusPlus',
  uninstallTarget: '目标：{path}',
  uninstallConfirmBody: '将移除 BepInEx 与插件文件，保留本地数据与游戏本体。',
  uninstallConfirmKeepsData: '本地数据库、截图与回放视频不会被删除。',
  uninstallConfirmAction: '卸载',
  uninstallRunning: '正在卸载…',
  uninstallDone: '卸载完成',
  installWarningGameMissing:
    '未找到有效的 The Bazaar 游戏目录，请选择游戏目录。',
  installWarningSteamConfigUnavailable:
    '无法读取 Steam 启动项配置。请先启动一次 Steam，然后完全退出并重试。',
  installWarningLaunchOptionsNotEmpty:
    'The Bazaar 的 Steam 启动项必须为空；修复会移除其中的全部内容。',
  installWarningTrampolineNotReady:
    'macOS 游戏启动配置缺失或不是当前版本，需要修复。',
  installWarningObsoleteMacosArtifacts:
    '检测到不属于当前 macOS 安装方式的文件，需要清理。',
  installWarningUnexpected:
    '检测到未知的安装警告，请重新检测；若持续出现，请查看诊断信息。',
  installProblemDetectionFailed: '检测安装状态失败，请重试。',
  installProblemChooseDirectoryFailed: '无法打开游戏目录选择器，请重试。',
  installProblemInstallFailed:
    '安装或修复失败。请退出 The Bazaar 与 Steam，检查目录权限后重试。',
  installProblemResetDataFailed:
    '重置本地数据失败，请关闭占用文件的程序后重试。',
  installProblemResetBepinexFailed:
    '重置 BepInEx 失败，请退出 The Bazaar 后重试。',
  installProblemUninstallFailed:
    '卸载失败，请退出 The Bazaar 与 Steam 后重试。',
  installProblemLaunchFailed:
    '无法通过 Steam 启动游戏，请确认 Steam 正在运行后重试。',
  installProblemUnexpected: '处理安装状态时发生意外错误，请重试。',

  // Install confirmation modal
  installModalTitle: '安装 BazaarPlusPlus',
  tutorialKicker: '使用教程',
  installModalBody:
    '安装会写入 BazaarPlusPlus 与 BepInEx 组件（BepInEx 用于让插件在游戏内运行）。',
  viewTutorial: '查看教程',
  installSteamNotice:
    '安装前请先关闭 Steam；如果 Steam 正在运行，请手动退出后再继续。',
  installSteamNoticeMacos: '继续后，安装器将关闭 Steam，以完成 macOS 配置。',
  installAcknowledge: '我确认安装插件存在风险，并愿意自行承担相关责任',
  installing: '安装中…',
  confirmInstall: '确认安装',

  // Update check (header)
  updaterPreview: '浏览器预览',
  updaterCurrent: '已是最新',
  headerCheckFailed: '检查失败',
  updateHeaderAvailable: '发现更新',
  updateModalKicker: '应用更新',
  updateModalTitle: '发现新版本',
  updateModalBody: 'BazaarPlusPlus {version} 已可用。',
  updateMainlandDownloadTitle: '中国大陆下载',
  updateMainlandDownloadHint: '自动更新较慢时，可通过大陆渠道手动下载。',
  updateMainlandDownload: '打开下载页',
  updateModalLater: '稍后',
  updateInstall: '下载并安装',
  updateNotesLabel: '更新内容',
  updateDownloading: '正在下载…',
  updateInstalling: '正在安装…',
  updateInstallingBody: '下载已完成，正在安装 BazaarPlusPlus {version}。',
  updateReady: '更新完成，重启后生效',
  updateReadyBody: 'BazaarPlusPlus {version} 已安装完成。重启应用后即可使用。',
  updateRestartNow: '立即重启',
  updateRestarting: '正在重启…',
  updateError: '自动更新失败',
  updateRestartFailedTitle: '自动重启失败',
  updateRetry: '重试',
  updateRetryRestart: '再次尝试重启',
  updateDownloadProgressLabel: '更新下载进度',
  updateDownloadProgressUnknown: '已下载 {downloaded} MB',
  updateDownloadProgressKnown: '已下载 {downloaded} / {total} MB（{percent}%）',
  updaterProblemCheckFailed: '无法检查更新。请检查网络连接后重试。',
  updaterProblemDownloadFailed: '更新下载失败。请检查网络连接后重试。',
  updaterProblemInstallFailed:
    '更新安装失败。请重试；如果问题持续，请重新打开应用后再次检查更新。',
  updaterProblemRestartFailedMac:
    '自动重启失败，但 BazaarPlusPlus {version} 已安装完成。请退出 BazaarPlusPlus，再从“应用程序”中重新打开。',
  updaterProblemRestartFailedWindows:
    '自动重启失败，但 BazaarPlusPlus {version} 已安装完成。请退出 BazaarPlusPlus，再从开始菜单重新打开。',

  // History page
  historyLoading: '读取战绩中',
  noLocalRuns: '暂无本地战绩',
  historyEmptyDescription:
    '完成一局 The Bazaar 对局后，战绩会自动显示在这里。你也可以刷新，或前往安装页检查游戏目录。',
  historyEmptyRefresh: '刷新战绩',
  historyEmptyInstall: '检查游戏目录',
  viewDetail: '查看详情',
  historySummaryRuns: '对局数',
  historySummaryVideos: '视频数',
  historySummaryWinRate: '胜率',
  historyProblemUnavailable:
    '未找到可用的本地战绩数据库。请先在安装页选择正确的游戏目录。',
  historyProblemReadFailed:
    '读取本地战绩失败。请关闭可能占用数据库的程序后重试。',
  historyProblemBlockedByGame:
    'The Bazaar 仍在后台运行，正占用战绩数据库。关闭游戏窗口后进程有时不会退出；结束该进程即可读取战绩。',
  historyEndGameProcess: '结束游戏进程',
  historyEndGameProcessDone: '已结束游戏进程，正在重新读取战绩。',
  historyEndGameProcessNotFound: '游戏进程已经退出，正在重新读取战绩。',
  historyEndGameProcessFailedWindows:
    '结束游戏进程失败。请在任务管理器中结束 TheBazaar.exe 后重试。',
  historyEndGameProcessFailedMac:
    '结束游戏进程失败。请在“活动监视器”中结束 The Bazaar 后重试。',
  historyProblemUnsupportedSchema:
    '战绩数据库版本不受支持（当前 {found}，支持 {expected}）。请更新 BazaarPlusPlus 插件或安装器。',
  historyProblemPreviewUnavailable:
    '战绩已载入，但缩略图服务当前不可用。可前往直播页启动本地服务。',
  historyProblemUnexpected: '加载本地战绩时发生意外错误。请重试。',
  historyOpenInstall: '前往安装页',
  historyOpenStream: '前往直播页',
  historyPreviewFallback: '缩略图不可用；刷新页面可重试。',
  historyPreviewServiceOffline: '缩略图服务未启动，请前往直播页启动',
  runResultVictory: '胜利',
  runResultDefeat: '失败',
  runResultAbandoned: '放弃',
  runResultActive: '进行中',
  runMetricWins: '胜场',
  runMetricDays: '天数',
  runStatRank: '段位',
  runStatRating: '段位分',

  // Run detail page
  runDetailEyebrow: '对局',
  runDetailBack: '返回战绩列表',
  runDetailLoading: '读取详情中',
  runDetailNotFound: '没有找到这局战绩',
  runDetailUnavailable: '战绩不可用',
  runDetailRefreshing: '正在刷新详情',
  runDetailProblemUnavailable:
    '未找到可用的本地战绩数据库。请先在安装页选择正确的游戏目录。',
  runDetailProblemReadFailed:
    '读取这局战绩失败。请关闭可能占用数据库的程序后重试。',
  runDetailProblemRevealScreenshotFailed:
    '无法打开这局战绩的截图位置，请重试。',
  runDetailProblemRevealVideoFailed: '无法打开这场战斗的视频位置，请重试。',
  runDetailProblemDeleteVideoFailed: '删除这场战斗的视频失败，请重试。',
  runDetailProblemUnexpected: '处理这局战绩时发生意外错误，请重试。',
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
  noLocalBattlesDescription: '对局完成后战斗记录会自动出现在这里；也可刷新重试',
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
  deleteVideoTarget: '目标：战斗 {battleId} · 视频 {videoId}',
  deleteVideoConfirmBody: '这会永久删除该场战斗的回放视频文件，且无法恢复。',
  deleteVideoConfirmAction: '删除视频',
  deleteVideoRunning: '正在删除目标视频…',

  // Storage cleanup (History page)
  storageCleanupTitle: '存储清理',
  storageCleanupScreenshotsLabel: '对局结算截图',
  storageCleanupRunDataLabel: '对局数据',
  storageCleanupPresetAll: '全部',
  storageCleanupPresetOlderThan7Days: '7 天前',
  storageCleanupPresetBeforeThisMonth: '本月以前',
  storageCleanupActionLabel: '清理{scope} · {preset}',
  storageCleanupConfirmTitle: '确认清理',
  storageCleanupTarget: '目标：{scope} · 范围：{preset}',
  storageCleanupScreenshotsConfirmBody:
    '将删除 {count} 张结算截图（约 {size}），删除后无法恢复。',
  storageCleanupRunDataConfirmBody:
    '将删除 {runs} 局对局记录，包括 {battles} 场战斗、{videos} 个回放视频及相关截图（约 {size}），删除后无法恢复。',
  storageCleanupSkippedPending: '另有 {count} 项尚未完成上传，将自动跳过。',
  storageCleanupNothingToClean: '没有符合条件的可清理数据。',
  storageCleanupConfirmAction: '确认清理',
  storageCleanupRunningScreenshots: '正在删除结算截图…',
  storageCleanupRunningRunData: '正在删除对局数据…',
  storageCleanupProblemUnavailable:
    '未找到可清理的本地战绩数据库，请先在安装页选择正确的游戏目录。',
  storageCleanupProblemPreviewFailed: '无法预览清理范围，请重试。',
  storageCleanupProblemExecuteFailed:
    '清理未完成，目标和范围已保留；请查看诊断后重试或安全关闭。',
  storageCleanupProblemUnexpected: '清理存储时发生意外错误，请重试。',
  storageCleanupScreenshotsDone: '已删除 {files} 个文件，释放约 {size}。',
  storageCleanupRunDataDone:
    '已删除 {runs} 局对局和 {files} 个文件，释放约 {size}。',
  noVideo: '无视频',

  // Stream page
  streamDisplayModeLabel: '显示样式',
  streamModeCurrent: '战斗场数',
  streamModeHero: '完整英雄',
  streamModeHeroHalf: '半高英雄',
  streamOpenOverlay: '预览叠加层',
  streamStart: '启动服务',
  streamRestart: '重启服务',
  streamObsPlaceholder: '直播服务启动后显示 OBS Browser Source 地址',
  streamObsGuide:
    '将此地址添加为 OBS 的 Browser Source，即可在直播画面中显示叠加层',
  streamWindowSection: '展示窗口',
  streamWindowLatest: '当前展示最新记录',
  streamWindowOffset: '向前补 {count} 条记录',
  streamMoreHistory: '更多历史',
  streamLessHistory: '更少历史',
  streamOverlayConfig: '叠加层配置',
  streamCropCodeLabel: '裁切代码',
  streamCropCodePlaceholder: '输入裁切代码…',
  streamApplyCrop: '应用裁切代码',
  streamResetCrop: '恢复默认裁切',
  streamOpenSettings: '校准叠加层',
  streamObsUrlLabel: 'OBS 地址',
  streamInfoHost: '主机',
  streamInfoPort: '端口',
  streamInfoDb: '数据库',
  streamInfoWindow: '窗口',
  streamStatusError: '直播服务错误',
  streamStatusStarting: '正在启动直播服务',
  streamStatusRunning: '直播服务运行中',
  streamStatusIdle: '直播服务未启动',
  streamStatusStale: '直播服务状态可能已过期',
  streamStatusUnavailable: '直播服务状态不可用',
  streamStarting: '正在启动本地直播服务',
  streamIdleDetail: '直播服务尚未启动',
  streamStaleRunningDetail: '上次检测为运行中，正在等待最新的直播服务状态',
  streamStaleIdleDetail: '上次检测为未运行，正在等待最新的直播服务状态',
  streamStatusUnavailableDetail: '尚未取得可信的直播服务状态',
  streamPortDetail: '端口 {port}',
  streamRetryStatus: '重新获取状态',
  streamRetryCrop: '重新加载配置',
  streamCopied: 'OBS 地址已复制',
  streamCopyFailed: '复制失败，请手动选择文本复制',
  streamCropSaved: '裁切代码已保存',
  streamCropReset: '裁切设置已恢复默认',
  streamProblemServiceFailed: '直播服务未能启动。请确认端口 17654 可用后重试。',
  streamProblemRestartFailed: '直播服务重启失败。请确认端口 17654 可用后重试。',
  streamProblemPollFailed:
    '暂时无法确认直播服务的最新状态；上次状态已标记为过期，请重新获取。',
  streamProblemWindowFailed: '无法调整展示窗口，请重试。',
  streamProblemCropLoadFailed:
    '无法加载叠加层配置；其他直播控制仍可使用，请重新加载。',
  streamProblemCropSaveFailed: '无法保存叠加层配置；请检查裁切代码并重试。',
  streamProblemOpenOverlayFailed: '无法打开叠加层预览，请重试。',
  streamProblemOpenSettingsFailed: '无法打开叠加层校准页，请重试。',
  streamProblemUnexpected: '处理直播功能时发生意外错误，请重试。',
  dbConnected: '数据库已连接',
  dbMissing: '数据库未找到',

  // About page
  aboutAppLabel: '应用',
  aboutBppLabel: '插件',
  aboutCredits: '致谢',
  aboutContributors: '贡献者',
  aboutAcknowledgements: '数据与灵感',
  aboutLicenses: '开源许可',
  aboutVerifiedBadge: 'Fable 5 认证',
  aboutLoadingBootstrapOnly: '正在获取本机应用信息。',
  aboutLoadingBootstrap: '正在获取本机应用信息，暂时显示随应用打包的备用数据。',
  aboutFallbackPreview: '当前为浏览器预览，显示随应用打包的备用数据。',
  aboutProblemBootstrapFailed:
    '无法获取本机应用信息。已保留可用的备用数据，请重试。',
  aboutUnavailableValue: '不可用',
  aboutRetrying: '正在重试',
  aboutBlockingFailure: '无法获取应用信息，且没有可用的备用数据。请重试。',

  // Refined dashboard copy
  installOverviewHealthyShort: '所有组件运行正常',
  installOverviewUpdateDescription: '当前组件版本不一致，重新安装即可完成更新',
  installOverviewNotInstalledDescription: '选择游戏目录后即可安装插件',
  installationDirectoryHeading: '游戏目录',
  copyPath: '复制路径',
  pathCopied: '已复制',
  selectDirectory: '选择目录',
  applicationVersionHeading: '应用版本',
  developmentBuild: '开发版',
  stableBuild: '正式版',
  maintenanceToolsHeading: '维护工具',
  maintenanceResetDataDescription: '删除本地数据库、截图与回放视频',
  maintenanceNoResettableDataDescription: '当前没有可删除的本地数据',
  maintenanceResetBepinexDescription: '重置插件环境',
  checkUpdateDescription: '获取最新版本信息',
  maintenanceUninstallDescription: '移除所有组件',
  installModalSubtitle: '安装前确认与环境检查',
  historySummaryRunsDescription: '总对局场次',
  historySummaryVideosDescription: '已录制视频',
  historySummaryWinRateDescription: '已完成对局胜率',
  historySummaryWinRateUnavailable: '胜率暂无数据',
  storageCleanupScreenshotsDescription: '保存对局结算时的截图文件',
  storageCleanupRunDataDescription: '保存对局产生的各类数据文件',
  aboutTagline: 'The Bazaar 数据增强与分析工具'
} as const;

export type MessageKey = keyof typeof zh;

const en: Record<MessageKey, string> = {
  htmlLang: 'en',

  kicker: 'Born of Passion',
  languageToggle: '切换中文',
  headerCheckUpdate: 'Check Updates',
  headerCheckingUpdate: 'Checking',
  supportProject: 'Support',
  primaryNavigation: 'Primary navigation',

  windowControls: 'Window controls',
  minimizeWindow: 'Minimize window',
  maximizeWindow: 'Maximize window',
  restoreWindow: 'Restore window',
  closeWindow: 'Close window',
  hideToTrayWhileStreaming: 'Hide to tray (stream service keeps running)',

  socialXiaohongshu: 'Xiaohongshu',
  socialXiaohongshuTitle: 'Find me on Xiaohongshu',
  socialXiaohongshuSubtitle: 'VibeCoding notes and daily life',
  socialDouyin: 'Douyin',
  socialDouyinTitle: 'Find me on Douyin',
  socialDouyinSubtitle: 'Short videos, dev clips, and daily life',
  socialBilibili: 'Bilibili',
  bilibiliProjectSubtitle: 'Tutorials, demos, and project content',
  bilibiliAuthorSubtitle: 'BazaarLine author',
  bilibiliCoreDevSubtitle: 'CoreDev',

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
  notifications: 'Notifications',
  cancel: 'Cancel',
  copy: 'Copy',
  refresh: 'Refresh',
  retry: 'Retry',
  problemDiagnostics: 'Show diagnostics',

  installTitle: 'Install',
  historyTitle: 'History',
  streamTitle: 'Stream',
  aboutTitle: 'About',

  gamePathEmpty: 'No game directory selected for The Bazaar',
  installDetecting: 'Detecting The Bazaar and BazaarPlusPlus',
  installRefreshing: 'Re-detecting installation state',
  actionChooseDirectory: 'Choose Game Directory',
  actionInstall: 'Install',
  actionReinstall: 'Reinstall',
  actionResetData: 'Delete Local Data',
  actionNoResettableData: 'No Local Data',
  actionResetBepinex: 'Delete BepInEx Folder',
  actionUninstall: 'Uninstall',
  launchGame: 'Launch Game',
  installed: 'Installed',
  notInstalled: 'Not Installed',
  ready: 'Ready',
  modNeedsReinstall: 'Reinstall required',
  installDone: 'Install complete',
  resetDataConfirmTitle: 'Delete Local Data',
  resetDataTarget: 'Target: the BazaarPlusPlusV5 folder inside {path}',
  resetDataConfirmBody:
    'This deletes the local database, screenshots, and combat replay videos under BazaarPlusPlusV5 in The Bazaar install directory. This includes battle data that has not been uploaded yet.',
  resetDataConfirmKeepsInstall:
    'This does not uninstall The Bazaar or the BazaarPlusPlus plugin.',
  resetDataConfirmGameClosed:
    'Quit The Bazaar first so data files are not held open.',
  resetDataConfirmAcknowledge: 'I understand this local data will be deleted.',
  resetDataConfirmAction: 'Delete Local Data',
  resetDataRunning: 'Deleting the target local data…',
  resetDataDone: 'Local data deleted',
  resetDataNothingToDelete: 'No local data found to delete',
  resetDataBlockedByGame:
    'The Bazaar is still running. Quit the game before deleting local data.',
  resetDataPartialFailure:
    '{count} local data {count|item|items} could not be deleted. Close the game and stream sources, then try again.',
  resetDataFailureDetails: 'Show undeleted items',
  resetDataFailureCopy: 'Copy Diagnostics',
  resetDataFailureCopied: 'Copied',
  resetDataFailureCopyFailed: 'Copy failed. Select the text and copy manually.',
  resetBepinexConfirmTitle: 'Delete BepInEx Folder',
  resetBepinexTarget: 'Target: the BepInEx folder inside {path}',
  resetBepinexConfirmBody:
    'This deletes the entire BepInEx folder in The Bazaar install directory.',
  resetBepinexConfirmOtherMods:
    'Everything inside it is removed, including any other BepInEx plugins you installed.',
  resetBepinexConfirmReinstall:
    'After this, BazaarPlusPlus stops working — click Install again to restore it.',
  resetBepinexConfirmGameClosed:
    'Quit The Bazaar first so the files are not held open.',
  resetBepinexConfirmAcknowledge:
    'I understand the entire BepInEx folder (including other plugins) will be deleted.',
  resetBepinexConfirmAction: 'Delete BepInEx Folder',
  resetBepinexRunning: 'Deleting the target BepInEx folder…',
  resetBepinexDone: 'BepInEx folder deleted',
  resetBepinexNothingToDelete: 'No BepInEx folder found',
  resetBepinexBlockedByGame:
    'The Bazaar is still running. Quit the game before deleting the BepInEx folder.',
  resetBepinexPartialFailure:
    '{count} {count|item|items} could not be deleted. Close the game, then try again.',
  operationCannotBeCancelled:
    'This operation has started and cannot be canceled or dismissed until it finishes.',
  uninstallConfirmTitle: 'Uninstall BazaarPlusPlus',
  uninstallTarget: 'Target: {path}',
  uninstallConfirmBody:
    'This removes BepInEx and plugin files while keeping local data and the base game.',
  uninstallConfirmKeepsData:
    'Local database, screenshots, and replay videos are not deleted.',
  uninstallConfirmAction: 'Uninstall',
  uninstallRunning: 'Uninstalling…',
  uninstallDone: 'Uninstall complete',
  installWarningGameMissing:
    'No valid The Bazaar installation was found. Choose the game directory.',
  installWarningSteamConfigUnavailable:
    'Steam launch-option configuration could not be read. Start Steam once, quit it completely, and retry.',
  installWarningLaunchOptionsNotEmpty:
    "The Bazaar's Steam launch options must be empty; Repair removes all of their contents.",
  installWarningTrampolineNotReady:
    'The macOS game bootstrap is missing or is not the current version and needs repair.',
  installWarningObsoleteMacosArtifacts:
    'Files outside the current macOS installation layout were found and need cleanup.',
  installWarningUnexpected:
    'An unknown installation warning was detected. Re-detect; if it persists, check the diagnostics.',
  installProblemDetectionFailed:
    'Installation state could not be detected. Please retry.',
  installProblemChooseDirectoryFailed:
    'The game directory picker could not be opened. Please retry.',
  installProblemInstallFailed:
    'Install or repair failed. Quit The Bazaar and Steam, check directory permissions, then retry.',
  installProblemResetDataFailed:
    'Local data could not be reset. Close apps using those files, then retry.',
  installProblemResetBepinexFailed:
    'BepInEx could not be reset. Quit The Bazaar, then retry.',
  installProblemUninstallFailed:
    'Uninstall failed. Quit The Bazaar and Steam, then retry.',
  installProblemLaunchFailed:
    'The game could not be launched through Steam. Make sure Steam is running, then retry.',
  installProblemUnexpected:
    'Something unexpected happened while handling installation state. Please retry.',

  installModalTitle: 'Install BazaarPlusPlus',
  tutorialKicker: 'Tutorial',
  installModalBody:
    'Installation writes the BazaarPlusPlus and BepInEx components (BepInEx lets the plugin run inside the game).',
  viewTutorial: 'View tutorial',
  installSteamNotice:
    'Please close Steam before installing. If Steam is running, quit it manually before continuing.',
  installSteamNoticeMacos:
    'Continuing will close Steam to complete the macOS setup.',
  installAcknowledge:
    'I understand installing the plugin carries risk and accept responsibility for it.',
  installing: 'Installing…',
  confirmInstall: 'Confirm Install',

  updaterPreview: 'Preview mode',
  updaterCurrent: 'Up to date',
  headerCheckFailed: 'Check failed',
  updateHeaderAvailable: 'Update available',
  updateModalKicker: 'App Update',
  updateModalTitle: 'Update Available',
  updateModalBody: 'BazaarPlusPlus {version} is available.',
  updateMainlandDownloadTitle: 'Mainland China download',
  updateMainlandDownloadHint:
    'If automatic updates are slow, use the mainland download channel.',
  updateMainlandDownload: 'Open download',
  updateModalLater: 'Later',
  updateInstall: 'Download & Install',
  updateNotesLabel: "What's new",
  updateDownloading: 'Downloading…',
  updateInstalling: 'Installing…',
  updateInstallingBody:
    'The download is complete. Installing BazaarPlusPlus {version}.',
  updateReady: 'Update ready — restart to apply',
  updateReadyBody:
    'BazaarPlusPlus {version} is installed. Restart the app to use it.',
  updateRestartNow: 'Restart Now',
  updateRestarting: 'Restarting…',
  updateError: 'Update failed',
  updateRestartFailedTitle: 'Automatic restart failed',
  updateRetry: 'Retry',
  updateRetryRestart: 'Try Restart Again',
  updateDownloadProgressLabel: 'Update download progress',
  updateDownloadProgressUnknown: 'Downloaded {downloaded} MB',
  updateDownloadProgressKnown:
    'Downloaded {downloaded} / {total} MB ({percent}%)',
  updaterProblemCheckFailed:
    'Could not check for updates. Check your network connection, then retry.',
  updaterProblemDownloadFailed:
    'The update could not be downloaded. Check your network connection, then retry.',
  updaterProblemInstallFailed:
    'The update could not be installed. Retry; if the problem continues, reopen the app and check again.',
  updaterProblemRestartFailedMac:
    'Automatic restart failed, but BazaarPlusPlus {version} is installed. Quit BazaarPlusPlus, then open it again from Applications.',
  updaterProblemRestartFailedWindows:
    'Automatic restart failed, but BazaarPlusPlus {version} is installed. Quit BazaarPlusPlus, then open it again from the Start menu.',

  historyLoading: 'Loading runs',
  noLocalRuns: 'No local runs yet',
  historyEmptyDescription:
    'Completed The Bazaar runs appear here automatically. Refresh, or check the selected game directory on the Install page.',
  historyEmptyRefresh: 'Refresh History',
  historyEmptyInstall: 'Check Game Directory',
  viewDetail: 'View Details',
  historySummaryRuns: 'Runs',
  historySummaryVideos: 'Videos',
  historySummaryWinRate: 'Win Rate',
  historyProblemUnavailable:
    'No local History database is available. Select the correct game directory on the Install page.',
  historyProblemReadFailed:
    'Local History could not be read. Close apps that may be using the database, then retry.',
  historyProblemBlockedByGame:
    'The Bazaar is still running in the background and holding the History database. Closing the game window does not always end its process; end it to read History.',
  historyEndGameProcess: 'End Game Process',
  historyEndGameProcessDone: 'Game process ended. Reloading History.',
  historyEndGameProcessNotFound:
    'The game process had already exited. Reloading History.',
  historyEndGameProcessFailedWindows:
    'The game process could not be ended. End TheBazaar.exe from Task Manager, then retry.',
  historyEndGameProcessFailedMac:
    'The game process could not be ended. Quit The Bazaar from Activity Monitor, then retry.',
  historyProblemUnsupportedSchema:
    'The History database schema is unsupported (found {found}, expected {expected}). Update the BazaarPlusPlus plugin or installer.',
  historyProblemPreviewUnavailable:
    'Runs are loaded, but thumbnails are unavailable. Start the local service from the Stream page.',
  historyProblemUnexpected:
    'Something unexpected happened while loading local History. Please retry.',
  historyOpenInstall: 'Open Install',
  historyOpenStream: 'Open Stream',
  historyPreviewFallback: 'Thumbnail unavailable; refresh to retry.',
  historyPreviewServiceOffline:
    'Thumbnail service is not running. Start it from the Stream page.',
  runResultVictory: 'VICTORY',
  runResultDefeat: 'DEFEAT',
  runResultAbandoned: 'ABANDONED',
  runResultActive: 'ACTIVE',
  runMetricWins: 'Wins',
  runMetricDays: 'Days',
  runStatRank: 'Rank',
  runStatRating: 'Rating',

  runDetailEyebrow: 'Run',
  runDetailBack: 'Back to History',
  runDetailLoading: 'Loading details',
  runDetailNotFound: 'This run was not found',
  runDetailUnavailable: 'Run unavailable',
  runDetailRefreshing: 'Refreshing details',
  runDetailProblemUnavailable:
    'No local History database is available. Select the correct game directory on the Install page.',
  runDetailProblemReadFailed:
    'This run could not be read. Close apps that may be using the database, then retry.',
  runDetailProblemRevealScreenshotFailed:
    'The screenshot location could not be opened. Please retry.',
  runDetailProblemRevealVideoFailed:
    'The video location could not be opened. Please retry.',
  runDetailProblemDeleteVideoFailed:
    'The battle video could not be deleted. Please retry.',
  runDetailProblemUnexpected:
    'Something unexpected happened while handling this run. Please retry.',
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
  noLocalBattlesDescription:
    'Battle records appear here automatically after a run finishes. You can also refresh to retry.',
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
  openVideoLocation: 'Open Video Location',
  deleteVideo: 'Delete Video',
  deleteVideoConfirmTitle: 'Delete Video',
  deleteVideoTarget: 'Target: battle {battleId} · video {videoId}',
  deleteVideoConfirmBody:
    'This permanently deletes the replay video file for this battle and cannot be undone.',
  deleteVideoConfirmAction: 'Delete Video',
  deleteVideoRunning: 'Deleting the target video…',

  storageCleanupTitle: 'Storage Cleanup',
  storageCleanupScreenshotsLabel: 'End-of-run screenshots',
  storageCleanupRunDataLabel: 'Run data',
  storageCleanupPresetAll: 'All',
  storageCleanupPresetOlderThan7Days: 'Older than 7 days',
  storageCleanupPresetBeforeThisMonth: 'Before this month',
  storageCleanupActionLabel: 'Clean {scope} · {preset}',
  storageCleanupConfirmTitle: 'Confirm Cleanup',
  storageCleanupTarget: 'Target: {scope} · Range: {preset}',
  storageCleanupScreenshotsConfirmBody:
    'This will permanently delete {count} end-of-run {count|screenshot|screenshots} (about {size}). This cannot be undone.',
  storageCleanupRunDataConfirmBody:
    'This will permanently delete {runs} {runs|run|runs}, including {battles} {battles|battle|battles}, {videos} replay {videos|video|videos} and related screenshots (about {size}). This cannot be undone.',
  storageCleanupSkippedPending:
    '{count} {count|item is|items are} still pending upload and will be skipped.',
  storageCleanupNothingToClean: 'Nothing matches the selected range.',
  storageCleanupConfirmAction: 'Clean Up',
  storageCleanupRunningScreenshots: 'Deleting end-of-run screenshots…',
  storageCleanupRunningRunData: 'Deleting run data…',
  storageCleanupProblemUnavailable:
    'No local History database is available to clean. Select the correct game directory on the Install page.',
  storageCleanupProblemPreviewFailed:
    'The cleanup range could not be previewed. Please retry.',
  storageCleanupProblemExecuteFailed:
    'Cleanup did not finish. The target and range are preserved; review diagnostics, then retry or close safely.',
  storageCleanupProblemUnexpected:
    'Something unexpected happened while cleaning storage. Please retry.',
  storageCleanupScreenshotsDone:
    'Deleted {files} {files|file|files}, freed about {size}.',
  storageCleanupRunDataDone:
    'Deleted {runs} {runs|run|runs} and {files} {files|file|files}, freed about {size}.',
  noVideo: 'No Video',

  streamDisplayModeLabel: 'Display style',
  streamModeCurrent: 'Battle Count',
  streamModeHero: 'Full Hero',
  streamModeHeroHalf: 'Half Hero',
  streamOpenOverlay: 'Preview Overlay',
  streamStart: 'Start Service',
  streamRestart: 'Restart Service',
  streamObsPlaceholder:
    'The OBS Browser Source URL appears after the stream service starts',
  streamObsGuide:
    'Add this URL as an OBS Browser Source to show the overlay on your stream',
  streamWindowSection: 'Display Window',
  streamWindowLatest: 'Showing the latest record',
  streamWindowOffset: 'Back {count} {count|record|records}',
  streamMoreHistory: 'More History',
  streamLessHistory: 'Less History',
  streamOverlayConfig: 'Overlay Config',
  streamCropCodeLabel: 'Crop code',
  streamCropCodePlaceholder: 'Enter crop code…',
  streamApplyCrop: 'Apply Crop Code',
  streamResetCrop: 'Reset Crop',
  streamOpenSettings: 'Calibrate Overlay',
  streamObsUrlLabel: 'OBS URL',
  streamInfoHost: 'Host',
  streamInfoPort: 'Port',
  streamInfoDb: 'DB',
  streamInfoWindow: 'Window',
  streamStatusError: 'Stream Service Error',
  streamStatusStarting: 'Stream Service Starting',
  streamStatusRunning: 'Stream Service Running',
  streamStatusIdle: 'Stream Service Stopped',
  streamStatusStale: 'Stream Service Status May Be Stale',
  streamStatusUnavailable: 'Stream Service Status Unavailable',
  streamStarting: 'Starting the local stream service',
  streamIdleDetail: 'The stream service is not running',
  streamStaleRunningDetail:
    'Last seen running; waiting for an up-to-date stream service status',
  streamStaleIdleDetail:
    'Last seen stopped; waiting for an up-to-date stream service status',
  streamStatusUnavailableDetail:
    'No authoritative stream service status is available',
  streamPortDetail: 'Port {port}',
  streamRetryStatus: 'Refresh Status',
  streamRetryCrop: 'Reload Config',
  streamCopied: 'OBS URL copied',
  streamCopyFailed: 'Copy failed. Select the text and copy manually.',
  streamCropSaved: 'Crop code saved',
  streamCropReset: 'Crop settings reset to default',
  streamProblemServiceFailed:
    'The stream service could not start. Make sure port 17654 is available, then retry.',
  streamProblemRestartFailed:
    'The stream service could not restart. Make sure port 17654 is available, then retry.',
  streamProblemPollFailed:
    'The latest stream service status could not be confirmed. The previous value is marked stale; refresh it.',
  streamProblemWindowFailed: 'The display window could not be changed. Retry.',
  streamProblemCropLoadFailed:
    'Overlay configuration could not be loaded. Other stream controls remain available; reload it.',
  streamProblemCropSaveFailed:
    'Overlay configuration could not be saved. Check the crop code and retry.',
  streamProblemOpenOverlayFailed:
    'The overlay preview could not be opened. Retry.',
  streamProblemOpenSettingsFailed:
    'The overlay calibration page could not be opened. Retry.',
  streamProblemUnexpected:
    'Something unexpected happened while handling Stream. Please retry.',
  dbConnected: 'DB Connected',
  dbMissing: 'DB Missing',

  aboutAppLabel: 'App',
  aboutBppLabel: 'Plugin',
  aboutCredits: 'Credits',
  aboutContributors: 'Contributors',
  aboutAcknowledgements: 'Data & Inspiration',
  aboutLicenses: 'Licenses',
  aboutVerifiedBadge: 'Fable 5 Verified',
  aboutLoadingBootstrapOnly: 'Loading native app information.',
  aboutLoadingBootstrap:
    'Loading native app information. Packaged fallback data is shown for now.',
  aboutFallbackPreview:
    'Browser Preview is showing fallback data packaged with the app.',
  aboutProblemBootstrapFailed:
    'Native app information could not be loaded. Usable fallback data remains available; please retry.',
  aboutUnavailableValue: 'Unavailable',
  aboutRetrying: 'Retrying',
  aboutBlockingFailure:
    'App information could not be loaded and no usable fallback data is available. Please retry.',

  installOverviewHealthyShort: 'All components are running normally',
  installOverviewUpdateDescription:
    'Component versions differ; reinstall to finish updating',
  installOverviewNotInstalledDescription:
    'Choose the game directory to install the plugin',
  installationDirectoryHeading: 'Game Directory',
  copyPath: 'Copy Path',
  pathCopied: 'Copied',
  selectDirectory: 'Choose Directory',
  applicationVersionHeading: 'App Version',
  developmentBuild: 'Development',
  stableBuild: 'Stable',
  maintenanceToolsHeading: 'Maintenance Tools',
  maintenanceResetDataDescription:
    'Delete the local database, screenshots, and replay videos',
  maintenanceNoResettableDataDescription: 'No local data to delete right now',
  maintenanceResetBepinexDescription: 'Reset the plugin environment',
  checkUpdateDescription: 'Fetch the latest version information',
  maintenanceUninstallDescription: 'Remove all components',
  installModalSubtitle: 'Pre-install confirmation and environment check',
  historySummaryRunsDescription: 'Total recorded runs',
  historySummaryVideosDescription: 'Recorded videos',
  historySummaryWinRateDescription: 'Across completed runs',
  historySummaryWinRateUnavailable: 'No win-rate data yet',
  storageCleanupScreenshotsDescription: 'Screenshot files saved at run end',
  storageCleanupRunDataDescription: 'Data files generated by completed runs',
  aboutTagline: 'Data enhancement and analysis for The Bazaar'
};

export const messages: Record<Locale, Record<MessageKey, string>> = { zh, en };

export type TranslateParams = Record<string, string | number>;

// `{name|singular|plural}` selects on the numeric value of `params[name]`.
const PLURAL_TOKEN = /\{([a-zA-Z0-9_]+)\|([^|{}]*)\|([^|{}]*)\}/g;

export function formatMessage(
  locale: Locale,
  key: MessageKey,
  params?: TranslateParams
): string {
  let text = messages[locale][key];
  if (!params) {
    return text;
  }
  text = text.replace(PLURAL_TOKEN, (match, name, one, other) => {
    const value = params[name];
    if (value === undefined) return match;
    return Number(value) === 1 ? one : other;
  });
  for (const [name, value] of Object.entries(params)) {
    text = text.replaceAll(`{${name}}`, String(value));
  }
  return text;
}

function normalizeLocale(value: string | undefined | null): Locale | null {
  if (!value) return null;
  const lower = value.toLowerCase();
  if (lower.startsWith('zh')) return 'zh';
  if (lower.startsWith('en')) return 'en';
  return null;
}

export function resolveInitialLocale(): Locale {
  if (typeof window === 'undefined') {
    return defaultLocale;
  }
  const saved = window.localStorage.getItem(LOCALE_STORAGE_KEY);
  if (saved === 'en' || saved === 'zh') {
    return saved;
  }
  // No stored choice yet: follow the host language before falling back to zh.
  const languages =
    typeof navigator === 'undefined'
      ? []
      : [...(navigator.languages ?? []), navigator.language];
  for (const language of languages) {
    const resolved = normalizeLocale(language);
    if (resolved) return resolved;
  }
  return defaultLocale;
}
