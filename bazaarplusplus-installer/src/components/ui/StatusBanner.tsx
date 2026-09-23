import clsx from 'clsx';
import { CheckCircle2, CircleX, Info, TriangleAlert } from 'lucide-react';
import type { HTMLAttributes, ReactNode } from 'react';

export type StatusBannerTone = 'info' | 'warning' | 'error' | 'success';

const TONE_ICON = {
  info: Info,
  warning: TriangleAlert,
  error: CircleX,
  success: CheckCircle2
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
      className={clsx('bpp-banner selectable', `is-${tone}`, className)}
    >
      {leading && (
        <span className="bpp-banner-icon" aria-hidden="true">
          {leading}
        </span>
      )}
      <div className="bpp-banner-body">
        <div className="bpp-banner-message">{message}</div>
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
