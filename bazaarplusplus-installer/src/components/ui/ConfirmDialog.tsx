import clsx from 'clsx';
import { DownloadCloud, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button } from './Button';
import {
  Dialog,
  DialogBody,
  DialogCard,
  DialogFooter,
  DialogHeader
} from './Dialog';
import { useI18n } from '../../i18n/LocaleProvider';

/** primary => install-style affirmative action; danger => destructive. */
export type ConfirmTone = 'primary' | 'danger';

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
  /** Constant per-modal DOM id (e.g. "install-modal-title"), wired to the
   *  heading id and the dialog's aria-labelledby. */
  titleId: string;
  title: string;
  subtitle?: string;
  tone: ConfirmTone;
  /** Body blocks, laid out as a vertical stack. */
  children: ReactNode;
  /** Optional gating checkbox, rendered after the body. Unchecked => confirm
   *  disabled. Fully controlled; the page owns reset-on-open/on-success. */
  acknowledge?: ConfirmAcknowledge;
  confirmLabel: string;
  /** Label swapped in while busy; the confirm button always shows a spinner
   *  while busy. */
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
  const dismissAllowed = !busy || activeDismissalPolicy.kind !== 'blocked';
  const activeDismissLabel =
    activeDismissalPolicy.kind === 'blocked'
      ? null
      : activeDismissalPolicy.label;
  const secondaryLabel = busy
    ? activeDismissLabel
    : (dismissLabel ?? t('cancel'));
  const requestDismiss = () =>
    requestConfirmDialogDismiss({
      busy,
      activeDismissalPolicy,
      onClose
    });
  const Icon = tone === 'danger' ? TriangleAlert : DownloadCloud;

  return (
    <Dialog
      onClose={requestDismiss}
      labelledBy={titleId}
      focusContainerOnOpen={tone === 'primary'}
    >
      <DialogCard size={tone === 'primary' ? 'lg' : 'md'}>
        <DialogHeader
          titleId={titleId}
          title={title}
          subtitle={subtitle}
          icon={<Icon size={18} />}
          tone={tone === 'danger' ? 'danger' : 'accent'}
          onClose={requestDismiss}
          closeDisabled={!dismissAllowed}
          closeLabel={
            busy && activeDismissLabel ? activeDismissLabel : t('close')
          }
        />
        <DialogBody>
          {children}
          {acknowledge && (
            <label className="bpp-confirm-ack">
              <input
                type="checkbox"
                checked={acknowledge.checked}
                disabled={busy}
                onChange={(event) => acknowledge.onChange(event.target.checked)}
              />
              <span>{acknowledge.label}</span>
            </label>
          )}
        </DialogBody>
        <DialogFooter>
          {secondaryLabel ? (
            <Button variant="ghost" onClick={requestDismiss}>
              {secondaryLabel}
            </Button>
          ) : (
            <p
              role="status"
              aria-live="polite"
              className="bpp-confirm-blocked-status"
            >
              {t('operationCannotBeCancelled')}
            </p>
          )}
          <Button
            variant={tone === 'danger' ? 'danger' : 'primary'}
            className="bpp-confirm-submit"
            data-tone={tone}
            disabled={
              (acknowledge ? !acknowledge.checked : false) ||
              Boolean(confirmDisabled)
            }
            busy={busy}
            busyLabel={busyLabel}
            onClick={onConfirm}
          >
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogCard>
    </Dialog>
  );
}

/** A tinted note inside a confirmation body. */
export function ConfirmNote({
  tone,
  icon,
  children
}: {
  tone: 'danger' | 'warning' | 'neutral';
  icon: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className={clsx('bpp-confirm-note', `is-${tone}`)}>
      <span className="bpp-confirm-note-icon" aria-hidden="true">
        {icon}
      </span>
      <div className="bpp-confirm-note-body">{children}</div>
    </div>
  );
}

/** The exact filesystem or data target a confirmation acts on. */
export function ConfirmTarget({ children }: { children: ReactNode }) {
  return <div className="bpp-confirm-target selectable">{children}</div>;
}

export function requestConfirmDialogDismiss({
  busy,
  activeDismissalPolicy,
  onClose
}: {
  busy: boolean;
  activeDismissalPolicy: ActiveDismissalPolicy;
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
