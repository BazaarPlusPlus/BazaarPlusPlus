import clsx from 'clsx';
import { DownloadCloud, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import {
  Button,
  Dialog,
  DialogBody,
  DialogCard,
  DialogFooter,
  DialogHeader
} from '@bazaarplusplus/ui';
import { useActiveModalDismissalPolicy } from './ModalCoordinator';
import { useI18n } from '../../i18n/LocaleProvider';

export type ConfirmTone = 'primary' | 'danger';

export type ActiveDismissalPolicy =
  | { kind: 'blocked' }
  | { kind: 'detachable'; label: string }
  | { kind: 'cancelable'; label: string; onCancel: () => void };

export interface ConfirmAcknowledge {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}

export interface ConfirmDialogProps {
  titleId: string;
  title: string;
  subtitle?: string;
  tone: ConfirmTone;
  children: ReactNode;
  acknowledge?: ConfirmAcknowledge;
  confirmLabel: string;
  busyLabel?: string;
  busy: boolean;
  activeDismissalPolicy: ActiveDismissalPolicy;
  dismissLabel?: string;
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
  const dismissible = useActiveModalDismissalPolicy() !== 'blocked';
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
      dismissible={dismissible}
      onClose={requestDismiss}
      labelledBy={titleId}
      focusContainerOnOpen={tone === 'primary'}
    >
      <DialogCard size={tone === 'primary' ? 'lg' : 'md'}>
        <DialogHeader
          titleId={titleId}
          title={title}
          subtitle={subtitle}
          icon={<Icon size={16} />}
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
            data-confirm-submit
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
