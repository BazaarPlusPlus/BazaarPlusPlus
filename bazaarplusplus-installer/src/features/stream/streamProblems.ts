import type { Translate } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type StreamProblemCode =
  | 'stream_service_failed'
  | 'stream_poll_failed'
  | 'stream_window_failed'
  | 'stream_crop_failed'
  | 'stream_copy_failed'
  | 'stream_open_failed'
  | 'stream_unexpected';

export type StreamProblem = UiProblem<StreamProblemCode>;

export type StreamNoticeCode =
  'stream_obs_url_copied' | 'stream_crop_saved' | 'stream_crop_reset';

export type StreamNotice = {
  code: StreamNoticeCode;
  params: Record<string, string>;
};

export function streamProblemFromError(
  error: unknown,
  fallbackCode: StreamProblemCode,
  fallbackParams: Record<string, string> = {}
): StreamProblem {
  const problem: UiProblem = problemFromError(error, fallbackCode);
  if (isStreamProblemCode(problem.code)) {
    return {
      ...problem,
      code: problem.code,
      params: { ...fallbackParams, ...problem.params }
    };
  }

  return createUiProblem(fallbackCode, {
    params: { ...fallbackParams, ...problem.params },
    diagnostic: problem.diagnostic
  });
}

export function streamRuntimeProblem(diagnostic: string): StreamProblem {
  return createUiProblem('stream_service_failed', {
    params: { operation: 'runtime' },
    diagnostic
  });
}

export function presentStreamProblem(
  problem: StreamProblem,
  t: Translate
): string {
  return t(streamProblemMessageKey(problem), problem.params);
}

export function presentStreamNotice(
  notice: StreamNotice | null,
  t: Translate
): string | null {
  if (!notice) return null;
  switch (notice.code) {
    case 'stream_obs_url_copied':
      return t('streamCopied', notice.params);
    case 'stream_crop_saved':
      return t('streamCropSaved', notice.params);
    case 'stream_crop_reset':
      return t('streamCropReset', notice.params);
  }
}

function isStreamProblemCode(code: string): code is StreamProblemCode {
  return (
    code === 'stream_service_failed' ||
    code === 'stream_poll_failed' ||
    code === 'stream_window_failed' ||
    code === 'stream_crop_failed' ||
    code === 'stream_copy_failed' ||
    code === 'stream_open_failed' ||
    code === 'stream_unexpected'
  );
}

function streamProblemMessageKey(problem: StreamProblem): MessageKey {
  switch (problem.code) {
    case 'stream_service_failed':
      return problem.params.operation === 'restart'
        ? 'streamProblemRestartFailed'
        : 'streamProblemServiceFailed';
    case 'stream_poll_failed':
      return 'streamProblemPollFailed';
    case 'stream_window_failed':
      return 'streamProblemWindowFailed';
    case 'stream_crop_failed':
      return problem.params.operation === 'load'
        ? 'streamProblemCropLoadFailed'
        : 'streamProblemCropSaveFailed';
    case 'stream_copy_failed':
      return 'streamCopyFailed';
    case 'stream_open_failed':
      return problem.params.operation === 'open_settings'
        ? 'streamProblemOpenSettingsFailed'
        : 'streamProblemOpenOverlayFailed';
    case 'stream_unexpected':
      return 'streamProblemUnexpected';
  }
}
