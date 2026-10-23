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
  hideToTray: '隐藏到托盘',
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
  kofiSubtitle: '请作者喝杯咖啡',
  supporterList: '支持者名单',
  wechatPayTagline: '请 Bazaar++ 喝一杯',
  supportLine1: '有你支持，Bazaar++ 会冒出更多好东西',
  supportLine2: '如果愿意，欢迎在备注里留一个支持者 ID',

  // Navigation rail
  navInstall: '安装',
  navHistory: '战绩',
  navStream: '直播',
  navAbout: '关于',
  navSupport: '支持',
  navLanguage: 'EN',

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
  actionDelete: '删除',
  launchGame: '启动游戏',
  installed: '已安装',
  notInstalled: '未安装',
  ready: '就绪',
  modNeedsReinstall: '需要重新安装',
  installDone: '安装完成',
  resetDataConfirmTitle: '删除本地数据',
  resetDataTarget: '目标：{path} 内的 BazaarPlusPlusV6 文件夹',
  resetDataConfirmBody:
    '这会删除 The Bazaar 游戏目录中 BazaarPlusPlusV6 下的本地数据库、截图和回放。这包括尚未上传的对局数据。',
  resetDataConfirmKeepsInstall:
    '不会卸载 The Bazaar，也不会卸载 BazaarPlusPlus 插件。',
  resetDataConfirmGameClosed: '请先退出 The Bazaar，避免数据文件仍被占用。',
  resetDataConfirmAcknowledge: '我知道这些本地数据会被删除。',
  resetDataConfirmAction: '删除本地数据',
  resetDataRunning: '正在删除目标本地数据…',
  resetDataDone: '本地数据已删除',
  resetDataNothingToDelete: '未找到可删除的本地数据',
  resetDataBlockedByGame:
    'The Bazaar 仍在运行，或无法确认游戏已退出。请先退出游戏，再删除本地数据。',
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
    'The Bazaar 仍在运行，或无法确认游戏已退出。请先退出游戏，再删除 BepInEx 文件夹。',
  resetBepinexPartialFailure: '有 {count} 个项目未能删除。请关闭游戏后重试。',
  operationCannotBeCancelled: '操作已开始，完成前无法取消或关闭此窗口。',
  uninstallConfirmTitle: '卸载 BazaarPlusPlus',
  uninstallTarget: '目标：{path}',
  uninstallConfirmBody:
    '将移除 BepInEx 与插件文件。本地数据（数据库、截图与回放）和游戏本体会保留。',
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
  installBlockedByGame:
    'The Bazaar 仍在运行，或无法确认游戏已退出。请先退出游戏，再安装 BazaarPlusPlus。',
  importBlockedByGame:
    'The Bazaar 仍在运行，或无法确认游戏已退出。请先退出游戏，再导入 V5 战绩。',
  legacyDeleteBlockedByGame:
    'The Bazaar 仍在运行，或无法确认游戏已退出。请先退出游戏，再删除旧数据文件夹。',
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
  installProblemLegacyDataFailed: '无法读取旧版本数据文件夹，请重试。',
  installProblemLegacyDeleteFailed:
    '删除旧数据文件夹失败，请关闭占用文件的程序后重试。',

  // Legacy Roots
  legacyDataHeading: '旧版本数据',
  legacyDataHint:
    '早期版本写入游戏目录的数据文件夹。当前版本不再读取它们，只有在你确认后才会删除。',
  legacyRootSize: '{size}，{files} 个文件',
  legacyRootHardLinked:
    '其中 {count} 个文件与其他位置共用磁盘空间，删除后实际释放的空间可能更少。',
  legacyRootUnreadable: '有 {count} 个项目无法读取，未计入大小。',
  legacyModReinstallRequired:
    '已安装的 BazaarPlusPlus 仍在写入旧的数据文件夹。请重新安装，让它改用 BazaarPlusPlusV6。',
  legacyDeleteAction: '删除 {name}',
  legacyDeleteConfirmTitle: '删除旧数据文件夹',
  legacyDeleteTarget: '目标：{path} 内的 {name} 文件夹',
  legacyDeleteConfirmBody:
    '这会永久删除 {name} 中的全部内容，包括早期版本的数据库、截图和回放。',
  legacyDeleteConfirmSpace:
    '部分文件可能与其他位置共用磁盘空间，删除后释放的空间可能少于显示的大小。',
  legacyDeleteConfirmKeepsCurrent:
    '当前数据（BazaarPlusPlusV6）、插件和游戏本体都不会受影响。',
  legacyDeleteConfirmAcknowledge: '我知道 {name} 文件夹会被永久删除。',
  legacyDeleteConfirmAction: '删除文件夹',
  legacyDeleteRunning: '正在删除旧数据文件夹…',
  legacyDeleteDone: '{name} 已删除',
  legacyDeleteNothingToDelete: '{name} 已不存在',
  legacyDeletePartialFailure: '有 {count} 个项目未能删除。请关闭游戏后重试。',
  legacyDeleteAfterImport:
    '导入完成，BazaarPlusPlusV6 中新增 {imported} 局。导入的对局只保存在本地，不会上传。',
  legacyDeleteAfterImportSkipped:
    '有 {count} 局 BazaarPlusPlusV6 中已经存在，未重复导入。',
  legacyDeleteAfterImportUnavailableFiles:
    '有 {count} 个截图或视频文件不在 BazaarPlusPlusV5 中，未能导入。',
  legacyDeleteAfterImportOffer:
    '现在可以删除 BazaarPlusPlusV5，也可以保留它。保留的话，它会继续列在“旧版本数据”中，之后随时可以删除。',
  legacyDeleteKeep: '保留',

  // V5 import
  legacyImportHeading: '导入 V5 战绩',
  legacyImportHint:
    '把 BazaarPlusPlusV5 中的对局、截图和回放导入 BazaarPlusPlusV6，只需一次。导入的对局只显示在本地战绩中，不会上传；BazaarPlusPlusV5 保持不变。',
  legacyImportAction: '导入',
  legacyImportRunning:
    '正在导入 V5 战绩。请保持游戏关闭，完成前不要退出安装器。',
  importFailedAlreadyImported: 'V5 战绩已经导入过，无需再次导入。',
  importFailedNotEligible:
    '现在无法导入。请确认已安装当前版本的 BazaarPlusPlus，且 BazaarPlusPlusV5 文件夹仍在游戏目录中。',
  importFailedModContract:
    '已安装的 BazaarPlusPlus 缺少导入所需的信息。请重新安装后再导入。',
  importFailedV5Database:
    'BazaarPlusPlusV5 中的战绩数据库缺失或无法识别，无法导入。BazaarPlusPlusV6 未被修改。',
  importFailedInsufficientSpace:
    '磁盘空间不足，无法复制 V5 的文件。导入已撤销，请释放空间后重试。',
  importFailedFiles:
    '复制 V5 的截图或回放文件失败，导入已撤销。请关闭占用这些文件的程序后重试。',
  importFailedTarget: '写入 BazaarPlusPlusV6 失败，导入已撤销。请重试。',
  importFailedMarker:
    '战绩已导入，但未能在 BazaarPlusPlusV5 中写入导入标记。请重试以完成，已导入的对局不会重复。',

  // Install confirmation modal
  installModalTitle: '安装 BazaarPlusPlus',
  tutorialKicker: '使用教程',
  installModalBody: '安装完成后，从 Steam 启动游戏即可使用。',
  viewTutorial: '查看教程',
  installSteamNotice:
    '安装前请先关闭 Steam；如果 Steam 正在运行，请手动退出后再继续。',
  installSteamNoticeMacos: '请先退出游戏。继续安装将关闭 Steam。',
  installCloseGameNotice: '请先退出游戏，再继续安装。',
  installAcknowledge: '我了解使用第三方插件的风险，并自行承担。',
  installing: '安装中…',
  confirmInstall: '确认安装',

  // Update check (header)
  updaterPreview: '浏览器预览',
  updaterCurrent: '已是最新',
  headerCheckFailed: '检查失败',
  updateHeaderAvailable: '发现更新',
  updateModalTitle: '发现新版本',
  updateModalBody: 'BazaarPlusPlus {version} 已可用。',
  updateMainlandDownloadTitle: '中国大陆下载',
  updateMainlandDownloadHint: '自动更新较慢时，可通过大陆渠道手动下载。',
  updateMainlandDownloadFailedHint:
    '自动更新失败时，可通过大陆渠道下载安装包手动安装。',
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
  historyEmptyDescription: '完成一局游戏后，战绩会出现在这里。',
  historyEmptyRefresh: '刷新战绩',
  historyEmptyInstall: '检查游戏目录',
  viewDetail: '查看详情',
  historySummaryRuns: '对局数',
  historySummaryVideos: '视频数',
  historySummaryWinRate: '十胜达成率',
  historyOverviewLabel: '战绩概览',
  historyPagination: '战绩分页',
  historyPageRange: '第 {start}–{end} 局，共 {total} 局',
  historyPageNumber: '{page} / {total} 页',
  historyPreviousPage: '上一页',
  historyNextPage: '下一页',
  runModeRanked: '排位',
  runModeUnranked: '匹配',
  runModeNormal: '普通',
  // Game data stays English at rest; History translates heroes and rank tiers
  // at display time. Names follow the game's official Simplified Chinese copy.
  heroVanessa: '凡妮莎',
  heroPygmalien: '皮格马利翁',
  heroDooley: '杜利',
  heroMak: '马克',
  heroJules: '朱尔斯',
  heroStelle: '斯黛拉',
  heroKarnok: '卡诺克',
  heroTheDragons: '双龙',
  rankBronze: '青铜',
  rankSilver: '白银',
  rankGold: '黄金',
  rankDiamond: '钻石',
  rankMaster: '大师',
  rankLegendary: '传奇',
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
    '战绩数据库版本不受支持（当前 {found}，支持 {supported}）。请更新 BazaarPlusPlus 插件或安装器。',
  historyProblemUnexpected: '加载本地战绩时发生意外错误。请重试。',
  historyOpenInstall: '前往安装页',
  historyUnavailableImport:
    '还没有 BazaarPlusPlusV6 战绩数据库，但游戏目录中有 BazaarPlusPlusV5 的战绩。可以在安装页导入它们。',
  historyUnavailableReinstall:
    '游戏目录中有 BazaarPlusPlusV5 的战绩，但已安装的 BazaarPlusPlus 仍在写入旧文件夹。请在安装页重新安装，然后导入。',
  historyOpenImport: '前往导入',
  historyPreviewFallback: '缩略图不可用；刷新页面可重试。',
  historyPreviewEmpty: '暂无截图',
  historyPreviewLoading: '正在加载截图…',
  historyProblemThumbnailsUnavailable:
    '缩略图服务无法启动，本地端口可能被占用；战绩仍可正常浏览。',
  runResultMisfortune: '惨淡旅程',
  runResultBronze: '青铜胜利',
  runResultSilver: '白银胜利',
  runResultGold: '黄金胜利',
  runResultDiamond: '钻石胜利',
  runResultAbandoned: '中途放弃',
  runResultActive: '正在进行',
  runMetricWins: '胜场',
  runMetricDays: '天数',
  runStatRank: '段位',
  runStatRating: '段位分',

  // Run detail page
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
  deleteVideoRunDate: '对局时间',
  deleteVideoBattle: '战斗',
  deleteVideoBattleDescription: '第 {day} 天 · {opponent}',
  deleteVideoDuration: '视频时长',
  deleteVideoTechnicalDetails: '技术详情',
  battleVideoActionLabel: '{action}：第 {day} 天，对手 {opponent}',
  deleteVideoConfirmBody: '这会永久删除该场战斗的回放文件，且无法恢复。',
  deleteVideoConfirmAction: '删除视频',
  deleteVideoRunning: '正在删除目标视频…',

  // Storage cleanup (History page)
  storageCleanupTitle: '存储管理',
  storageCleanupRangeLabel: '{scope}的清理时间范围',
  storageCleanupPreviewLabel: '预览清理{scope}',
  storageCleanupPreviewAction: '预览清理',
  storageCleanupScreenshotsLabel: '对局结算截图',
  storageCleanupRunDataLabel: '对局数据',
  storageCleanupPresetAll: '全部',
  storageCleanupPresetOlderThan7Days: '7 天前',
  storageCleanupPresetBeforeThisMonth: '本月以前',
  storageCleanupConfirmTitle: '确认清理',
  storageCleanupTarget: '目标：{scope} · 范围：{preset}',
  storageCleanupScreenshotsConfirmBody:
    '清理完成前请保持游戏关闭。将删除 {count} 张结算截图（约 {size}），删除后无法恢复。',
  storageCleanupRunDataConfirmBody:
    '清理完成前请保持游戏关闭。将删除 {runs} 局对局记录，包括 {battles} 场战斗、{videos} 个回放及相关截图（约 {size}），删除后无法恢复。',
  storageCleanupSkippedPending: '另有 {count} 项尚未完成上传，将自动跳过。',
  storageCleanupNothingToClean: '没有符合条件的可清理数据。',
  storageCleanupConfirmAction: '确认清理',
  storageCleanupRunningScreenshots: '正在删除结算截图…',
  storageCleanupRunningRunData: '正在删除对局数据…',
  storageCleanupProblemUnavailable:
    '未找到可清理的本地战绩数据库，请先在安装页选择正确的游戏目录。',
  storageCleanupProblemPreviewFailed: '无法预览清理范围，请重试。',
  storageCleanupProblemExecuteFailed:
    '清理未完成，目标和范围已保留；请确认游戏已关闭，并查看诊断后重试或安全关闭。',
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
  aboutLoadingBootstrap: '正在获取本机应用信息，暂时显示随应用打包的备用数据。',
  aboutFallbackPreview: '当前为浏览器预览，显示随应用打包的备用数据。',
  aboutProblemBootstrapFailed:
    '无法获取本机应用信息。已保留可用的备用数据，请重试。',
  aboutUnavailableValue: '不可用',
  aboutRetrying: '正在重试',

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
  maintenanceToolsHeading: '高级操作',
  maintenanceToolsHint: '卸载插件、重置 BepInEx 或删除本地数据',
  maintenanceResetDataDescription: '删除本地数据库、截图与回放',
  maintenanceNoResettableDataDescription: '当前没有可删除的本地数据',
  maintenanceResetBepinexDescription: '重置插件环境',
  maintenanceUninstallDescription: '移除所有组件',
  historySummaryWinRateDescription: '十胜对局 / 已完成对局',
  historySummaryWinRateUnavailable: '尚无已完成对局',
  storageCleanupScreenshotsDescription: '保存对局结算时的截图文件',
  storageCleanupRunDataDescription: '保存对局产生的各类数据文件',
  aboutTagline: 'The Bazaar 数据增强与分析工具'
} as const;

