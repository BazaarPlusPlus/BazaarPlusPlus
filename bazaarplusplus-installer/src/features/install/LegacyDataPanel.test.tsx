// @vitest-environment jsdom

// The import card's states as the Install page shows them: hidden behind the
// reinstall hint while the installed mod writes an old Data Root, offered when
// eligible, in progress, and failed with its typed recovery copy.
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SemanticProblemError } from '../../api/problems';
import { LocaleProvider } from '../../i18n/LocaleProvider';
import { formatMessage, LOCALE_STORAGE_KEY } from '../../i18n/messages';
import type {
  ImportV5HistoryResult,
  LegacyDataState
} from '../../types/backend';
import { LegacyDataPanel } from './LegacyDataPanel';
import { LegacyRootDeleteConfirmModal } from './LegacyRootDeleteConfirmModal';
import {
  createLegacyDataWorkflow,
  type LegacyDataCommandPort,
  type LegacyDataWorkflow
} from './legacyDataWorkflow';

const GAME = '/Games/The Bazaar';
const zh = (key: Parameters<typeof formatMessage>[1]) =>
  formatMessage('zh', key);

function state(overrides: Partial<LegacyDataState>): LegacyDataState {
  return {
    game_path: GAME,
    roots: [
      {
        name: 'BazaarPlusPlusV5',
        size_bytes: 4096,
        file_count: 3,
        hard_linked_file_count: 0,
        unreadable_entry_count: 0
      }
    ],
    installed_mod_data_root: 'current',
    v5_import_eligible: true,
    ...overrides
  };
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  // jsdom does not implement the native dialog lifecycle.
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.open = true;
    }
  });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.open = false;
    }
  });
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function show(
  data: LegacyDataState,
  overrides: Partial<LegacyDataCommandPort> = {}
): Promise<LegacyDataWorkflow> {
  const workflow = createLegacyDataWorkflow({
    commands: {
      getLegacyDataState: vi.fn(async () => data),
      importV5History: vi.fn(async () => ({
        imported_run_count: 1,
        skipped_run_count: 0,
        unavailable_file_count: 0,
        state: state({ v5_import_eligible: false })
      })),
      deleteLegacyRoot: vi.fn(),
      ...overrides
    }
  });
  await workflow.load(GAME);
  const render = () =>
    root.render(
      <LocaleProvider>
        <LegacyDataPanel
          snapshot={workflow.getSnapshot()}
          workflow={workflow}
          modInstalled
          gamePath={GAME}
        />
      </LocaleProvider>
    );
  workflow.subscribe(render);
  await act(async () => render());
  return workflow;
}

function importButton() {
  return [...container.querySelectorAll('button')].find(
    (button) => button.textContent === zh('legacyImportAction')
  );
}

describe('V5 import card', () => {
  it('shows only the reinstall hint while the mod writes an old Data Root', async () => {
    await show(
      state({
        installed_mod_data_root: 'reinstall_required',
        v5_import_eligible: false
      })
    );

    expect(container.textContent).toContain(zh('legacyModReinstallRequired'));
    expect(container.textContent).not.toContain(zh('legacyImportHeading'));
    expect(importButton()).toBeUndefined();
  });

  it('offers the import when eligible', async () => {
    await show(state({}));

    expect(container.querySelector('#legacy-data')).not.toBeNull();
    expect(container.textContent).toContain(zh('legacyImportHeading'));
    expect(container.textContent).not.toContain(
      zh('legacyModReinstallRequired')
    );
    expect(importButton()?.disabled).toBe(false);
  });

  it('shows progress and blocks deletion while importing', async () => {
    let finish!: () => void;
    await show(state({}), {
      importV5History: vi.fn(
        () =>
          new Promise<ImportV5HistoryResult>((resolve) => {
            finish = () =>
              resolve({
                imported_run_count: 1,
                skipped_run_count: 0,
                unavailable_file_count: 0,
                state: state({ v5_import_eligible: false })
              });
          })
      )
    });

    await act(async () => importButton()!.click());

    expect(container.querySelector('[role="status"]')?.textContent).toBe(
      zh('legacyImportRunning')
    );
    const deleteButton = container.querySelector<HTMLButtonElement>(
      `button[aria-label="${formatMessage('zh', 'legacyDeleteAction', {
        name: 'BazaarPlusPlusV5'
      })}"]`
    );
    expect(deleteButton?.disabled).toBe(true);

    await act(async () => finish());
    // Imported: the card leaves, and V5 stays listed until a confirmation.
    expect(container.textContent).not.toContain(zh('legacyImportHeading'));
    expect(container.textContent).toContain('BazaarPlusPlusV5');
  });

  it('shows the typed failure and keeps the card for a retry', async () => {
    await show(state({}), {
      importV5History: vi.fn(async () => {
        throw new SemanticProblemError({
          code: 'import_failed',
          params: { operation: 'import_v5_history', reason: 'file_conflict' },
          diagnostic: null
        });
      })
    });

    await act(async () => importButton()!.click());

    expect(container.textContent).toContain(zh('importFailedFiles'));
    expect(importButton()?.disabled).toBe(false);
  });
});

describe('offer to delete V5 after an import', () => {
  it('reports the import, promises no reclaimed space, and can be kept', async () => {
    const onClose = vi.fn();
    await act(async () =>
      root.render(
        <LocaleProvider>
          <LegacyRootDeleteConfirmModal
            busy={false}
            acknowledged={false}
            targetPath={GAME}
            name="BazaarPlusPlusV5"
            problem={null}
            failurePaths={[]}
            afterImport={{
              imported_run_count: 8,
              skipped_run_count: 1,
              unavailable_file_count: 0
            }}
            onAcknowledgedChange={() => {}}
            onClose={onClose}
            onConfirm={() => {}}
          />
        </LocaleProvider>
      )
    );

    const text = container.textContent ?? '';
    expect(text).toContain(
      formatMessage('zh', 'legacyDeleteAfterImport', { imported: 8 })
    );
    expect(text).toContain(
      formatMessage('zh', 'legacyDeleteAfterImportSkipped', { count: 1 })
    );
    expect(text).not.toContain(
      formatMessage('zh', 'legacyDeleteAfterImportUnavailableFiles', {
        count: 0
      })
    );
    expect(text).toContain(zh('legacyDeleteConfirmSpace'));
    const keep = [...container.querySelectorAll('button')].find(
      (button) => button.textContent === zh('legacyDeleteKeep')
    );
    await act(async () => keep!.click());
    expect(onClose).toHaveBeenCalled();
  });
});
