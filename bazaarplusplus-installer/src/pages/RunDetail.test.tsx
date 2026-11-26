// @vitest-environment jsdom

import { Blob as NodeBlob } from 'node:buffer';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import {
  Link,
  MemoryRouter,
  Route,
  Routes,
  useLocation
} from 'react-router-dom';
import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  vi
} from 'vite-plus/test';
import { ModalCoordinatorProvider } from '../components/ui/ModalCoordinator';
import { commandClient } from '../api/commandClient';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
import type { HistoryRunDetail } from '../types/backend';
import RunDetail from './RunDetail';

vi.mock('../api/commandClient', () => ({
  commandClient: {
    getCardCatalog: vi.fn().mockResolvedValue(catalog(null)),
    getHistoryRunDetail: vi.fn(),
    revealRunScreenshot: vi.fn(),
    revealBattleVideo: vi.fn(),
    deleteBattleVideo: vi.fn()
  }
}));
const commands = commandClient;

function catalog(revision: string | null) {
  return {
    revision,
    current: revision !== null,
    gameFound: true,
    progress: null,
    failed: false,
    exportedAtMs: null,
    files: 0,
    bytes: 0
  };
}

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
      final_player_rating_delta: null,
      screenshot_id: 's1',
      player_name: null
    },
    decision_path: { state: 'absent' },
    battles: [
      {
        recorded_at_ms: 0,
        encounter_id: null,
        combat_kind: 'PVP',
        battle_id: 'battle-1',
        day: 1,
        hour: 2,
        result: 'win',
        opponent_hero: 'Pygmalien',
        opponent_name: null,
        opponent_rank: null,
        opponent_rating: null,
        player_board: null,
        player_skills: null,
        opponent_board: null,
        opponent_skills: null,
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
  vi.mocked(commands.getHistoryRunDetail)
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
  vi.mocked(commands.getCardCatalog).mockResolvedValue(catalog(null));
  container.remove();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  vi.unstubAllGlobals();
});

async function render(historySearch?: string) {
  await act(async () =>
    root.render(
      <StrictMode>
        <LocaleProvider>
          <ModalCoordinatorProvider>
            <MemoryRouter
              initialEntries={[
                { pathname: '/history/run-1', state: { historySearch } }
              ]}
            >
              <Link to="/history/run-2">Another run</Link>
              <Routes>
                <Route path="/history/:runId" element={<RunDetail />} />
                <Route path="/history" element={<ListRoute />} />
              </Routes>
            </MemoryRouter>
          </ModalCoordinatorProvider>
        </LocaleProvider>
      </StrictMode>
    )
  );
}

function ListRoute() {
  const location = useLocation();
  return (
    <output data-testid="list">{location.pathname + location.search}</output>
  );
}

async function click(selector: string) {
  const button = container.querySelector<HTMLButtonElement>(selector);
  expect(button).not.toBeNull();
  await act(async () => button!.click());
}

describe('Run Detail back link', () => {
  it('returns to the History List page and filter it came from', async () => {
    await render('?hero=Vanessa&result=win&page=2');
    await click('button[aria-label="Back to History"]');
    expect(container.querySelector('[data-testid="list"]')?.textContent).toBe(
      '/history?hero=Vanessa&result=win&page=2'
    );
  });

  it('returns to the first unfiltered page without a recorded list', async () => {
    await render();
    await click('button[aria-label="Back to History"]');
    expect(container.querySelector('[data-testid="list"]')?.textContent).toBe(
      '/history'
    );
  });
});

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
    expect(commands.getHistoryRunDetail).toHaveBeenLastCalledWith('run-2');
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
    await click('button[title="Delete video"]');
    expect(container.querySelector('dialog')?.textContent).toContain(
      'Pygmalien'
    );
    await click('dialog button[data-confirm-submit]');
    expect(container.querySelector('dialog')?.textContent).toContain(
      'disk busy'
    );
    expect(
      container.querySelector('dialog button[data-confirm-submit]')?.textContent
    ).toContain('Retry');
    await click('dialog button[data-confirm-submit]');
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
    expect(container.querySelector('button[title="Delete video"]')).toBeNull();
    expect(commands.deleteBattleVideo).toHaveBeenLastCalledWith(
      'battle-1',
      'video-1'
    );
  });
});

