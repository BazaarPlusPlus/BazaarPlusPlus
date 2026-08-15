import {
  AlertTriangle,
  DownloadCloud,
  Loader2,
  X,
  type LucideIcon
} from 'lucide-react';
import type { ReactNode } from 'react';
import { Dialog, type DialogCloseReason } from './Dialog';
import { useI18n } from '../../i18n/LocaleProvider';

export type ConfirmTone = 'gold' | 'danger';

export type ConfirmDialogDismissReason =
  DialogCloseReason | 'close-button' | 'secondary-action';

export type ActiveDismissalPolicy =
  | { kind: 'blocked' }
  | { kind: 'detachable'; label: string }
  | { kind: 'cancelable'; label: string; onCancel: () => void };

export interface ConfirmAcknowledge {
  /** Already-localized label (pass t('...')). */
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}

export interface ConfirmDialogProps {
  /** Constant per-modal DOM id (e.g. "install-modal-title"), NOT useId() —
   *  useId changes rendered bytes. Wired to <h2 id> and Dialog labelledBy. */
  titleId: string;
  title: string;
  subtitle?: string;
  /** gold => DownloadCloud + gold chrome + body gap-6;
   *  danger => AlertTriangle + red chrome + body gap-5. */
  tone: ConfirmTone;
  /** Body blocks. MUST be direct siblings (a fragment or multiple top-level
   *  elements) — ConfirmDialog renders {children} as direct children of the
   *  `p-6 flex flex-col gap-*` body div. A wrapping <div> breaks both the flex
   *  gap spacing AND byte-identity. */
  children: ReactNode;
  /** Optional tone-styled gating checkbox, rendered AFTER children, BEFORE
   *  footer. Unchecked => confirm disabled. Fully controlled; the page owns
   *  reset-on-open/on-success. ConfirmDialog holds NO internal checkbox state. */
  acknowledge?: ConfirmAcknowledge;
  confirmLabel: string;
  /** Present => Install affordance: busy swaps text to busyLabel, NO spinner.
   *  Absent => the four danger modals: busy prepends Loader2 animate-spin to
   *  confirmLabel. Presence is the switch. */
  busyLabel?: string;
  /** Disables confirm + triggers busy affordance. */
  busy: boolean;
  /** Explicitly defines what every dismiss surface means while busy. */
  activeDismissalPolicy: ActiveDismissalPolicy;
  /** Replaces ordinary Cancel wording after a failed operation. */
  dismissLabel?: string;
  /** Extra gate (for example, Cleanup having nothing to clean). */
  confirmDisabled?: boolean;
  onConfirm: () => unknown;
  onClose: () => void;
}

const TONE = {
  gold: {
    Icon: DownloadCloud as LucideIcon,
    card: 'bpp-modal-card bpp-install-confirm-card w-full max-w-[560px] mx-4 relative',
    bar: 'bpp-modal-header bpp-install-confirm-header flex justify-between items-center px-5 py-4',
    icon: 'bpp-confirm-tone-icon is-gold',
    title: 'bpp-confirm-title is-gold cinzel text-[1.1rem] m-0 tracking-wider',
    close:
      'bpp-confirm-close is-gold bpp-install-confirm-close transition-colors',
    body: 'bpp-install-confirm-body p-6 flex flex-col gap-6',
    ackBox:
      'bpp-confirm-ack is-gold bpp-install-confirm-ack flex items-start gap-3 p-3 group',
    ackText: 'bpp-confirm-ack-text is-gold text-[13px] leading-relaxed',
    confirm:
      'bpp-confirm-submit is-gold bpp-install-confirm-submit px-5 py-2 text-sm cinzel font-bold tracking-wider transition-[filter] hover:brightness-110 active:brightness-95 disabled:opacity-50 disabled:hover:brightness-100'
  },
  danger: {
    Icon: AlertTriangle as LucideIcon,
    card: 'bpp-modal-card bpp-modal-danger w-full max-w-md mx-4 relative',
    bar: 'bpp-modal-header flex justify-between items-center px-5 py-4',
    icon: 'bpp-confirm-tone-icon is-danger',
    title:
      'bpp-confirm-title is-danger cinzel text-[1.1rem] m-0 tracking-wider',
    close: 'bpp-confirm-close is-danger transition-colors',
    body: 'p-6 flex flex-col gap-5',
    ackBox: 'bpp-confirm-ack is-danger flex items-start gap-3 p-3 group',
    ackText: 'bpp-confirm-ack-text is-danger text-[13px] leading-relaxed',
    confirm:
      'bpp-confirm-submit is-danger px-5 py-2 text-sm cinzel font-bold tracking-wider transition-[filter] hover:brightness-110 active:brightness-95 disabled:opacity-50 disabled:hover:brightness-100'
  }
} as const;

