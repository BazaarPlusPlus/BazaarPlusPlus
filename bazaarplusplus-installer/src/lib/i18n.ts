export type Locale = 'en' | 'zh';

export type MessageKey =
  | 'htmlLang'
  | 'pageTitle'
  | 'kicker'
  | 'subtitle'
  | 'stepBpp'
  | 'stepBazaar'
  | 'stepActions'
  | 'statusChecking'
  | 'statusNotInstalled'
  | 'detectInstalledHint'
  | 'statusFound'
  | 'actionReenter'
  | 'actionBrowse'
  | 'placeholderGamePath'
  | 'actionCheck'
  | 'errorGamePath'
  | 'actionInstalling'
  | 'actionReinstall'
  | 'actionInstall'
  | 'actionRepairing'
  | 'actionRepair'
  | 'resetHistoryTitle'
  | 'resetHistoryBody'
  | 'actionUninstalling'
  | 'actionUninstall'
  | 'footer'
  | 'aboutTitle'
  | 'aboutBack'
  | 'aboutOpenSource'
  | 'aboutInspiredBy'
  | 'aboutDependencies'
  | 'aboutDataSources'
  | 'aboutInfo'
  | 'aboutAuthors'
  | 'aboutAuthorRole'
  | 'aboutCocreatorRole'
  | 'aboutSupport'
  | 'runtimeDownload'
  | 'launchOptionsWarningTitle'
  | 'launchOptionsWarningBody'
  | 'actionClose'
  | 'installRiskTitle'
  | 'installRiskSteamDetected'
  | 'installRiskBody'
  | 'actionInstallAtRisk'
  | 'actionContinueInstall'
  | 'actionGoBack'
  | 'gameQuitTitle'
  | 'gameQuitBody'
  | 'actionGameClosed'
  | 'steamQuitTitle'
  | 'steamQuitBody'
  | 'actionQuitSteam'
  | 'updaterButton'
  | 'updaterChecking'
  | 'updaterReady'
  | 'updaterCurrent'
  | 'updaterDownloading'
  | 'updaterInstallReady'
  | 'updaterUnsupported'
  | 'updaterRetry'
  | 'updaterErrorState'
  | 'updaterErrorTitle'
  | 'updaterErrorBody'
  | 'updaterCurrentTitle'
  | 'updaterCurrentBody'
  | 'updaterReadyTitle'
  | 'updaterReadyBody'
  | 'updaterReviewTitle'
  | 'updaterReviewBody'
  | 'updaterReviewConfirm'
  | 'updaterReviewCancel'
  | 'updaterInstalling'
  | 'updaterInstalledTitle'
  | 'updaterInstalledBody'
  | 'navHome'
  | 'navInstall'
  | 'navStream'
  | 'navChangelog'
  | 'navAbout'
  | 'homeTitle'
  | 'homeIntro'
  | 'homeOpenInstall'
  | 'homeOpenStream'
  | 'homeInstallHint'
  | 'homeStreamHint'
  | 'homeChangelogHint'
  | 'homeAboutHint'
  | 'streamTitle';

export const defaultLocale: Locale = 'zh';

