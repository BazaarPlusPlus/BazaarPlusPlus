import {
  Copy,
  ExternalLink,
  Minus,
  Play,
  Plus,
  RotateCw,
  Settings2
} from 'lucide-react';
import { useEffect, useMemo } from 'react';
import type { StreamOverlayDisplayMode } from '../types/backend';
import { Button } from '../components/ui/Button';
import { PageShell } from '../components/ui/PageShell';
import { StatusBadge } from '../components/ui/StatusBadge';
import { SegmentedControl } from '../components/ui/SegmentedControl';
import { useToast, type ToastTone } from '../components/ui/Toast';
import { useStreamPage } from '../features/stream/useStreamPage';
import { presentStreamSnapshot } from '../features/stream/streamPresentation';
import {
  presentStreamProblem,
  type StreamProblem
} from '../features/stream/streamProblems';
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
  const presentation = useMemo(
    () => presentStreamSnapshot(snapshot, t),
    [snapshot, t]
  );
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

  const statusBadge = (
    {
      loading: 'busy',
      running: 'ok',
      idle: 'neutral',
      degraded: 'bad',
      stale: 'warn'
    } as const
  )[statusTone];

  return (
    <PageShell
      title={t('streamTitle')}
      meta={
        <StatusBadge tone={statusBadge}>
          {presentation.status.label}
        </StatusBadge>
      }
      action={
        <>
          <Button
            size="sm"
            disabled={!snapshot.oneOff.canOpenOverlay}
            onClick={() => void intents.openOverlay()}
            icon={<ExternalLink />}
          >
            {t('streamOpenOverlay')}
          </Button>
          <Button
            variant="primary"
            size="sm"
            disabled={!snapshot.service.canRestart}
            busy={snapshot.service.operation === 'restart'}
            onClick={() => void intents.restart()}
            icon={status?.running ? <RotateCw /> : <Play />}
          >
            {status?.running ? t('streamRestart') : t('streamStart')}
          </Button>
        </>
      }
    >
      <p className="bpp-page-meta -mt-2 m-0">
        {presentation.status.detail}
        {status?.running && snapshot.polling.freshness === 'fresh'
          ? ` · ${presentation.dbLabel}`
          : ''}
      </p>

      <section className="bpp-panel bpp-panel-pad">
        <h3 className="bpp-panel-title">{t('streamObsUrlLabel')}</h3>
        <div className="bpp-field-row">
          <div
            id="stream-obs-url"
            className={`bpp-input is-readonly selectable flex-1 ${snapshot.oneOff.obsUrl ? 'mono' : ''}`}
            aria-label={t('streamObsUrlLabel')}
          >
            {snapshot.oneOff.obsUrl ?? t('streamObsPlaceholder')}
          </div>
          <Button
            disabled={!snapshot.oneOff.canCopyObsUrl}
            onClick={() => void intents.copyObsUrl()}
            icon={<Copy />}
          >
            {t('copy')}
          </Button>
        </div>
      </section>

      <section className="bpp-panel">
        <div className="bpp-row">
          <div className="bpp-row-copy">
            <span className="bpp-row-title">{t('streamWindowSection')}</span>
            <span className="bpp-row-description">
              {presentation.windowLabel}
            </span>
          </div>
          <div className="flex flex-none items-center gap-2">
            <Button
              size="sm"
              disabled={!snapshot.window.canMoveLessHistory}
              onClick={() => void intents.moveWindow(-1)}
              icon={<Minus />}
            >
              {t('streamLessHistory')}
            </Button>
            <Button
              size="sm"
              disabled={!snapshot.window.canMoveMoreHistory}
              onClick={() => void intents.moveWindow(1)}
              icon={<Plus />}
            >
              {t('streamMoreHistory')}
            </Button>
          </div>
        </div>
        <div className="bpp-kv">
          <InfoMetric
            mono
            label={t('streamInfoHost')}
            value={status?.host ?? '-'}
          />
          <InfoMetric
            mono
            label={t('streamInfoPort')}
            value={status?.port ? String(status.port) : '-'}
          />
          <InfoMetric label={t('streamInfoDb')} value={presentation.dbLabel} />
          <InfoMetric
            label={t('streamInfoWindow')}
            value={String(status?.active_window_offset ?? 0)}
          />
        </div>
      </section>

      <section className="bpp-panel bpp-panel-pad">
        <h3 className="bpp-panel-title">{t('streamOverlayConfig')}</h3>
        <div className="bpp-field">
          <span className="bpp-field-label">{t('streamDisplayModeLabel')}</span>
          <SegmentedControl
            className="self-start"
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
        </div>
        <div className="bpp-field">
          <label htmlFor="stream-crop-code" className="bpp-field-label">
            {t('streamCropCodeLabel')}
          </label>
          <div className="bpp-field-row">
            <input
              id="stream-crop-code"
              type="text"
              placeholder={t('streamCropCodePlaceholder')}
              value={snapshot.crop.code}
              disabled={!snapshot.crop.canEdit}
              onChange={(event) => intents.setCropCode(event.target.value)}
              className="bpp-input mono min-w-[180px] flex-1"
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
              variant="ghost"
              disabled={!snapshot.oneOff.canOpenSettings}
              onClick={() => void intents.openSettings()}
              icon={<Settings2 />}
            >
              {t('streamOpenSettings')}
            </Button>
          </div>
        </div>
      </section>
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

function InfoMetric({
  label,
  value,
  mono = false
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <>
      <span className="bpp-kv-key">{label}</span>
      <span
        className={`bpp-kv-value selectable ${mono ? 'mono' : 'tnum'}`}
        title={value}
      >
        {value}
      </span>
      <span />
    </>
  );
}