export function ConfirmDialog({
  titleId,
  title,
  subtitle,
  tone,
  children,
  acknowledge,
  confirmLabel,
  busyLabel,
  busy,
  activeDismissalPolicy,
  dismissLabel,
  confirmDisabled,
  onConfirm,
  onClose
}: ConfirmDialogProps) {
  const { t } = useI18n();
  const s = TONE[tone];
  const Icon = s.Icon;
  const dismissAllowed = !busy || activeDismissalPolicy.kind !== 'blocked';
  const activeDismissLabel =
    activeDismissalPolicy.kind === 'blocked'
      ? null
      : activeDismissalPolicy.label;
  const secondaryLabel = busy
    ? activeDismissLabel
    : (dismissLabel ?? t('cancel'));
  const requestDismiss = (reason: ConfirmDialogDismissReason) =>
    requestConfirmDialogDismiss({
      busy,
      activeDismissalPolicy,
      reason,
      onClose
    });

  return (
    <Dialog
      onClose={requestDismiss}
      labelledBy={titleId}
      focusContainerOnOpen={tone === 'gold'}
      className={tone === 'gold' ? 'bpp-install-confirm-dialog' : undefined}
    >
      <div className={s.card}>
        <div className={s.bar}>
          <div className="flex items-center gap-4">
            <Icon size={tone === 'gold' ? 30 : 18} className={s.icon} />
            <div className="min-w-0">
              <h2 id={titleId} className={s.title}>
                {title}
              </h2>
              {subtitle && (
                <p className="bpp-install-confirm-subtitle">{subtitle}</p>
              )}
            </div>
          </div>
          <button
            type="button"
            onClick={() => requestDismiss('close-button')}
            disabled={!dismissAllowed}
            className={`${s.close} disabled:opacity-50 disabled:pointer-events-none`}
            aria-label={
              busy && activeDismissLabel ? activeDismissLabel : t('close')
            }
          >
            <X size={20} />
          </button>
        </div>
        <div className={s.body}>
          {children}
          {acknowledge && (
            <label className={s.ackBox}>
              <input
                type="checkbox"
                className="mt-1"
                checked={acknowledge.checked}
                disabled={busy}
                onChange={(event) => acknowledge.onChange(event.target.checked)}
              />
              <span className={s.ackText}>{acknowledge.label}</span>
            </label>
          )}
          <div className="bpp-confirm-footer flex justify-end gap-3 pt-2">
            {secondaryLabel ? (
              <button
                type="button"
                onClick={() => requestDismiss('secondary-action')}
                className="bpp-confirm-cancel px-5 py-2 transition-colors text-sm"
              >
                {secondaryLabel}
              </button>
            ) : (
              <p
                role="status"
                aria-live="polite"
                className="bpp-confirm-blocked-status m-0 mr-auto text-xs"
              >
                {t('operationCannotBeCancelled')}
              </p>
            )}
            <button
              type="button"
              disabled={
                busy ||
                (acknowledge ? !acknowledge.checked : false) ||
                Boolean(confirmDisabled)
              }
              onClick={onConfirm}
              className={s.confirm}
              aria-busy={busy || undefined}
            >
              <span className="bpp-busy-label-sizer">
                <span
                  className="bpp-busy-label-idle"
                  aria-hidden={busy ? true : undefined}
                >
                  {confirmLabel}
                </span>
                <span
                  className="bpp-busy-label-active"
                  aria-hidden={!busy ? true : undefined}
                >
                  {busyLabel !== undefined ? (
                    busyLabel
                  ) : (
                    <>
                      <Loader2 size={14} className="animate-spin" />
                      {confirmLabel}
                    </>
                  )}
                </span>
              </span>
            </button>
          </div>
        </div>
      </div>
    </Dialog>
  );
}

export function requestConfirmDialogDismiss({
  busy,
  activeDismissalPolicy,
  onClose
}: {
  busy: boolean;
  activeDismissalPolicy: ActiveDismissalPolicy;
  reason: ConfirmDialogDismissReason;
  onClose: () => void;
}): boolean {
  if (!busy) {
    onClose();
    return true;
  }

  switch (activeDismissalPolicy.kind) {
    case 'blocked':
      return false;
    case 'detachable':
      onClose();
      return true;
    case 'cancelable':
      activeDismissalPolicy.onCancel();
      return true;
  }
}
