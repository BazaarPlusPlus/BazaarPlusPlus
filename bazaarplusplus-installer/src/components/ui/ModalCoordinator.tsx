import {
  createContext,
  use,
  useEffect,
  useLayoutEffect,
  useRef,
  useSyncExternalStore,
  type ReactNode,
  type RefObject
} from 'react';
import {
  createModalCoordinator,
  restoreModalFocus,
  type ModalCoordinator,
  type ModalDismissalPolicy,
  type ModalPriority,
  type RegisteredModal
} from '../../features/shared/modalCoordinator';

const ModalCoordinatorContext = createContext<ModalCoordinator | null>(null);
const ActiveModalPolicyContext = createContext<ModalDismissalPolicy | null>(
  null
);

export function ModalCoordinatorProvider({
  children
}: {
  children: ReactNode;
}) {
  const coordinatorRef = useRef<ModalCoordinator | null>(null);
  coordinatorRef.current ??= createModalCoordinator();
  const coordinator = coordinatorRef.current;
  const snapshot = useSyncExternalStore(
    coordinator.subscribe,
    coordinator.getSnapshot,
    coordinator.getSnapshot
  );
  const previousActiveRef = useRef<RegisteredModal | null>(null);

  useEffect(() => {
    const previous = previousActiveRef.current;
    previousActiveRef.current = snapshot.active;
    if (!previous || previous.id === snapshot.active?.id) return;

    const wasPreempted = snapshot.queued.some(({ id }) => id === previous.id);
    if (wasPreempted || snapshot.active) return;

    const preferred = previous.restoreFocusTo?.() ?? null;
    let cancelled = false;
    queueMicrotask(() => {
      if (cancelled || coordinator.getSnapshot().active) return;
      restoreModalFocus(preferred, [
        document.querySelector<HTMLElement>('[data-page-heading]'),
        document.querySelector<HTMLElement>('main')
      ]);
    });
    return () => {
      cancelled = true;
    };
  }, [coordinator, snapshot]);

  return (
    <ModalCoordinatorContext.Provider value={coordinator}>
      {children}
    </ModalCoordinatorContext.Provider>
  );
}

export function ModalSource({
  id,
  open,
  priority,
  dismissalPolicy,
  restoreFocusRef,
  children
}: {
  id: string;
  open: boolean;
  priority: ModalPriority;
  dismissalPolicy: ModalDismissalPolicy;
  restoreFocusRef?: RefObject<HTMLElement | null>;
  children: ReactNode;
}) {
  const coordinator = use(ModalCoordinatorContext);
  if (!coordinator) {
    throw new Error(
      'ModalSource must be used inside ModalCoordinatorProvider.'
    );
  }
  const snapshot = useSyncExternalStore(
    coordinator.subscribe,
    coordinator.getSnapshot,
    coordinator.getSnapshot
  );

  useLayoutEffect(() => {
    if (!open) return;
    const activeElement =
      document.activeElement instanceof HTMLElement &&
      document.activeElement !== document.body &&
      document.activeElement !== document.documentElement
        ? document.activeElement
        : null;
    return coordinator.register({
      id,
      priority,
      dismissalPolicy,
      restoreFocusTo: () => restoreFocusRef?.current ?? activeElement
    });
  }, [coordinator, id, open, restoreFocusRef]);

  useLayoutEffect(() => {
    if (!open) return;
    coordinator.update(id, { priority, dismissalPolicy });
  }, [coordinator, dismissalPolicy, id, open, priority]);

  if (!open || snapshot.active?.id !== id) return null;
  return (
    <ActiveModalPolicyContext.Provider value={dismissalPolicy}>
      {children}
    </ActiveModalPolicyContext.Provider>
  );
}

export function useActiveModalDismissalPolicy(): ModalDismissalPolicy | null {
  return use(ActiveModalPolicyContext);
}
