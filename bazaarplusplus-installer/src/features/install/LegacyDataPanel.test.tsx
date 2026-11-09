// @vitest-environment jsdom

// The panel as the Install page shows it: the reinstall hint while the installed
// mod writes an old Data Root, and each listed root with its delete action.
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  vi
} from 'vite-plus/test';
import { LocaleProvider } from '../../i18n/LocaleProvider';
import { formatMessage, LOCALE_STORAGE_KEY } from '../../i18n/messages';
import type { LegacyDataState } from '../../types/backend';
import { LegacyDataPanel } from './LegacyDataPanel';
import { createLegacyDataWorkflow } from './legacyDataWorkflow';

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
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function show(data: LegacyDataState) {
  const workflow = createLegacyDataWorkflow({
    commands: {
      getLegacyDataState: vi.fn(async () => data),
      deleteLegacyRoot: vi.fn()
    }
  });
  await workflow.load(GAME);
  await act(async () =>
    root.render(
      <LocaleProvider>
        <LegacyDataPanel
          snapshot={workflow.getSnapshot()}
          workflow={workflow}
          modInstalled
          gamePath={GAME}
        />
      </LocaleProvider>
    )
  );
}

describe('legacy data panel', () => {
  it('shows the reinstall hint while the mod writes an old Data Root', async () => {
    await show(state({ installed_mod_data_root: 'reinstall_required' }));

    expect(container.textContent).toContain(zh('legacyModReinstallRequired'));
  });

  it('lists a root with its delete action and no hint for a current mod', async () => {
    await show(state({}));

    expect(container.textContent).toContain('BazaarPlusPlusV5');
    expect(container.textContent).not.toContain(
      zh('legacyModReinstallRequired')
    );
    const remove = container.querySelector(
      `button[aria-label="${formatMessage('zh', 'legacyDeleteAction', {
        name: 'BazaarPlusPlusV5'
      })}"]`
    );
    expect(remove).not.toBeNull();
  });
});
