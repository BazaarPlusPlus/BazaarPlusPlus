// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { emptyHistoryRunList } from '../api/previewDefaults';
import { ToastProvider } from '../components/ui/Toast';
import { listHistoryRuns } from '../features/history/historyApi';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
import History from './History';

vi.mock('../features/history/historyApi', () => ({ listHistoryRuns: vi.fn() }));
vi.mock('../features/shared/streamSessionApi', () => ({
  getStreamStatus: async () => ({ running: false })
}));
vi.mock('../features/history/HistoryOverview', () => ({
  HistoryOverview: () => null
}));

const runs = Array.from({ length: 235 }, (_, index) => ({
  run_id: `run-${index + 1}`,
  hero: `Hero ${index + 1}`,
  game_mode: 'Ranked',
  started_at_utc: '2026-09-12T10:00:00Z',
  ended_at_utc: '2026-09-12T11:00:00Z',
  result: 'win',
  victories: 10,
  losses: 0,
  final_day: 10,
  final_player_rank: null,
  final_player_rating: null,
  screenshot_id: null,
  strip_url: null
}));
const loadedPage = (offset = 0, total = 235) => ({
  ...emptyHistoryRunList,
  summary: { ...emptyHistoryRunList.summary, runs: total },
  runs: runs.slice(0, total).slice(offset, offset + 50)
});
let container: HTMLDivElement;
let root: Root;

function Route() {
  const location = useLocation();
  return <History key={location.key} />;
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
  vi.mocked(listHistoryRuns)
    .mockReset()
    .mockImplementation(async (_limit, offset) => loadedPage(offset));
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function render(path: string) {
  await act(async () =>
    root.render(
      <LocaleProvider>
        <ToastProvider>
          <MemoryRouter initialEntries={[path]}>
            <Route />
          </MemoryRouter>
        </ToastProvider>
      </LocaleProvider>
    )
  );
}

async function click(label: string) {
  const button = [...container.querySelectorAll('button')].find(
    (button) => button.textContent === label
  );
  expect(button).toBeDefined();
  await act(async () => button!.click());
}

describe('history pagination', () => {
  it('reaches records beyond 200 and keeps summary totals independent of the page', async () => {
    await render('/history?page=5');
    expect(container.querySelectorAll('.bpp-history-run-card')).toHaveLength(
      35
    );
    expect(container.textContent).toContain('第 201–235 局，共 235 局');
    expect(
      container.querySelector('.bpp-history-run-card')?.getAttribute('href')
    ).toBe('/history/run-201');
    const next = [...container.querySelectorAll('button')].find(
      (button) => button.textContent === '下一页'
    );
    expect(next?.disabled).toBe(true);
    await click('上一页');
    expect(container.querySelectorAll('.bpp-history-run-card')).toHaveLength(
      50
    );
    expect(container.textContent).toContain('第 151–200 局，共 235 局');
  });

  it('shows a page failure without stale rows and retries that same page', async () => {
    await render('/history');
    vi.mocked(listHistoryRuns).mockRejectedValueOnce({
      code: 'history_read_failed',
      params: {},
      diagnostic: null
    });
    await click('下一页');
    expect(container.querySelectorAll('.bpp-history-run-card')).toHaveLength(0);
    expect(container.textContent).toContain('读取本地战绩失败');
    await click('重试');
    expect(container.textContent).toContain('第 51–100 局，共 235 局');
  });

  it('moves to the last available page after the record count shrinks', async () => {
    vi.mocked(listHistoryRuns).mockImplementation(async (_limit, offset) =>
      loadedPage(offset, 52)
    );
    await render('/history?page=5');
    expect(container.querySelectorAll('.bpp-history-run-card')).toHaveLength(2);
    expect(container.textContent).toContain('第 51–52 局，共 52 局');
  });

  it('treats an invalid page parameter as the first page', async () => {
    await render('/history?page=Infinity');
    expect(container.textContent).toContain('第 1–50 局，共 235 局');
  });
});
