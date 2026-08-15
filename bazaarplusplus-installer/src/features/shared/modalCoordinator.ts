export type ModalPriority =
  'critical' | 'confirmation' | 'system' | 'informational';

export type ModalDismissalPolicy = 'dismissible' | 'blocked';

export interface ModalFocusTarget {
  readonly isConnected: boolean;
  focus(): void;
}

export interface ModalRequest {
  id: string;
  priority: ModalPriority;
  dismissalPolicy: ModalDismissalPolicy;
  restoreFocusTo?: () => ModalFocusTarget | null;
}

export type RegisteredModal = ModalRequest & { sequence: number };

export type ModalCoordinatorSnapshot = {
  active: RegisteredModal | null;
  queued: readonly RegisteredModal[];
};

export interface ModalCoordinator {
  getSnapshot(): ModalCoordinatorSnapshot;
  subscribe(listener: () => void): () => void;
  register(request: ModalRequest): () => void;
  update(id: string, patch: Partial<Omit<ModalRequest, 'id'>>): boolean;
  unregister(id: string): boolean;
}

const PRIORITY_RANK: Record<ModalPriority, number> = {
  critical: 4,
  confirmation: 3,
  system: 2,
  informational: 1
};

export function createModalCoordinator(): ModalCoordinator {
  const entries = new Map<string, RegisteredModal>();
  const listeners = new Set<() => void>();
  let nextSequence = 0;
  let activeId: string | null = null;
  let snapshot: ModalCoordinatorSnapshot = { active: null, queued: [] };

  const sortedEntries = () =>
    [...entries.values()].sort(
      (left, right) =>
        PRIORITY_RANK[right.priority] - PRIORITY_RANK[left.priority] ||
        left.sequence - right.sequence
    );

  const publish = () => {
    const ordered = sortedEntries();
    const current = activeId ? entries.get(activeId) : null;
    if (!current) {
      activeId = ordered[0]?.id ?? null;
    } else {
      const challenger = ordered.find(({ id }) => id !== current.id);
      if (
        challenger &&
        PRIORITY_RANK[challenger.priority] > PRIORITY_RANK[current.priority]
      ) {
        activeId = challenger.id;
      }
    }

    const active = activeId ? (entries.get(activeId) ?? null) : null;
    snapshot = {
      active,
      queued: sortedEntries().filter(({ id }) => id !== active?.id)
    };
    for (const listener of listeners) listener();
  };

  const unregister = (id: string) => {
    if (!entries.delete(id)) return false;
    if (activeId === id) activeId = null;
    publish();
    return true;
  };

  return {
    getSnapshot: () => snapshot,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    register: (request) => {
      const existing = entries.get(request.id);
      entries.set(request.id, {
        ...request,
        sequence: existing?.sequence ?? nextSequence++
      });
      publish();
      return () => {
        unregister(request.id);
      };
    },
    update: (id, patch) => {
      const existing = entries.get(id);
      if (!existing) return false;
      entries.set(id, { ...existing, ...patch, id });
      publish();
      return true;
    },
    unregister
  };
}

export function restoreModalFocus(
  preferred: ModalFocusTarget | null,
  fallbacks: readonly (ModalFocusTarget | null)[]
): ModalFocusTarget | null {
  const target = [preferred, ...fallbacks].find(
    (candidate): candidate is ModalFocusTarget =>
      candidate !== null && candidate.isConnected
  );
  target?.focus();
  return target ?? null;
}
