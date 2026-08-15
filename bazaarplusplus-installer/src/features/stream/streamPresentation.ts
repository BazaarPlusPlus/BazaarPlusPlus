import type { Translate } from '../../i18n/LocaleProvider';
import type { StreamPageSnapshot } from './streamWorkflow';
import { presentStreamNotice, presentStreamProblem } from './streamProblems';

export { presentStreamProblem };

export type StreamStatusPresentation = {
  label: string;
  detail: string;
  tone: 'loading' | 'running' | 'idle' | 'degraded' | 'stale';
};

export type StreamPagePresentation = {
  status: StreamStatusPresentation;
  dbLabel: string;
  windowLabel: string;
  notice: string | null;
};

export function presentStreamSnapshot(
  snapshot: StreamPageSnapshot,
  t: Translate
): StreamPagePresentation {
  const status = snapshot.service.status;
  let statusPresentation: StreamStatusPresentation;

  if (snapshot.service.phase === 'loading') {
    statusPresentation = {
      label: t('streamStatusStarting'),
      detail: t('streamStarting'),
      tone: 'loading'
    };
  } else if (snapshot.service.problem) {
    statusPresentation = {
      label: t('streamStatusError'),
      detail: presentStreamProblem(snapshot.service.problem, t),
      tone: 'degraded'
    };
  } else if (snapshot.polling.freshness === 'stale') {
    statusPresentation = {
      label: t('streamStatusStale'),
      detail: status?.running
        ? t('streamStaleRunningDetail')
        : t('streamStaleIdleDetail'),
      tone: 'stale'
    };
  } else if (status?.running) {
    statusPresentation = {
      label: t('streamStatusRunning'),
      detail:
        status.port === null
          ? t('streamStatusUnavailableDetail')
          : t('streamPortDetail', { port: status.port }),
      tone: 'running'
    };
  } else if (status) {
    statusPresentation = {
      label: t('streamStatusIdle'),
      detail: t('streamIdleDetail'),
      tone: 'idle'
    };
  } else {
    statusPresentation = {
      label: t('streamStatusUnavailable'),
      detail: t('streamStatusUnavailableDetail'),
      tone: 'degraded'
    };
  }

  return {
    status: statusPresentation,
    dbLabel: status?.db.found ? t('dbConnected') : t('dbMissing'),
    windowLabel:
      (status?.active_window_offset ?? 0) === 0
        ? t('streamWindowLatest')
        : t('streamWindowOffset', {
            count: status?.active_window_offset ?? 0
          }),
    notice: presentStreamNotice(snapshot.notice, t)
  };
}
