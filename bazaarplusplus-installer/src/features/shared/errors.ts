export function toErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

const RESET_BPP_DATA_ERR_GAME_RUNNING = 'bpp_data_reset_blocked_by_game';
const RESET_BPP_DATA_ERR_PARTIAL_FAILURE = 'bpp_data_reset_partial_failure:';
const RESET_BPP_DATA_PATH_SEPARATOR = '\u001f';

export type ResetBppDataError =
  | { code: 'game_running' }
  | { code: 'partial_failure'; paths: string[] };

export function parseResetBppDataError(
  error: unknown
): ResetBppDataError | null {
  const message = toErrorMessage(error);
  if (message === RESET_BPP_DATA_ERR_GAME_RUNNING) {
    return { code: 'game_running' };
  }

  if (message.startsWith(RESET_BPP_DATA_ERR_PARTIAL_FAILURE)) {
    const payload = message.slice(RESET_BPP_DATA_ERR_PARTIAL_FAILURE.length);
    const paths = payload
      .split(RESET_BPP_DATA_PATH_SEPARATOR)
      .map((path) => path.trim())
      .filter(Boolean);
    return { code: 'partial_failure', paths };
  }

  return null;
}
