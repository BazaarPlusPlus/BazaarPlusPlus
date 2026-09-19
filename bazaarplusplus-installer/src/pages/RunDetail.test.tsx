// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ModalCoordinatorProvider } from '../components/ui/ModalCoordinator';
import * as commands from '../features/history/historyApi';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
import type { HistoryRunDetail } from '../types/backend';
import RunDetail from './RunDetail';

vi.mock('../features/history/historyApi', () => ({
  loadHistoryRunDetail: vi.fn(),
  revealRunScreenshot: vi.fn(),
  revealBattleVideo: vi.fn(),
  deleteBattleVideo: vi.fn()
}));

function detail(runId = 'run-1'): HistoryRunDetail {
  return {
    run: {
      run_id: runId,
      hero: runId === 'run-1' ? 'Vanessa' : 'Mak',
      game_mode: 'Ranked',
      started_at_utc: '2026-09-12T10:00:00Z',
      ended_at_utc: null,
      status: 'completed',
      result: 'win',
      victories: 10,
      losses: 0,
      final_day: 10,
      final_player_rank: null,
      final_player_rating: null,
      screenshot_id: 's1',
      strip_url: null,
      player_name: null
    },
    battles: [
      {
        battle_id: 'battle-1',
        day: 1,
        hour: 2,
        result: 'win',
        opponent_hero: 'Pygmalien',
        opponent_name: null,
        opponent_rank: null,
        opponent_rating: null,
        video: {
          video_id: 'video-1',
          status: 'completed',
          file_size_bytes: 100,
          duration_ms: 2000
        }
      }
    ]
  };
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  localStorage.setItem(LOCALE_STORAGE_KEY, 'en');
  vi.mocked(commands.loadHistoryRunDetail)
    .mockReset()
    .mockImplementation(async (runId) => detail(runId));
  vi.mocked(commands.deleteBattleVideo).mockReset();
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
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  vi.unstubAllGlobals();
});

async function render() {
  await act(async () =>
    root.render(
      <StrictMode>
        <LocaleProvider>
          <ModalCoordinatorProvider>
            <MemoryRouter initialEntries={['/history/run-1']}>
              <Link to="/history/run-2">Another run</Link>
              <Routes>
                <Route path="/history/:runId" element={<RunDetail />} />
              </Routes>
            </MemoryRouter>
          </ModalCoordinatorProvider>
        </LocaleProvider>
      </StrictMode>
    )
  );
}

async function click(selector: string) {
  const button = container.querySelector<HTMLButtonElement>(selector);
  expect(button).not.toBeNull();
  await act(async () => button!.click());
}

describe('Run Detail workflow binding', () => {
  it('renders new detail after navigation even without a keyed route remount', async () => {
    await render();
    expect(container.querySelector('[data-page-heading]')?.textContent).toBe(
      'Vanessa'
    );
    await click('a[href="/history/run-2"]');
    expect(container.querySelector('[data-page-heading]')?.textContent).toBe(
      'Mak'
    );
    expect(commands.loadHistoryRunDetail).toHaveBeenLastCalledWith('run-2');
  });

  it('keeps failed video deletion retryable and blocks dismissal while retry runs', async () => {
    let finish!: (value: HistoryRunDetail) => void;
    const deletion = new Promise<HistoryRunDetail>((resolve) => {
      finish = resolve;
    });
    vi.mocked(commands.deleteBattleVideo)
      .mockRejectedValueOnce(new Error('disk busy'))
      .mockReturnValueOnce(deletion);
    await render();
    await click('button[title="Delete Video"]');
    expect(container.querySelector('dialog')?.textContent).toContain(
      'Pygmalien'
    );
    await click('dialog button.bpp-confirm-submit');
    expect(container.querySelector('dialog')?.textContent).toContain(
      'disk busy'
    );
    expect(
      container.querySelector('dialog button.bpp-confirm-submit')?.textContent
    ).toContain('Retry');
    await click('dialog button.bpp-confirm-submit');
    expect(
      container.querySelector<HTMLButtonElement>(
        'dialog button[aria-label="Close"]'
      )?.disabled
    ).toBe(true);
    await act(async () =>
      container
        .querySelector('dialog')!
        .dispatchEvent(new Event('cancel', { cancelable: true }))
    );
    expect(container.querySelector('dialog')?.open).toBe(true);
    const updated = detail();
    updated.battles[0].video = null;
    await act(async () => finish(updated));
    expect(container.querySelector('dialog')).toBeNull();
    expect(container.querySelector('button[title="Delete Video"]')).toBeNull();
    expect(commands.deleteBattleVideo).toHaveBeenLastCalledWith(
      'battle-1',
      'video-1'
    );
  });
});
