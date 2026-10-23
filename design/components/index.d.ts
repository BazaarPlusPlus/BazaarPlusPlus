// BazaarPlusPlus component props, from the installer's components/ui and the
// site's shared/components, converged where the two had duplicates. Documentation only: this system ships the
// stylesheet (bundle.css) and static previews, not a runtime bundle.
import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'md' | 'sm' | 'icon' | 'icon-sm';
export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Leading icon. Replaced by a spinner while `busy`. */
  icon?: ReactNode;
  busy?: boolean;
  /** Label shown while busy; the button keeps the wider label's width. */
  busyLabel?: string;
}
/** Class list for anything that should look like a Button (router Link, <a>). */
export declare function buttonClassName(opts?: { variant?: ButtonVariant; size?: ButtonSize; className?: string }): string;

export type BadgeTone = 'ok' | 'warn' | 'bad' | 'busy' | 'neutral' | 'accent';
/** The one 22px label pill. A status reads as dot + word; tiers and neutral facts have no dot. */
export interface BadgeProps {
  tone?: BadgeTone;
  /** Outcome tier pill (`.bpp-outcome`); no dot. */
  tier?: 'misfortune' | 'bronze' | 'silver' | 'gold' | 'diamond';
  /** A run in progress (success tint). */
  active?: boolean;
  dot?: boolean;
  className?: string;
  children: ReactNode;
}

/** Site: the one hero chip. */
export interface HeroBadgeProps {
  hero: string;
  selected?: boolean;
  /** `short` shows the three-letter code and keeps the full name for assistive tech. */
  label?: 'short' | 'full';
  /** Trailing value, such as a win rate or the active window. */
  children?: ReactNode;
}

export type StatusBannerTone = 'info' | 'warning' | 'error' | 'success';
export interface StatusBannerProps extends HTMLAttributes<HTMLDivElement> {
  tone?: StatusBannerTone;
  /** Defaults to the tone's icon; pass `null` to omit it. */
  icon?: ReactNode;
  message: ReactNode;
  actions?: ReactNode;
  diagnostic?: ReactNode;
  diagnosticLabel?: string;
}
/** The one way a page shows a semantic problem. */
export interface ProblemBannerProps {
  message: string;
  problem?: { diagnostic?: unknown } | null;
  tone?: 'error' | 'warning';
  onRetry?: () => void;
  retryBusy?: boolean;
  retryBusyLabel?: string;
  /** Extra feature actions, rendered before Retry. */
  actions?: ReactNode;
}

export type ToastTone = 'success' | 'info' | 'warning' | 'error';
export interface ToastProps {
  id?: string;
  tone: ToastTone;
  message: string;
  durationMs?: number;
  action?: { label: string; onClick: () => void };
}

export interface DialogProps {
  onClose: () => void;
  labelledBy?: string;
  focusContainerOnOpen?: boolean;
  className?: string;
  children: ReactNode;
}
export interface DialogCardProps { size?: 'sm' | 'md' | 'lg'; className?: string; children: ReactNode }
export interface DialogHeaderProps {
  titleId: string;
  title: ReactNode;
  subtitle?: ReactNode;
  icon?: ReactNode;
  tone?: 'neutral' | 'accent' | 'danger';
  onClose?: () => void;
  closeDisabled?: boolean;
  closeLabel?: string;
}
export type ActiveDismissalPolicy =
  | { kind: 'blocked' }
  | { kind: 'detachable'; label: string }
  | { kind: 'cancelable'; label: string; onCancel: () => void };
export interface ConfirmDialogProps {
  titleId: string;
  title: string;
  subtitle?: string;
  tone: 'primary' | 'danger';
  children: ReactNode;
  acknowledge?: { label: string; checked: boolean; onChange: (checked: boolean) => void };
  confirmLabel: string;
  busyLabel?: string;
  busy: boolean;
  activeDismissalPolicy: ActiveDismissalPolicy;
  dismissLabel?: string;
  confirmDisabled?: boolean;
  onConfirm: () => unknown;
  onClose: () => void;
}

export interface SegmentedControlProps<Value extends string> {
  label: string;
  name: string;
  value: Value;
  options: readonly { value: Value; label: ReactNode; disabled?: boolean }[];
  onChange: (value: Value) => void;
  disabled?: boolean;
  className?: string;
}

/** One layout for loading, empty, error and not-found (replaces EmptyState and LoadingPanel). */
export interface StatusPanelProps {
  kind: 'loading' | 'error' | 'empty' | 'not-found';
  /** Mark for `empty`; defaults to Inbox. */
  icon?: ReactNode;
  title: string;
  body?: string;
  /** Single-line loading state: spinner + label. */
  compact?: boolean;
  headingLevel?: 1 | 2 | 3;
  /** Draw the panel frame; off when the status sits inside another panel. */
  framed?: boolean;
  actions?: ReactNode;
}

export interface PageHeaderProps {
  title: string;
  /** Rendered before the title, e.g. a back button. */
  leading?: ReactNode;
  /** Short secondary facts beside the title. */
  meta?: ReactNode;
  action?: ReactNode;
}
