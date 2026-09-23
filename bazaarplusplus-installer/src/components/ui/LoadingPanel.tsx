import { Loader2 } from 'lucide-react';

export function LoadingPanel({
  label,
  className = 'h-48'
}: {
  label: string;
  className?: string;
}) {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      className={`bpp-loading-panel ${className}`}
    >
      <Loader2 size={16} className="bpp-spin" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}
