import {
  AlertCircle,
  AlertTriangle,
  Copy,
  ExternalLink,
  Maximize,
  Minimize,
  Radio,
  RefreshCw,
  Settings2
} from 'lucide-react';
import { useEffect } from 'react';
import type { StreamOverlayDisplayMode } from '../types/backend';
import { Button } from '../components/ui/Button';
import { PageShell } from '../components/ui/PageShell';
import { SegmentedControl } from '../components/ui/SegmentedControl';
import { useToast, type ToastTone } from '../components/ui/Toast';
import { useStreamPage } from '../features/stream/useStreamPage';
import {
  presentStreamProblem,
  presentStreamSnapshot
} from '../features/stream/streamPresentation';
import type { StreamProblem } from '../features/stream/streamProblems';
import { useI18n } from '../i18n/LocaleProvider';
import type { MessageKey } from '../i18n/messages';

const displayModes: Array<{
  value: StreamOverlayDisplayMode;
  labelKey: MessageKey;
}> = [
  { value: 'current', labelKey: 'streamModeCurrent' },
  { value: 'hero', labelKey: 'streamModeHero' },
  { value: 'herohalf', labelKey: 'streamModeHeroHalf' }
];

export default function Stream() {
  const { t } = useI18n();
  const { snapshot, intents } = useStreamPage();
  const presentation = presentStreamSnapshot(snapshot, t);
  const status = snapshot.service.status;
  const cropSettings = snapshot.crop.settings;
  const statusTone = presentation.status.tone;

  useStreamProblemToast(
    snapshot.service.problem,
    'stream:service',
    'error',
    t('streamRestart'),
    intents.restart
  );
  useStreamProblemToast(
    snapshot.polling.problem,
    'stream:polling',
    'warning',
    t('streamRetryStatus'),
    intents.retryStatus
  );
  useStreamProblemToast(
    snapshot.oneOff.problems.open_overlay,
    'stream:open-overlay'
  );
  useStreamProblemToast(snapshot.oneOff.problems.copy, 'stream:copy');
  useStreamProblemToast(snapshot.window.problem, 'stream:window');
  useStreamProblemToast(
    snapshot.crop.problem,
    'stream:crop',
    'error',
    snapshot.crop.problem?.params.operation === 'load'
      ? t('streamRetryCrop')
      : undefined,
    snapshot.crop.problem?.params.operation === 'load'
      ? intents.reloadCropSettings
      : undefined
  );
  useStreamProblemToast(
    snapshot.oneOff.problems.open_settings,
    'stream:open-settings'
  );
  useStreamNoticeToast(presentation.notice);

  return (
    <PageShell title={t('streamTitle')}>
      <div className="bpp-stream-stack">
        <section className="bpp-panel bpp-stream-panel">
          <div className="bpp-stream-service-row">
            <div className="bpp-stream-service-status">
              <div className={`bpp-stream-status-icon is-${statusTone}`}>
                {statusTone === 'degraded' ? (
                  <AlertCircle size={16} />
                ) : statusTone === 'stale' ? (
                  <AlertTriangle size={16} />
                ) : statusTone === 'running' ? (
                  <Radio size={16} className="animate-pulse" />
                ) : (
                  <RefreshCw
                    size={16}
                    className={
                      snapshot.service.phase === 'loading' ||
                      snapshot.service.operation === 'restart'
                        ? 'animate-spin'
                        : ''
                    }
                  />
                )}
              </div>
              <div className="min-w-0">
                <h3 className="bpp-stream-status-title">
                  {presentation.status.label}
                </h3>
                <p className="bpp-stream-status-detail">
                  {presentation.status.detail}
                  {status?.running && snapshot.polling.freshness === 'fresh'
                    ? ` · ${presentation.dbLabel}`
                    : ''}
                </p>
              </div>
            </div>
            <div className="bpp-stream-service-actions">
              <Button
                disabled={!snapshot.oneOff.canOpenOverlay}
                onClick={() => void intents.openOverlay()}
              >
                <ExternalLink size={14} /> {t('streamOpenOverlay')}
              </Button>
              <Button
                variant="primary"
                disabled={!snapshot.service.canRestart}
                onClick={() => void intents.restart()}
              >
                <RefreshCw
                  size={14}
                  className={
                    snapshot.service.operation === 'restart'
                      ? 'animate-spin'
                      : ''
                  }
                />
                {status?.running ? t('streamRestart') : t('streamStart')}
              </Button>
            </div>
          </div>

          <div className="bpp-stream-section">
            <span
              id="stream-obs-url-label"
              className="bpp-stream-section-label"
            >
              {t('streamObsUrlLabel')}
            </span>
            <div className="bpp-stream-obs-row">
              <div
                id="stream-obs-url"
                className="bpp-input bpp-stream-obs-value selectable fira-code"
                aria-labelledby="stream-obs-url-label"
              >
                {snapshot.oneOff.obsUrl ?? t('streamObsPlaceholder')}
              </div>
              <Button
                disabled={!snapshot.oneOff.canCopyObsUrl}
                onClick={() => void intents.copyObsUrl()}
              >
                <Copy size={16} /> {t('copy')}
              </Button>
            </div>
            <p className="bpp-stream-section-hint m-0 mt-2 text-[12px] leading-relaxed">
              {t('streamObsGuide')}
            </p>
          </div>

          <div className="bpp-stream-section">
            <div className="bpp-stream-section-heading">
              <span className="bpp-stream-section-label">
                {t('streamWindowSection')}
              </span>
              <span className="bpp-stream-window-summary">
                {presentation.windowLabel}
              </span>
            </div>
            <div className="bpp-stream-metrics-grid">
              <InfoMetric
                label={t('streamInfoHost')}
                value={status?.host ?? '-'}
              />
              <InfoMetric
                label={t('streamInfoPort')}
                value={status?.port ? String(status.port) : '-'}
              />
              <InfoMetric
                label={t('streamInfoDb')}
                value={presentation.dbLabel}
              />
              <div className="bpp-stream-window-metric">
                <InfoMetric
                  label={t('streamInfoWindow')}
                  value={String(status?.active_window_offset ?? 0)}
                />
                <div className="bpp-stream-window-actions">
                  <Button
                    size="small"
                    disabled={!snapshot.window.canMoveMoreHistory}
                    onClick={() => void intents.moveWindow(1)}
                  >
                    <Maximize size={12} />
                    {t('streamMoreHistory')}
                  </Button>
                  <Button
                    size="small"
                    disabled={!snapshot.window.canMoveLessHistory}
                    onClick={() => void intents.moveWindow(-1)}
                  >
                    <Minimize size={12} />
                    {t('streamLessHistory')}
                  </Button>
                </div>
              </div>
            </div>
          </div>

          <div className="bpp-stream-section bpp-stream-config-section">
            <span className="bpp-stream-section-label">
              {t('streamOverlayConfig')}
            </span>

            <span className="bpp-stream-metric-label">
              {t('streamDisplayModeLabel')}
            </span>

            <SegmentedControl
              label={t('streamDisplayModeLabel')}
              name="displayMode"
              value={cropSettings.display_mode}
              disabled={!snapshot.crop.canEdit}
              options={displayModes.map((mode) => ({
                value: mode.value,
                label: t(mode.labelKey)
              }))}
              onChange={(mode) => void intents.changeDisplayMode(mode)}
            />

            <div className="bpp-stream-crop-row">
              <label htmlFor="stream-crop-code" className="sr-only">
                {t('streamCropCodeLabel')}
              </label>
              <input
                id="stream-crop-code"
                type="text"
                placeholder={t('streamCropCodePlaceholder')}
                value={snapshot.crop.code}
                disabled={!snapshot.crop.canEdit}
                onChange={(event) => intents.setCropCode(event.target.value)}
                className="bpp-input bpp-stream-crop-input fira-code"
              />
              <Button
                variant="primary"
                onClick={() => void intents.submitCropCode()}
                disabled={!snapshot.crop.canEdit}
              >
                {t('streamApplyCrop')}
              </Button>
              <Button
                onClick={() => void intents.resetCropCode()}
                disabled={!snapshot.crop.canEdit}
              >
                {t('streamResetCrop')}
              </Button>
              <Button
                disabled={!snapshot.oneOff.canOpenSettings}
                onClick={() => void intents.openSettings()}
              >
                <Settings2 size={16} /> {t('streamOpenSettings')}
              </Button>
            </div>
          </div>
        </section>
      </div>
    </PageShell>
  );
}

function useStreamProblemToast(
  problem: StreamProblem | null,
  id: string,
  tone: Extract<ToastTone, 'error' | 'warning'> = 'error',
  actionLabel?: string,
  onAction?: () => void
) {
  const { t } = useI18n();
  const { dismissToast, showToast } = useToast();

  useEffect(() => {
    if (!problem) {
      dismissToast(id);
      return;
    }
    showToast({
      id,
      tone,
      message: presentStreamProblem(problem, t),
      action:
        actionLabel && onAction
          ? { label: actionLabel, onClick: onAction }
          : undefined
    });
  }, [actionLabel, dismissToast, id, onAction, problem, showToast, t, tone]);
}

function useStreamNoticeToast(notice: string | null) {
  const { dismissToast, showToast } = useToast();

  useEffect(() => {
    if (!notice) {
      dismissToast('stream:notice');
      return;
    }
    showToast({ id: 'stream:notice', tone: 'success', message: notice });
  }, [dismissToast, notice, showToast]);
}

function InfoMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="bpp-stream-metric">
      <span className="bpp-stream-metric-label">{label}</span>
      <span className="bpp-stream-metric-value fira-code">{value}</span>
    </div>
  );
}
