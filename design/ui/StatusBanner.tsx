import type { HTMLAttributes, ReactNode } from 'react';
import { cx } from './cx';
import { ErrorIcon, InfoIcon, SuccessIcon, WarningIcon } from './icons';

export type StatusBannerTone = 'info' | 'warning' | 'error' | 'success';

const TONE_ICON = {
  info: InfoIcon,
  warning: WarningIcon,
  error: ErrorIcon,
  success: SuccessIcon
} as const;

export interface StatusBannerProps extends HTMLAttributes<HTMLDivElement> {
  tone?: StatusBannerTone;
  /** Defaults to the tone's icon; pass `null` to omit it. */
  icon?: ReactNode;
  message: ReactNode;
  actions?: ReactNode;
  diagnostic?: ReactNode;
  diagnosticLabel?: string;
}

/** An inline notice above the content it is about. Errors interrupt as alerts;
 *  every other tone is announced politely. */
export function StatusBanner({
  tone = 'info',
  icon,
  message,
  actions,
  diagnostic,
  diagnosticLabel,
  role,
  'aria-live': ariaLive,
  className,
  ...bannerProps
}: StatusBannerProps) {
  const error = tone === 'error';
  const ToneIcon = TONE_ICON[tone];
  const leading = icon === undefined ? <ToneIcon size={16} /> : icon;

  return (
    <div
      {...bannerProps}
      role={role ?? (error ? 'alert' : 'status')}
      aria-live={ariaLive ?? (error ? 'assertive' : 'polite')}
      className={cx('bpp-banner selectable', `is-${tone}`, className)}
    >
      {leading && (
        <span className="bpp-banner-icon" aria-hidden="true">
          {leading}
        </span>
      )}
      <div className="bpp-banner-body">
        <div>{message}</div>
        {diagnostic != null && diagnosticLabel && (
          <details className="bpp-banner-diagnostic">
            <summary>{diagnosticLabel}</summary>
            <pre>{diagnostic}</pre>
          </details>
        )}
      </div>
      {actions && <div className="bpp-banner-actions">{actions}</div>}
    </div>
  );
}
