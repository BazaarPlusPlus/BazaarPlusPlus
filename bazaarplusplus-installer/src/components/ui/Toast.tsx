import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  Info,
  X
} from 'lucide-react';
import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode
} from 'react';
import { useI18n } from '../../i18n/LocaleProvider';

export type ToastTone = 'success' | 'info' | 'warning' | 'error';

type ToastAction = {
  label: string;
  onClick: () => void;
};

type ToastInput = {
  id?: string;
  tone: ToastTone;
  message: string;
  durationMs?: number;
  action?: ToastAction;
};

type ToastRecord = Required<Pick<ToastInput, 'id' | 'tone' | 'message'>> & {
  action?: ToastAction;
  present: boolean;
  closing: boolean;
};

type ToastController = {
  showToast: (toast: ToastInput) => void;
  dismissToast: (id: string) => void;
};

const ToastContext = createContext<ToastController | null>(null);
const EXIT_DURATION_MS = 160;
let nextToastId = 0;

export function ToastProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const [toasts, setToasts] = useState<ToastRecord[]>([]);
  const timers = useRef(new Map<string, number>());

  const clearTimer = useCallback((key: string) => {
    const timer = timers.current.get(key);
    if (timer !== undefined) {
      window.clearTimeout(timer);
      timers.current.delete(key);
    }
  }, []);

  const dismissToast = useCallback(
    (id: string) => {
      clearTimer(`auto:${id}`);
      if (timers.current.has(`exit:${id}`)) return;
      setToasts((current) =>
        current.map((toast) =>
          toast.id === id ? { ...toast, closing: true, present: false } : toast
        )
      );
      const timer = window.setTimeout(() => {
        setToasts((current) => current.filter((toast) => toast.id !== id));
        timers.current.delete(`exit:${id}`);
      }, EXIT_DURATION_MS);
      timers.current.set(`exit:${id}`, timer);
    },
    [clearTimer]
  );

  const showToast = useCallback(
    ({
      id = `toast-${++nextToastId}`,
      tone,
      message,
      durationMs = tone === 'error' ? 0 : 4000,
      action
    }: ToastInput) => {
      clearTimer(`auto:${id}`);
      clearTimer(`exit:${id}`);
      setToasts((current) => [
        ...current.filter((toast) => toast.id !== id),
        { id, tone, message, action, present: false, closing: false }
      ]);
      requestAnimationFrame(() => {
        setToasts((current) =>
          current.map((toast) =>
            toast.id === id ? { ...toast, present: true } : toast
          )
        );
      });
      if (durationMs > 0) {
        const timer = window.setTimeout(() => dismissToast(id), durationMs);
        timers.current.set(`auto:${id}`, timer);
      }
    },
    [clearTimer, dismissToast]
  );

  useEffect(
    () => () => {
      for (const timer of timers.current.values()) window.clearTimeout(timer);
      timers.current.clear();
    },
    []
  );

  const controller = useMemo(
    () => ({ showToast, dismissToast }),
    [dismissToast, showToast]
  );
  const viewportRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport || typeof viewport.showPopover !== 'function') return;
    try {
      if (viewport.matches(':popover-open') === false) {
        viewport.showPopover();
      }
    } catch {
      // Browsers without popover or already-open state are fine.
    }
  }, [toasts.length]);

  return (
    <ToastContext.Provider value={controller}>
      {children}
      <div
        ref={viewportRef}
        popover="manual"
        className="bpp-toast-viewport"
        aria-label={t('notifications')}
      >
        {toasts.map((toast) => (
          <ToastItem
            key={toast.id}
            toast={toast}
            onDismiss={() => dismissToast(toast.id)}
            closeLabel={t('close')}
          />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastController {
  const controller = use(ToastContext);
  if (!controller)
    throw new Error('useToast must be used inside ToastProvider.');
  return controller;
}

function ToastItem({
  toast,
  onDismiss,
  closeLabel
}: {
  toast: ToastRecord;
  onDismiss: () => void;
  closeLabel: string;
}) {
  const Icon =
    toast.tone === 'success'
      ? CheckCircle2
      : toast.tone === 'warning'
        ? AlertTriangle
        : toast.tone === 'error'
          ? AlertCircle
          : Info;

  return (
    <div
      role={toast.tone === 'error' ? 'alert' : 'status'}
      aria-live={toast.tone === 'error' ? 'assertive' : 'polite'}
      className={`bpp-toast is-${toast.tone}${toast.present ? ' is-present' : ''}${toast.closing ? ' is-closing' : ''}`}
    >
      <Icon size={16} className="bpp-toast-icon" aria-hidden="true" />
      <div className="bpp-toast-copy">
        <p className="bpp-toast-message">{toast.message}</p>
        {toast.action && (
          <button
            type="button"
            className="bpp-toast-action"
            onClick={() => {
              toast.action?.onClick();
              onDismiss();
            }}
          >
            {toast.action.label}
          </button>
        )}
      </div>
      <button
        type="button"
        className="bpp-toast-close"
        onClick={onDismiss}
        aria-label={closeLabel}
      >
        <X size={15} />
      </button>
    </div>
  );
}