describe('battle boards', () => {
  it.each(['index', 'image'])(
    'keeps localized names after an %s failure',
    async (failure) => {
      localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
      const data = detail();
      data.battles[0].player_board = [
        {
          instance_id: 'instance-1',
          template_id: 'card-1',
          name: 'Example',
          name_zh: '测试物品',
          tier: 'Silver',
          enchant: 'Golden',
          slot: 3,
          size: 2
        }
      ];
      data.battles[0].opponent_board = [];
      vi.mocked(commands.getHistoryRunDetail).mockResolvedValue(data);
      if (failure === 'index')
        vi.mocked(commands.getCardCatalog).mockRejectedValue(
          new Error('no game')
        );
      else {
        vi.mocked(commands.getCardCatalog).mockResolvedValue(
          catalog('image-failure')
        );
        vi.stubGlobal(
          'fetch',
          vi.fn(async () =>
            Response.json({ cards: { 'card-1': '01234567.webp' } })
          )
        );
      }
      await render();
      const tile = container.querySelector('.bpp-board-card')!;
      if (failure === 'image') {
        await vi.waitFor(() =>
          expect(container.querySelector('.bpp-board-card img')).not.toBeNull()
        );
        const image = container.querySelector('.bpp-board-card img')!;
        expect(image.getAttribute('src')).toBe('/card-art/01234567.webp');
        await act(async () => image.dispatchEvent(new Event('error')));
      }
      expect(tile.textContent).toContain('测试物品');
      expect(tile.textContent).toContain('白银');
      expect(tile.textContent).toContain('黄金');
      const disclosure = container.querySelector('details')!;
      expect(disclosure.open).toBe(false);
      expect(disclosure.textContent).toContain('空阵容');
      expect(disclosure.textContent).toContain('未记录技能');
    }
  );
});
describe('local decision path', () => {
  it('keeps captured boards and video actions when stream rows are unreadable', async () => {
    const data = detail();
    data.decision_path = { state: 'unreadable' };
    vi.mocked(commands.getHistoryRunDetail).mockResolvedValue(data);
    await render();
    expect(container.textContent).toContain(
      'Couldn’t read this run’s message stream.'
    );
    expect(
      container.querySelector('button[title="Delete video"]')
    ).not.toBeNull();
    expect(container.querySelector('.bpp-board-state')).not.toBeNull();
  });

  it('places an event-only fight on its recorded day and keeps the original sequence in the inspector', async () => {
    const data = detail();
    data.decision_path = {
      state: 'available',
      frames: [
        {
          seq: 11,
          timestamp_ms: 10,
          kind: 'stream_message',
          message_base64: 'AQ==',
          command: null,
          command_args_json: null,
          responds_to_seq: null
        }
      ],
      events: [
        {
          seq: 12,
          timestamp_ms: 20,
          kind: 'pve_combat_recorded',
          body: '{"day":4,"hour":2,"winner":"Player","encounter_id":"monster-1"}'
        }
      ]
    };
    vi.mocked(commands.getHistoryRunDetail).mockResolvedValue(data);
    await render();
    expect(container.textContent).toContain('Day 4');
    await act(async () => vi.dynamicImportSettled());
    expect(container.textContent).toContain('Raw frames');
    const frameDetails = Array.from(container.querySelectorAll('details')).find(
      (d) => d.querySelector('summary')?.textContent === 'Raw frames'
    )!;
    await act(async () => {
      frameDetails.open = true;
      frameDetails.dispatchEvent(new Event('toggle'));
    });
    expect(frameDetails.querySelector('option')?.value).toBe('11');
    expect(
      JSON.parse(frameDetails.querySelector('pre')!.textContent)
    ).toMatchObject({ Seq: 11, Message: { $raw: 1 } });
    const blobs: NodeBlob[] = [];
    const downloads: string[] = [];
    vi.stubGlobal('Blob', NodeBlob);
    vi.stubGlobal('URL', {
      createObjectURL(blob: NodeBlob) {
        blobs.push(blob);
        return 'blob:local';
      },
      revokeObjectURL: vi.fn()
    });
    const anchor = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(function (this: HTMLAnchorElement) {
        downloads.push(this.download);
      });
    try {
      for (const button of frameDetails.querySelectorAll('button'))
        await act(async () => button.click());
      expect(downloads).toEqual(['frame-11.bin', 'run-message-stream.json']);
      expect(new Uint8Array(await blobs[0].arrayBuffer())).toEqual(
        new Uint8Array([1])
      );
      expect(JSON.parse(await blobs[1].text())[0].Seq).toBe(11);
    } finally {
      anchor.mockRestore();
    }
  });
});