export const messages: Record<Locale, Record<MessageKey, string>> = {
  en: {
    htmlLang: 'en',
    pageTitle: 'BazaarPlusPlus',
    kicker: 'Born of Passion',
    subtitle: 'Mod Installation',
    stepBpp: 'BazaarPlusPlus',
    stepBazaar: 'The Bazaar',
    stepActions: 'Actions',
    statusChecking: 'Checking...',
    statusNotInstalled: 'Not installed',
    detectInstalledHint:
      'Run detection to inspect the installed BazaarPlusPlus version.',
    statusFound: 'Found',
    actionReenter: 'Choose again',
    actionBrowse: 'Browse',
    placeholderGamePath: 'Game install path...',
    actionCheck: 'Check',
    errorGamePath:
      'The Bazaar was not found in this folder. Verify the install path.',
    actionInstalling: 'Installing...',
    actionReinstall: 'Reinstall',
    actionInstall: 'Install',
    actionRepairing: 'Resetting match history...',
    actionRepair: 'Reset Match History',
    resetHistoryTitle: 'Reset Match History',
    resetHistoryBody:
      'Current match history size: {size}\nAfter confirmation, all current match history will be permanently deleted.\nThe installer will also try to recover match history if it is currently broken.',
    actionUninstalling: 'Uninstalling...',
    actionUninstall: 'Uninstall',
    footer: 'BazaarPlusPlus · Born of Passion',
    aboutTitle: 'About',
    aboutBack: 'Back',
    aboutOpenSource: 'Open Source Software',
    aboutInspiredBy: 'Inspired By',
    aboutDependencies: 'Dependencies',
    aboutDataSources: 'Data Sources',
    aboutInfo: 'Information',
    aboutAuthors: 'Authors',
    aboutAuthorRole: 'Author',
    aboutCocreatorRole: 'Co-creator',
    aboutSupport: 'Support',
    runtimeDownload: 'Download .NET',
    launchOptionsWarningTitle: 'Steam Launch Option Check',
    launchOptionsWarningBody:
      'BazaarPlusPlus finished installing, but the installer could not confirm that Steam saved the new launch options. Please reopen Steam and verify the game launch command if the mod does not start.',
    actionClose: 'Close',
    installRiskTitle: 'Steam Might Still Be Running',
    installRiskSteamDetected:
      'The installer detected that Steam may still be running.',
    installRiskBody:
      'This check can occasionally report a false positive.\nIf you continue anyway, installation may fail, files may stay locked, or Steam launch options may not update correctly.\nOnly continue if you understand the risk.',
    actionInstallAtRisk: 'Install Anyway',
    actionContinueInstall: 'Continue Install',
    actionGoBack: 'Go Back',
    gameQuitTitle: 'Close The Bazaar First',
    gameQuitBody:
      'The Bazaar is still running.\nClose the game completely, then continue once it has fully exited.\nIf installation still fails after closing it, try restarting your PC.',
    actionGameClosed: 'I Closed the Game',
    steamQuitTitle: 'Close Steam First',
    steamQuitBody:
      'Steam is still running. BazaarPlusPlus needs Steam to close before it updates the game launch options. Continue and let the installer close Steam for you.',
    actionQuitSteam: 'Close Steam',
    updaterButton: 'Update',
    updaterChecking: 'Checking for updates...',
    updaterReady: 'Update {version}',
    updaterCurrent: 'Up to Date',
    updaterDownloading: 'Downloading {progress}',
    updaterInstallReady: 'Restart to apply {version}',
    updaterUnsupported: 'Auto-update unavailable',
    updaterRetry: 'Retry Update',
    updaterErrorState: 'Update Failed',
    updaterErrorTitle: 'Unable to Update Right Now',
    updaterErrorBody:
      'The update did not finish successfully. Please try again in a moment.\n\nDetails: {message}',
    updaterCurrentTitle: 'You’re Up to Date',
    updaterCurrentBody: 'You already have the latest version.',
    updaterReadyTitle: 'Update Available',
    updaterReadyBody:
      'Version {version} is available. Select Update to download and install it, then restart the app.',
    updaterReviewTitle: 'Ready to Install',
    updaterReviewBody: 'Version {version} is ready to install.',
    updaterReviewConfirm: 'Update Now',
    updaterReviewCancel: 'Later',
    updaterInstalling: 'Installing update...',
    updaterInstalledTitle: 'Update Ready to Apply',
    updaterInstalledBody:
      'Version {version} is installed. Restart the app to apply it.',
    navHome: 'Home',
    navInstall: 'Install & Repair',
    navStream: 'Stream Mode',
    navChangelog: 'Changelog',
    navAbout: 'About',
    homeTitle: 'BazaarPlusPlus Control Room',
    homeIntro:
      'Use the installer when you need setup work. Use Stream Mode only when you want a localhost overlay page for OBS.',
    homeOpenInstall: 'Open Install & Repair',
    homeOpenStream: 'Open Stream Mode',
    homeInstallHint:
      'Install, repair, or remove BazaarPlusPlus from the current game directory.',
    homeStreamHint:
      'Start the local OBS service, copy the browser-source URL, and verify end-of-run records captured after stream start are loading.',
    homeChangelogHint:
      'Review the current release notes and update highlights.',
    homeAboutHint: 'Project credits, dependencies, and support information.',
    streamTitle: 'Stream Mode'
  },
  zh: {
    htmlLang: 'zh-CN',
    pageTitle: 'BazaarPlusPlus',
    kicker: '因热爱而生',
    subtitle: '模组安装',
    stepBpp: 'BazaarPlusPlus',
    stepBazaar: 'The Bazaar',
    stepActions: '操作',
    statusChecking: '检查中...',
    statusNotInstalled: '未安装',
    detectInstalledHint: '点击检测以查看当前已安装的 BazaarPlusPlus 版本。',
    statusFound: '已找到',
    actionReenter: '重新选择',
    actionBrowse: '浏览',
    placeholderGamePath: '游戏安装路径...',
    actionCheck: '检查',
    errorGamePath: '该目录中未找到 The Bazaar，请确认安装路径是否正确。',
    actionInstalling: '安装中...',
    actionReinstall: '重新安装',
    actionInstall: '安装',
    actionRepairing: '重置战绩记录中...',
    actionRepair: '重置战绩记录',
    resetHistoryTitle: '重置战绩记录',
    resetHistoryBody:
      '当前战绩记录占用空间：{size}\n确认后将永久删除当前所有战绩记录。\n如果战绩记录已出现异常，安装器也会尝试将其恢复正常。',
    actionUninstalling: '卸载中...',
    actionUninstall: '卸载',
    footer: 'BazaarPlusPlus · 因热爱而生',
    aboutTitle: '关于',
    aboutBack: '返回',
    aboutOpenSource: '开源软件',
    aboutInspiredBy: '灵感来源',
    aboutDependencies: '依赖项目',
    aboutDataSources: '数据来源',
    aboutInfo: '信息',
    aboutAuthors: '作者',
    aboutAuthorRole: '作者',
    aboutCocreatorRole: '联创',
    aboutSupport: '支持我们',
    runtimeDownload: '下载 .NET',
    launchOptionsWarningTitle: 'Steam 启动项检查',
    launchOptionsWarningBody:
      'BazaarPlusPlus 已完成安装，但安装器无法确认 Steam 已正确保存新的启动项。如果模组没有生效，请重新打开 Steam 后检查游戏启动命令。',
    actionClose: '关闭',
    installRiskTitle: 'Steam 可能仍在运行',
    installRiskSteamDetected: '安装器检测到 Steam 可能仍在运行。',
    installRiskBody:
      '这项检测偶尔会出现误报。\n如果你仍然继续安装，可能会出现安装失败、文件被占用，或 Steam 启动项未正确更新的问题。\n只有在你了解这些风险时才继续。',
    actionInstallAtRisk: '已知风险，仍然安装',
    actionContinueInstall: '继续安装',
    actionGoBack: '返回处理',
    gameQuitTitle: '请先关闭 The Bazaar',
    gameQuitBody:
      '检测到 The Bazaar 仍在运行。\n请先完全关闭游戏，确认退出后再继续安装。\n如果关闭后仍无法安装，请尝试重启电脑。',
    actionGameClosed: '已关闭游戏',
    steamQuitTitle: '请先关闭 Steam',
    steamQuitBody:
      'Steam 当前仍在运行。BazaarPlusPlus 需要先关闭 Steam，才能安全更新游戏启动项。继续后，安装器会尝试为你关闭 Steam。',
    actionQuitSteam: '关闭 Steam',
    updaterButton: '更新',
    updaterChecking: '正在检查更新...',
    updaterReady: '更新到 {version}',
    updaterCurrent: '已是最新',
    updaterDownloading: '下载中 {progress}',
    updaterInstallReady: '重启以应用 {version}',
    updaterUnsupported: '暂不支持自动更新',
    updaterRetry: '重试更新',
    updaterErrorState: '更新失败',
    updaterErrorTitle: '暂时无法完成更新',
    updaterErrorBody: '这次更新没有成功完成，请稍后再试。\n\n详情：{message}',
    updaterCurrentTitle: '当前已是最新版本',
    updaterCurrentBody: '你当前使用的已经是最新版本。',
    updaterReadyTitle: '发现新版本',
    updaterReadyBody:
      '发现新版本 {version}。点击“更新”即可下载并安装，完成后重启应用。',
    updaterReviewTitle: '准备安装更新',
    updaterReviewBody: '新版本 {version} 已准备好，可以开始安装。',
    updaterReviewConfirm: '立即更新',
    updaterReviewCancel: '稍后再说',
    updaterInstalling: '正在安装更新...',
    updaterInstalledTitle: '更新已准备就绪',
    updaterInstalledBody: '新版本 {version} 已安装完成。重启应用后即可生效。',
    navHome: '首页',
    navInstall: '安装与修复',
    navStream: '直播模式',
    navChangelog: '更新日志',
    navAbout: '关于',
    homeTitle: 'BazaarPlusPlus 控制台',
    homeIntro:
      '安装与修复用于一次性的部署维护；直播模式只在你需要给 OBS 提供本地网页时再开启。',
    homeOpenInstall: '进入安装与修复',
    homeOpenStream: '进入直播模式',
    homeInstallHint: '安装、修复或卸载当前游戏目录中的 BazaarPlusPlus。',
    homeStreamHint:
      '启动本地 OBS 服务、复制浏览器源地址，并确认开播后的 End of Run 记录是否正常读取。',
    homeChangelogHint: '查看当前版本的更新记录与功能亮点。',
    homeAboutHint: '查看项目说明、依赖信息与支持入口。',
    streamTitle: '直播模式'
  }
};

export function resolveInitialLocale(): Locale {
  if (typeof window === 'undefined') {
    return defaultLocale;
  }

  const saved = window.localStorage.getItem('locale');
  if (saved === 'en' || saved === 'zh') {
    return saved;
  }

  return defaultLocale;
}

export function formatMessage(
  locale: Locale,
  key: MessageKey,
  params?: Record<string, string | number>
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
