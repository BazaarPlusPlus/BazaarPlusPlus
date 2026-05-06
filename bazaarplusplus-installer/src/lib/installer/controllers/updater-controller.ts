import { writable, get } from 'svelte/store';
import type { Update } from '@tauri-apps/plugin-updater';

import {
  createCheckingUpdaterSnapshot,
  downloadPendingUpdate,
  resolveUpdaterActionDecision,
  runStartupUpdaterCheck
} from '../updater-flow.ts';
import { createInitialUpdaterSnapshot } from '../../updater.ts';
import type { TranslateText } from '../selectors/types.ts';

export function createUpdaterController(input: {
  hasTauriRuntime: () => boolean;
}) {
  const updaterSnapshot = writable(createInitialUpdaterSnapshot());
  const pendingUpdate = writable<Update | null>(null);
  const showUpdaterModal = writable(false);
  const updaterModalTitle = writable('');
  const updaterModalBody = writable('');
  const showUpdaterReviewModal = writable(false);
  const updaterReviewBusy = writable(false);
  let updaterCheckRequestId = 0;

  function openUpdaterModal(title: string, body: string) {
    updaterModalTitle.set(title);
    updaterModalBody.set(body);
    showUpdaterModal.set(true);
  }

  function closeUpdaterModal() {
    showUpdaterModal.set(false);
  }

  function openUpdaterReviewModal() {
    showUpdaterReviewModal.set(true);
  }

  function closeUpdaterReviewModal() {
    if (get(updaterReviewBusy)) {
      return;
    }

    showUpdaterReviewModal.set(false);
  }

  async function runStartupCheck() {
    return runStartupUpdaterCheck({
      snapshot: get(updaterSnapshot),
      hasTauriRuntime: input.hasTauriRuntime()
    });
  }

  async function checkForUpdatesOnStartup() {
    const requestId = ++updaterCheckRequestId;

    updaterSnapshot.set(
      createCheckingUpdaterSnapshot(get(updaterSnapshot), input.hasTauriRuntime())
    );

    const result = await runStartupCheck();
    if (
      requestId !== updaterCheckRequestId ||
      get(updaterSnapshot).status !== 'checking'
    ) {
      return;
    }

    updaterSnapshot.set(result.snapshot);
    pendingUpdate.set(result.update);
  }

  async function startPendingUpdateDownload(update: Update, t: TranslateText) {
    const result = await downloadPendingUpdate({
      snapshot: get(updaterSnapshot),
      update,
      t,
      onProgress: (snapshot) => {
        updaterSnapshot.set(snapshot);
      }
    });

    updaterSnapshot.set(result.snapshot);
    openUpdaterModal(result.modal.title, result.modal.body);
  }

  async function confirmUpdaterReview(t: TranslateText) {
    const update = get(pendingUpdate);
    if (!update || get(updaterReviewBusy)) {
      return;
    }

    updaterReviewBusy.set(true);
    try {
      await startPendingUpdateDownload(update, t);
      showUpdaterReviewModal.set(false);
    } finally {
      updaterReviewBusy.set(false);
    }
  }

  async function handleUpdaterAction(t: TranslateText) {
    const decision = resolveUpdaterActionDecision({
      snapshot: get(updaterSnapshot),
      pendingUpdate: get(pendingUpdate),
      hasTauriRuntime: input.hasTauriRuntime(),
      t
    });

    if (decision.type === 'noop') {
      return;
    }

    if (decision.type === 'check') {
      await checkForUpdatesOnStartup();
      return;
    }

    if (decision.type === 'open_review') {
      openUpdaterReviewModal();
      return;
    }

    openUpdaterModal(decision.modal.title, decision.modal.body);
  }

  return {
    updaterSnapshot,
    pendingUpdate,
    showUpdaterModal,
    updaterModalTitle,
    updaterModalBody,
    showUpdaterReviewModal,
    updaterReviewBusy,
    openUpdaterModal,
    closeUpdaterModal,
    openUpdaterReviewModal,
    closeUpdaterReviewModal,
    checkForUpdatesOnStartup,
    confirmUpdaterReview,
    handleUpdaterAction
  };
}
