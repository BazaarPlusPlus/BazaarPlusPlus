# Frontend Architecture

## Native Boundary

- `commandClient` in `src/api/commandClient.ts` selects the generated native adapter or Browser Preview once at module load. Feature APIs call typed adapter functions rather than invoking Tauri command strings.
- `normalizeBackendError` in `src/api/nativeCommands.ts` preserves a valid `SemanticProblem` as structured code, parameters, and optional diagnostic. Presenters map that structure to localized copy; diagnostics are never the primary message.
- Preview defaults live in `src/api/previewDefaults.ts` and are reused by `createPreviewCommands` in `src/api/previewCommands.ts`, keeping browser-preview behavior at the adapter boundary.

## Workflow State

- `PageState` in `src/features/shared/pageState.ts` separates initial loading, blocking failure, ready-empty, and ready-content. Refreshing and refresh failure retain successful data, and request ids reject stale completions.
- `DefaultInstallWorkflow` in `src/features/install/installWorkflow.ts` owns the authoritative install snapshot, action single-flight, target-bearing confirmations, retry parameters, and primary-action derivation.
- `DefaultStreamWorkflow` in `src/features/stream/streamWorkflow.ts` owns service freshness, window, crop, and one-off capability states. React hooks create workflows once and bind lifecycle plus subscription rather than duplicating orchestration.
- About bootstrap uses the separate `AppBootstrapSnapshot` in `src/features/about/appBootstrap.ts` because packaged fallback data can remain usable while native provenance fails.

## Confirmations And Modals

- `createConfirmedOperationController` in `src/features/shared/confirmedOperation.ts` retains the target through confirmation, running, and failure; native work blocks dismissal; failure keeps retry state; success closes the operation.
- `createModalCoordinator` in `src/features/shared/modalCoordinator.ts` renders one registered dialog, orders sources by priority and FIFO, tolerates route-source unmount, and restores focus after the queue drains.
- `ConfirmDialog` in `src/components/ui/ConfirmDialog.tsx` consumes an explicit dismissal policy. Callers must represent whether work is blocked, detachable, or genuinely cancelable instead of presenting a cosmetic cancel action.

## Locale Boundary

The `zh` catalog and `MessageKey` type in `src/i18n/messages.ts` define the message-key set; `en` is typed against it. Workflow state stores semantic values, while translation happens during presentation so changing locale does not recreate native workflows. Updater-specific presentation belongs to [Updater](updater.md).
