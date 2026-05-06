// Stable prefixes that the Rust `repair_bpp` command returns. Keep these in
// lockstep with `REPAIR_ERR_*` in `src-tauri/src/commands/bepinex/mod.rs`.
const ERR_GAME_RUNNING = 'bpp_data_reset_blocked_by_game';
const ERR_PARTIAL_FAILURE = 'bpp_data_reset_partial_failure';

export type RepairError =
  | { kind: 'game_running' }
  | { kind: 'partial_failure'; failedPaths: string[] }
  | { kind: 'unknown'; message: string };

export function parseRepairError(error: unknown): RepairError {
  const message = error instanceof Error ? error.message : String(error ?? '');

  if (message === ERR_GAME_RUNNING) {
    return { kind: 'game_running' };
  }

  if (message.startsWith(`${ERR_PARTIAL_FAILURE}:`)) {
    const tail = message.slice(ERR_PARTIAL_FAILURE.length + 1);
    const failedPaths = tail
      .split('\u{1f}')
      .map((entry) => entry.trim())
      .filter(Boolean);
    return { kind: 'partial_failure', failedPaths };
  }

  return { kind: 'unknown', message };
}

export interface RepairErrorCopy {
  title: string;
  body: string;
  pathListLabel?: string;
  paths?: string[];
  retryLabel: string;
}

export function describeRepairError(
  error: RepairError,
  localized: (zh: string, en: string) => string
): RepairErrorCopy {
  switch (error.kind) {
    case 'game_running':
      return {
        title: localized('请先关闭游戏', 'Close The Bazaar first'),
        body: localized(
          '检测到 The Bazaar 正在运行。游戏运行时会持续占用战绩数据库，无法删除。请退出游戏后再次尝试。',
          'The Bazaar appears to be running. The game keeps the match-history database open, so the files cannot be removed while it runs. Quit the game and try again.'
        ),
        retryLabel: localized('我已退出，重试', 'I quit the game, retry')
      };
    case 'partial_failure':
      return {
        title: localized('有文件无法删除', 'Some files could not be deleted'),
        body: localized(
          '部分文件被其他程序占用（常见原因：游戏仍在运行、杀毒软件正在扫描、资源管理器正在预览截图）。请关闭这些程序后重试，或手动删除下列文件后重试。',
          'Some files were locked by another process (usually the game is still running, antivirus is scanning, or Explorer is previewing screenshots). Close those programs and retry, or delete the listed files manually and retry.'
        ),
        pathListLabel: localized('未能删除的文件', 'Files we could not delete'),
        paths: error.failedPaths,
        retryLabel: localized('重试', 'Retry')
      };
    case 'unknown':
      return {
        title: localized('重置失败', 'Reset failed'),
        body: error.message
          ? localized(
              `重置过程中出现错误：${error.message}`,
              `An error occurred during reset: ${error.message}`
            )
          : localized(
              '重置过程中出现未知错误，请重试。',
              'An unknown error occurred during reset. Please retry.'
            ),
        retryLabel: localized('重试', 'Retry')
      };
  }
}
