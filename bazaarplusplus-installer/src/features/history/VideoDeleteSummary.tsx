import { useI18n } from '../../i18n/LocaleProvider';
import { formatDateTime, formatDuration } from './format';

export interface VideoDeleteTarget {
  kind: 'delete-video';
  battleId: string;
  videoId: string;
  runStartedAt: string;
  day: number | null;
  opponent: string;
  durationMs: number | null;
}

export function VideoDeleteSummary({ target }: { target: VideoDeleteTarget }) {
  const { locale, t } = useI18n();
  return (
    <div className="bpp-confirm-target selectable">
      <dl className="bpp-video-delete-summary">
        <dt>{t('deleteVideoRunDate')}</dt>
        <dd>{formatDateTime(target.runStartedAt, locale)}</dd>
        <dt>{t('deleteVideoBattle')}</dt>
        <dd>
          {t('deleteVideoBattleDescription', {
            day: target.day ?? '-',
            opponent: target.opponent
          })}
        </dd>
        <dt>{t('deleteVideoDuration')}</dt>
        <dd>{formatDuration(target.durationMs, locale)}</dd>
      </dl>
      <details className="bpp-video-delete-diagnostics">
        <summary>{t('deleteVideoTechnicalDetails')}</summary>
        <p className="fira-code">
          {t('deleteVideoTarget', {
            battleId: target.battleId,
            videoId: target.videoId
          })}
        </p>
      </details>
    </div>
  );
}
