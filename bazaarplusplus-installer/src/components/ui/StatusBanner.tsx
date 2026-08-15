import clsx from 'clsx';
import type { HTMLAttributes, ReactNode } from 'react';

export type StatusBannerTone = 'info' | 'warning' | 'error' | 'success';

export interface StatusBannerProps extends HTMLAttributes<HTMLDivElement> {
  tone?: StatusBannerTone;
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

  return (
    <div
      {...bannerProps}
      role={role ?? (error ? 'alert' : 'status')}
      aria-live={ariaLive ?? (error ? 'assertive' : 'polite')}
      className={clsx(
        'bpp-status-banner selectable',
        `bpp-status-banner-${tone}`,
        className
      )}
    >
      <div className="bpp-status-banner-row">
        {icon && (
          <span className="bpp-status-banner-icon" aria-hidden="true">
            {icon}
          </span>
        )}
        <div className="bpp-status-banner-message">{message}</div>
        {actions && <div className="bpp-status-banner-actions">{actions}</div>}
      </div>
      {diagnostic != null && diagnosticLabel && (
        <details className="bpp-status-banner-diagnostic">
          <summary>{diagnosticLabel}</summary>
          <pre>{diagnostic}</pre>
        </details>
      )}
    </div>
  );
}