export type MessageKey = keyof typeof zh;

const en: Record<MessageKey, string> = {
  htmlLang: 'en',

  languageToggle: '切换中文',
  headerCheckUpdate: 'Check updates',
  headerCheckingUpdate: 'Checking',
  supportProject: 'Support',
  primaryNavigation: 'Primary navigation',

  windowControls: 'Window controls',
  minimizeWindow: 'Minimize window',
  maximizeWindow: 'Maximize window',
  restoreWindow: 'Restore window',
  closeWindow: 'Close window',
  hideToTray: 'Hide to tray',
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
  kofiSubtitle: 'Buy the author a coffee',
  supporterList: 'Supporters',
  wechatPayTagline: 'Buy Bazaar++ a drink',
  supportLine1: 'With your support, Bazaar++ keeps growing new things',
  supportLine2: 'If you like, leave a supporter ID in the note',

  navInstall: 'Install',
  navHistory: 'History',
  navStream: 'Stream',
  navAbout: 'About',
  navSupport: 'Support',
  navLanguage: '中文',

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
  actionChooseDirectory: 'Choose game directory',
  actionInstall: 'Install',
  actionReinstall: 'Reinstall',
  actionResetData: 'Delete local data',
  actionNoResettableData: 'No local data',
  actionResetBepinex: 'Delete BepInEx folder',
  actionUninstall: 'Uninstall',
  actionDelete: 'Delete',
  launchGame: 'Launch game',
  installed: 'Installed',
  notInstalled: 'Not installed',
  ready: 'Ready',
  modNeedsReinstall: 'Reinstall required',
  installDone: 'Install complete',
  resetDataConfirmTitle: 'Delete local data',
  resetDataTarget: 'Target: the BazaarPlusPlusV6 folder inside {path}',
  resetDataConfirmBody:
    'This deletes the local database, screenshots, and replays under BazaarPlusPlusV6 in The Bazaar install directory. This includes battle data that has not been uploaded yet.',
  resetDataConfirmKeepsInstall:
    'This does not uninstall The Bazaar or the BazaarPlusPlus plugin.',
  resetDataConfirmGameClosed:
    'Quit The Bazaar first so data files are not held open.',
  resetDataConfirmAcknowledge: 'I understand this local data will be deleted.',
  resetDataConfirmAction: 'Delete local data',
  resetDataRunning: 'Deleting the target local data…',
  resetDataDone: 'Local data deleted',
  resetDataNothingToDelete: 'No local data found to delete',
  resetDataBlockedByGame:
    'The Bazaar is still running, or the installer could not confirm that it has closed. Quit the game before deleting local data.',
  resetDataPartialFailure:
    '{count} local data {count|item|items} could not be deleted. Close the game and stream sources, then try again.',
  resetDataFailureDetails: 'Show undeleted items',
  resetDataFailureCopy: 'Copy diagnostics',
  resetDataFailureCopied: 'Copied',
  resetDataFailureCopyFailed: 'Copy failed. Select the text and copy manually.',
  resetBepinexConfirmTitle: 'Delete BepInEx folder',
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
  resetBepinexConfirmAction: 'Delete BepInEx folder',
  resetBepinexRunning: 'Deleting the target BepInEx folder…',
  resetBepinexDone: 'BepInEx folder deleted',
  resetBepinexNothingToDelete: 'No BepInEx folder found',
  resetBepinexBlockedByGame:
    'The Bazaar is still running, or the installer could not confirm that it has closed. Quit the game before deleting the BepInEx folder.',
  resetBepinexPartialFailure:
    '{count} {count|item|items} could not be deleted. Close the game, then try again.',
  operationCannotBeCancelled:
    'This operation has started and cannot be canceled or dismissed until it finishes.',
  uninstallConfirmTitle: 'Uninstall BazaarPlusPlus',
  uninstallTarget: 'Target: {path}',
  uninstallConfirmBody:
    'This removes BepInEx and the plugin files. Local data (database, screenshots and replays) and the base game are kept.',
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
  installBlockedByGame:
    'The Bazaar is still running, or the installer could not confirm that it has closed. Quit the game before installing BazaarPlusPlus.',
  importBlockedByGame:
    'The Bazaar is still running, or the installer could not confirm that it has closed. Quit the game before importing V5 history.',
  legacyDeleteBlockedByGame:
    'The Bazaar is still running, or the installer could not confirm that it has closed. Quit the game before deleting the legacy data folder.',
  installProblemDetectionFailed:
    'Installation state could not be detected. Try again.',
  installProblemChooseDirectoryFailed:
    'The game directory picker could not be opened. Try again.',
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
    'Something unexpected happened while handling installation state. Try again.',
  installProblemLegacyDataFailed:
    'Data folders from earlier versions could not be read. Try again.',
  installProblemLegacyDeleteFailed:
    'The legacy data folder could not be deleted. Close apps using those files, then retry.',

  legacyDataHeading: 'Data from earlier versions',
  legacyDataHint:
    'Folders earlier versions wrote into the game directory. This version no longer reads them, and they are deleted only after you confirm.',
  legacyRootSize: '{size}, {files} {files|file|files}',
  legacyRootHardLinked:
    '{count} {count|file shares|files share} disk space with another location, so deleting may free less space.',
  legacyRootUnreadable:
    '{count} {count|item|items} could not be read and {count|is|are} not counted.',
  legacyModReinstallRequired:
    'The installed BazaarPlusPlus still writes to an old data folder. Reinstall it so it uses BazaarPlusPlusV6.',
  legacyDeleteAction: 'Delete {name}',
  legacyDeleteConfirmTitle: 'Delete legacy data folder',
  legacyDeleteTarget: 'Target: the {name} folder inside {path}',
  legacyDeleteConfirmBody:
    'This permanently deletes everything in {name}, including databases, screenshots, and replays from earlier versions.',
  legacyDeleteConfirmSpace:
    'Some files may share disk space with another location, so deleting may free less space than shown.',
  legacyDeleteConfirmKeepsCurrent:
    'Current data (BazaarPlusPlusV6), the mod, and the game are not affected.',
  legacyDeleteConfirmAcknowledge:
    'I understand the {name} folder will be permanently deleted.',
  legacyDeleteConfirmAction: 'Delete folder',
  legacyDeleteRunning: 'Deleting the legacy data folder…',
  legacyDeleteDone: '{name} deleted',
  legacyDeleteNothingToDelete: '{name} no longer exists',
  legacyDeletePartialFailure:
    '{count} {count|item|items} could not be deleted. Close the game, then try again.',
  legacyDeleteAfterImport:
    'Import complete: {imported} {imported|run was|runs were} added to BazaarPlusPlusV6. Imported runs stay local and are never uploaded.',
  legacyDeleteAfterImportSkipped:
    '{count} {count|run was|runs were} already in BazaarPlusPlusV6 and {count|was|were} not imported again.',
  legacyDeleteAfterImportUnavailableFiles:
    '{count} {count|screenshot or video file was|screenshot or video files were} not in BazaarPlusPlusV5 and could not be imported.',
  legacyDeleteAfterImportOffer:
    'You can delete BazaarPlusPlusV5 now or keep it. If you keep it, it stays listed under Data from earlier versions, and you can delete it later.',
  legacyDeleteKeep: 'Keep folder',

  legacyImportHeading: 'Import V5 History',
  legacyImportHint:
    'Import the runs, screenshots, and replays in BazaarPlusPlusV5 into BazaarPlusPlusV6, once. Imported runs appear only in local History and are never uploaded; BazaarPlusPlusV5 is left as it is.',
  legacyImportAction: 'Import',
  legacyImportRunning:
    'Importing V5 history. Keep the game closed and do not quit the installer until it finishes.',
  importFailedAlreadyImported:
    'V5 history was already imported; there is nothing to import again.',
  importFailedNotEligible:
    'The import cannot run now. Make sure the current BazaarPlusPlus is installed and the BazaarPlusPlusV5 folder is still in the game directory.',
  importFailedModContract:
    'The installed BazaarPlusPlus lacks what the import needs. Reinstall it, then import.',
  importFailedV5Database:
    'The History database in BazaarPlusPlusV5 is missing or not recognized, so it cannot be imported. BazaarPlusPlusV6 was not changed.',
  importFailedInsufficientSpace:
    'There is not enough disk space to copy the V5 files. The import was undone; free some space, then retry.',
  importFailedFiles:
    'V5 screenshots or replays could not be copied, so the import was undone. Close apps using those files, then retry.',
  importFailedTarget:
    'BazaarPlusPlusV6 could not be written, so the import was undone. Try again.',
  importFailedMarker:
    'History was imported, but the import marker could not be written to BazaarPlusPlusV5. Retry to finish; imported runs are not duplicated.',

  installModalTitle: 'Install BazaarPlusPlus',
  tutorialKicker: 'Tutorial',
  installModalBody:
    'After installation, launch the game from Steam to use the mod.',
  viewTutorial: 'View tutorial',
  installSteamNotice:
    'Close Steam before installing. If Steam is running, quit it manually before continuing.',
  installSteamNoticeMacos: 'Close the game first. Continuing will quit Steam.',
  installCloseGameNotice: 'Close the game before continuing.',
  installAcknowledge:
    'I understand and accept the risks of using third-party mods.',
  installing: 'Installing…',
  confirmInstall: 'Confirm Install',

  updaterPreview: 'Preview mode',
  updaterCurrent: 'Up to date',
  headerCheckFailed: 'Check failed',
  updateHeaderAvailable: 'Update available',
  updateModalTitle: 'Update available',
  updateModalBody: 'BazaarPlusPlus {version} is available.',
  updateMainlandDownloadTitle: 'Mainland China download',
  updateMainlandDownloadHint:
    'If automatic updates are slow, use the mainland download channel.',
  updateMainlandDownloadFailedHint:
    'If the automatic update failed, download the installer from the mainland channel and install it manually.',
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
  updateRestartNow: 'Restart now',
  updateRestarting: 'Restarting…',
  updateError: 'Update failed',
  updateRestartFailedTitle: 'Automatic restart failed',
  updateRetry: 'Retry',
  updateRetryRestart: 'Try restart again',
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
  historyEmptyDescription: 'Finish a run and it appears here.',
  historyEmptyRefresh: 'Refresh History',
  historyEmptyInstall: 'Check game directory',
  viewDetail: 'View details',
  historySummaryRuns: 'Runs',
  historySummaryVideos: 'Videos',
  historySummaryWinRate: '10-win rate',
  historyOverviewLabel: 'History overview',
  historyPagination: 'History pages',
  historyPageRange: 'Runs {start}–{end} of {total}',
  historyPageNumber: 'Page {page} of {total}',
  historyPreviousPage: 'Previous',
  historyNextPage: 'Next',
  runModeRanked: 'Ranked',
  runModeUnranked: 'Unranked',
  runModeNormal: 'Normal',
  heroVanessa: 'Vanessa',
  heroPygmalien: 'Pygmalien',
  heroDooley: 'Dooley',
  heroMak: 'Mak',
  heroJules: 'Jules',
  heroStelle: 'Stelle',
  heroKarnok: 'Karnok',
  heroTheDragons: 'The Dragons',
  rankBronze: 'Bronze',
  rankSilver: 'Silver',
  rankGold: 'Gold',
  rankDiamond: 'Diamond',
  rankMaster: 'Master',
  rankLegendary: 'Legendary',
  historyProblemUnavailable:
    'No local History database is available. Select the correct game directory on the Install page.',
  historyProblemReadFailed:
    'Local History could not be read. Close apps that may be using the database, then retry.',
  historyProblemBlockedByGame:
    'The Bazaar is still running in the background and holding the History database. Closing the game window does not always end its process; end it to read History.',
  historyEndGameProcess: 'End game process',
  historyEndGameProcessDone: 'Game process ended. Reloading History.',
  historyEndGameProcessNotFound:
    'The game process had already exited. Reloading History.',
  historyEndGameProcessFailedWindows:
    'The game process could not be ended. End TheBazaar.exe from Task Manager, then retry.',
  historyEndGameProcessFailedMac:
    'The game process could not be ended. Quit The Bazaar from Activity Monitor, then retry.',
  historyProblemUnsupportedSchema:
    'The History database schema is unsupported (found {found}, supported {supported}). Update the BazaarPlusPlus plugin or installer.',
  historyProblemUnexpected:
    'Something unexpected happened while loading local History. Try again.',
  historyOpenInstall: 'Open Install',
  historyUnavailableImport:
    'There is no BazaarPlusPlusV6 History database yet, but the game directory holds BazaarPlusPlusV5 history. Import it on the Install page.',
  historyUnavailableReinstall:
    'The game directory holds BazaarPlusPlusV5 history, but the installed BazaarPlusPlus still writes to an old folder. Reinstall it on the Install page, then import.',
  historyOpenImport: 'Open import',
  historyPreviewFallback: 'Thumbnail unavailable; refresh to retry.',
  historyPreviewEmpty: 'No screenshot',
  historyPreviewLoading: 'Loading screenshot…',
  historyProblemThumbnailsUnavailable:
    "Thumbnails can't load: the local image service couldn't start (its port may be in use). History still works.",
  runResultMisfortune: 'Misfortune journey',
  runResultBronze: 'Bronze victory',
  runResultSilver: 'Silver victory',
  runResultGold: 'Gold victory',
  runResultDiamond: 'Diamond victory',
  runResultAbandoned: 'Abandoned',
  runResultActive: 'Active',
  runMetricWins: 'Wins',
  runMetricDays: 'Days',
  runStatRank: 'Rank',
  runStatRating: 'Rating',

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
    'The screenshot location could not be opened. Try again.',
  runDetailProblemRevealVideoFailed:
    'The video location could not be opened. Try again.',
  runDetailProblemDeleteVideoFailed:
    'The battle video could not be deleted. Try again.',
  runDetailProblemUnexpected:
    'Something unexpected happened while handling this run. Try again.',
  runDetailPlayer: 'Player',
  runStatusCompleted: 'Completed',
  runStatusAbandoned: 'Abandoned',
  runStatusActive: 'Active',
  openScreenshotLocation: 'Open screenshot location',
  statWinLoss: 'Wins / Losses',
  statFinalDay: 'Final day',
  statFinalRank: 'Final rank',
  statFinalRating: 'Rating',
  noLocalBattles: 'No local battle records',
  noLocalBattlesDescription:
    'Battle records appear here automatically after a run finishes. You can also refresh to retry.',
  battleColDay: 'Day',
  battleColResult: 'Result',
  battleColOpponentHero: 'Opponent hero',
  battleColOpponentPlayer: 'Opponent player',
  battleColRank: 'Rank',
  battleColRating: 'Rating',
  battleColVideo: 'Video',
  battleResultWin: 'Win',
  battleResultLoss: 'Loss',
  battleResultNeutral: 'Draw',
  openVideoLocation: 'Open video location',
  deleteVideo: 'Delete video',
  deleteVideoConfirmTitle: 'Delete video',
  deleteVideoTarget: 'Target: battle {battleId} · video {videoId}',
  deleteVideoRunDate: 'Run started',
  deleteVideoBattle: 'Battle',
  deleteVideoBattleDescription: 'Day {day} · {opponent}',
  deleteVideoDuration: 'Video duration',
  deleteVideoTechnicalDetails: 'Technical details',
  battleVideoActionLabel: '{action}: day {day}, opponent {opponent}',
  deleteVideoConfirmBody:
    'This permanently deletes the replay file for this battle and cannot be undone.',
  deleteVideoConfirmAction: 'Delete video',
  deleteVideoRunning: 'Deleting the target video…',

  storageCleanupTitle: 'Storage',
  storageCleanupRangeLabel: 'Cleanup time range for {scope}',
  storageCleanupPreviewLabel: 'Review cleanup of {scope}',
  storageCleanupPreviewAction: 'Review cleanup',
  storageCleanupScreenshotsLabel: 'End-of-run screenshots',
  storageCleanupRunDataLabel: 'Run data',
  storageCleanupPresetAll: 'All',
  storageCleanupPresetOlderThan7Days: 'Older than 7 days',
  storageCleanupPresetBeforeThisMonth: 'Before this month',
  storageCleanupConfirmTitle: 'Confirm cleanup',
  storageCleanupTarget: 'Target: {scope} · Range: {preset}',
  storageCleanupScreenshotsConfirmBody:
    'Keep The Bazaar closed until cleanup finishes. This will permanently delete {count} end-of-run {count|screenshot|screenshots} (about {size}). This cannot be undone.',
  storageCleanupRunDataConfirmBody:
    'Keep The Bazaar closed until cleanup finishes. This will permanently delete {runs} {runs|run|runs}, including {battles} {battles|battle|battles}, {videos} replay {videos|video|videos} and related screenshots (about {size}). This cannot be undone.',
  storageCleanupSkippedPending:
    '{count} {count|item is|items are} still pending upload and will be skipped.',
  storageCleanupNothingToClean: 'Nothing matches the selected range.',
  storageCleanupConfirmAction: 'Clean up',
  storageCleanupRunningScreenshots: 'Deleting end-of-run screenshots…',
  storageCleanupRunningRunData: 'Deleting run data…',
  storageCleanupProblemUnavailable:
    'No local History database is available to clean. Select the correct game directory on the Install page.',
  storageCleanupProblemPreviewFailed:
    'The cleanup range could not be previewed. Try again.',
  storageCleanupProblemExecuteFailed:
    'Cleanup did not finish. The target and range are preserved; make sure The Bazaar is closed and review diagnostics, then retry or close safely.',
  storageCleanupProblemUnexpected:
    'Something unexpected happened while cleaning storage. Try again.',
  storageCleanupScreenshotsDone:
    'Deleted {files} {files|file|files}, freed about {size}.',
  storageCleanupRunDataDone:
    'Deleted {runs} {runs|run|runs} and {files} {files|file|files}, freed about {size}.',
  noVideo: 'No video',

  streamDisplayModeLabel: 'Display style',
  streamModeCurrent: 'Battle count',
  streamModeHero: 'Full hero',
  streamModeHeroHalf: 'Half hero',
  streamOpenOverlay: 'Preview overlay',
  streamStart: 'Start service',
  streamRestart: 'Restart service',
  streamObsPlaceholder:
    'The OBS Browser Source URL appears after the stream service starts',
  streamWindowSection: 'Display window',
  streamWindowLatest: 'Showing the latest record',
  streamWindowOffset: 'Back {count} {count|record|records}',
  streamMoreHistory: 'More History',
  streamLessHistory: 'Less History',
  streamOverlayConfig: 'Overlay config',
  streamCropCodeLabel: 'Crop code',
  streamCropCodePlaceholder: 'Enter crop code…',
  streamApplyCrop: 'Apply crop code',
  streamResetCrop: 'Reset crop',
  streamOpenSettings: 'Calibrate overlay',
  streamObsUrlLabel: 'OBS URL',
  streamInfoHost: 'Host',
  streamInfoPort: 'Port',
  streamInfoDb: 'DB',
  streamInfoWindow: 'Window',
  streamStatusError: 'Stream service error',
  streamStatusStarting: 'Stream service starting',
  streamStatusRunning: 'Stream service running',
  streamStatusIdle: 'Stream service stopped',
  streamStatusStale: 'Stream service status may be stale',
  streamStatusUnavailable: 'Stream service status unavailable',
  streamStarting: 'Starting the local stream service',
  streamIdleDetail: 'The stream service is not running',
  streamStaleRunningDetail:
    'Last seen running; waiting for an up-to-date stream service status',
  streamStaleIdleDetail:
    'Last seen stopped; waiting for an up-to-date stream service status',
  streamStatusUnavailableDetail:
    'No authoritative stream service status is available',
  streamPortDetail: 'Port {port}',
  streamRetryStatus: 'Refresh status',
  streamRetryCrop: 'Reload config',
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
    'Something unexpected happened while handling Stream. Try again.',
  dbConnected: 'DB connected',
  dbMissing: 'DB missing',

  aboutAppLabel: 'App',
  aboutBppLabel: 'Plugin',
  aboutCredits: 'Credits',
  aboutContributors: 'Contributors',
  aboutAcknowledgements: 'Data & inspiration',
  aboutLicenses: 'Licenses',
  aboutVerifiedBadge: 'Fable 5 verified',
  aboutLoadingBootstrap:
    'Loading native app information. Packaged fallback data is shown for now.',
  aboutFallbackPreview:
    'Browser Preview is showing fallback data packaged with the app.',
  aboutProblemBootstrapFailed:
    'Native app information could not be loaded. Usable fallback data remains available; please retry.',
  aboutUnavailableValue: 'Unavailable',
  aboutRetrying: 'Retrying',

  installOverviewHealthyShort: 'All components are running normally',
  installOverviewUpdateDescription:
    'Component versions differ; reinstall to finish updating',
  installOverviewNotInstalledDescription:
    'Choose the game directory to install the plugin',
  installationDirectoryHeading: 'Game directory',
  copyPath: 'Copy path',
  pathCopied: 'Copied',
  selectDirectory: 'Choose directory',
  applicationVersionHeading: 'App version',
  developmentBuild: 'Development',
  stableBuild: 'Stable',
  maintenanceToolsHeading: 'Advanced',
  maintenanceToolsHint: 'Uninstall, reset BepInEx, or delete local data',
  maintenanceResetDataDescription:
    'Delete the local database, screenshots, and replays',
  maintenanceNoResettableDataDescription: 'No local data to delete right now',
  maintenanceResetBepinexDescription: 'Reset the plugin environment',
  maintenanceUninstallDescription: 'Remove all components',
  historySummaryWinRateDescription: '10-win runs / completed runs',
  historySummaryWinRateUnavailable: 'No completed runs yet',
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
