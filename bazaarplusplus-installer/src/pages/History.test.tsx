// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { emptyHistoryRunList, idleStreamStatus } from '../api/previewDefaults';
import { commandClient } from '../api/commandClient';
import { ToastProvider } from '../components/ui/Toast';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
import History from './History';

vi.mock('../api/commandClient', () => ({
  commandClient: {
    listHistoryRuns: vi.fn(),
    endGameProcess: vi.fn(),
    prepareHistoryThumbnails: vi.fn(),
    getStreamStatus: vi.fn()
  }
}));
const { listHistoryRuns, endGameProcess, prepareHistoryThumbnails } =
  commandClient;
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
  thumbnail_url: null
}));
const loadedPage = (offset = 0, total = 235) => ({
  ...emptyHistoryRunList,
  summary: { ...emptyHistoryRunList.summary, runs: total },
  runs: runs.slice(0, total).slice(offset, offset + 50)
});
let container: HTMLDivElement;
let root: Root;

function Route({ remountOnNavigation }: { remountOnNavigation: boolean }) {
  const location = useLocation();
  return (
    <>
      <History key={remountOnNavigation ? location.key : undefined} />
      <output data-testid="route">{location.pathname + location.search}</output>
    </>
  );
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
  vi.mocked(listHistoryRuns)
    .mockReset()
    .mockImplementation(async (_limit, offset) => loadedPage(offset ?? 0));
  vi.mocked(endGameProcess).mockReset().mockResolvedValue(true);
  vi.mocked(prepareHistoryThumbnails).mockReset().mockResolvedValue(null);
  vi.mocked(commandClient.getStreamStatus)
    .mockReset()
    .mockResolvedValue(idleStreamStatus);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function render(path: string, remountOnNavigation = false) {
  await act(async () =>
    root.render(
      <StrictMode>
        <LocaleProvider>
          <ToastProvider>
            <MemoryRouter initialEntries={[path]}>
              <Route remountOnNavigation={remountOnNavigation} />
            </MemoryRouter>
          </ToastProvider>
        </LocaleProvider>
      </StrictMode>
    )
  );
}

async function click(label: string) {
  const button = [...container.querySelectorAll('button')].find(
    (candidate) => candidate.textContent === label
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
      loadedPage(offset ?? 0, 52)
    );
    await render('/history?page=5');
    expect(container.querySelectorAll('.bpp-history-run-card')).toHaveLength(2);
    expect(container.textContent).toContain('第 51–52 局，共 52 局');
    expect(container.querySelector('[data-testid="route"]')?.textContent).toBe(
      '/history?page=2'
    );
  });

  it('refreshes the current page when recovery started on an older page', async () => {
    let finishRecovery!: (result: boolean) => void;
    vi.mocked(endGameProcess).mockReturnValueOnce(
      new Promise<boolean>((resolve) => {
        finishRecovery = resolve;
      })
    );
    await render('/history');
    vi.mocked(listHistoryRuns).mockRejectedValueOnce({
      code: 'history_read_blocked_by_game',
      params: {},
      diagnostic: null
    });
    await click('刷新');
    await click('结束游戏进程');
    expect(endGameProcess).toHaveBeenCalledOnce();
    await click('下一页');
    expect(
      container.querySelector('.bpp-history-run-card')?.getAttribute('href')
    ).toBe('/history/run-51');

    await act(async () => finishRecovery(true));

    expect(listHistoryRuns).toHaveBeenLastCalledWith(50, 50);
    expect(
      container.querySelector('.bpp-history-run-card')?.getAttribute('href')
    ).toBe('/history/run-51');
    expect(container.textContent).toContain('第 51–100 局，共 235 局');
  });

  it('treats an invalid page parameter as the first page', async () => {
    await render('/history?page=Infinity');
    expect(container.textContent).toContain('第 1–50 局，共 235 局');
  });

  it('does not refresh an unmounted page when recovery completes after navigation', async () => {
    let finishRecovery!: (result: boolean) => void;
    vi.mocked(endGameProcess).mockReturnValueOnce(
      new Promise<boolean>((resolve) => {
        finishRecovery = resolve;
      })
    );
    // AnimatedOutlet remounts route content on location.key in the production shell.
    await render('/history', true);
    vi.mocked(listHistoryRuns).mockRejectedValueOnce({
      code: 'history_read_blocked_by_game',
      params: {},
      diagnostic: null
    });
    await click('刷新');
    await click('结束游戏进程');
    await click('下一页');
    const callsAfterNavigation = vi.mocked(listHistoryRuns).mock.calls.length;
    expect(
      container.querySelector('.bpp-history-run-card')?.getAttribute('href')
    ).toBe('/history/run-51');

    await act(async () => finishRecovery(true));

    expect(listHistoryRuns).toHaveBeenCalledTimes(callsAfterNavigation);
    expect(listHistoryRuns).toHaveBeenLastCalledWith(50, 50);
    expect(
      container.querySelector('.bpp-history-run-card')?.getAttribute('href')
    ).toBe('/history/run-51');
  });
});

const thumbnailUrl = (source: number, shot = 'shot-1') =>
  `http://127.0.0.1:17654/history/${source}/images/${shot}/strip`;
const thumbnailPage = (source: number, hero: string) => ({
  ...loadedPage(0, 1),
  runs: [
    {
      ...runs[0],
      hero,
      screenshot_id: 'shot-1',
      thumbnail_url: thumbnailUrl(source)
    }
  ]
});

describe('History thumbnails', () => {
  it('loads the list before preparation completes, then displays the prepared image', async () => {
    let ready!: () => void;
    const preparing = new Promise<null>((resolve) => {
      ready = () => resolve(null);
    });
    vi.mocked(listHistoryRuns).mockResolvedValue(thumbnailPage(0, 'Vanessa'));
    vi.mocked(prepareHistoryThumbnails).mockReturnValue(preparing);
    await render('/history');
    expect(container.textContent).toContain('Vanessa');
    expect(container.textContent).toContain('正在加载截图…');
    expect(container.textContent).not.toContain('刷新页面可重试');
    expect(container.querySelector('.bpp-history-run-preview img')).toBeNull();
    await act(async () => ready());
    expect(
      container
        .querySelector('.bpp-history-run-preview img')
        ?.getAttribute('src')
    ).toBe(thumbnailUrl(0));
    expect(container.querySelector('a[href="/stream"]')).toBeNull();
  });

  it.each([
    ['zh', '暂无截图', '缩略图不可用；刷新页面可重试。', '刷新'],
    [
      'en',
      'No screenshot',
      'Thumbnail unavailable; refresh to retry.',
      'Refresh'
    ]
  ] as const)(
    'keeps missing screenshots distinct from failed thumbnails in %s',
    async (locale, empty, failed, refresh) => {
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
      vi.mocked(listHistoryRuns).mockResolvedValue({
        ...loadedPage(0, 2),
        runs: [runs[1], ...thumbnailPage(0, 'Vanessa').runs]
      });
      vi.mocked(prepareHistoryThumbnails).mockRejectedValue(
        new Error('unavailable')
      );
      await render('/history');
      const cards = container.querySelectorAll('.bpp-history-run-card');
      expect(cards[0].textContent).toContain(empty);
      expect(cards[0].textContent).not.toContain(failed);
      expect(cards[1].textContent).toContain(failed);

      vi.mocked(prepareHistoryThumbnails).mockResolvedValue(null);
      await click(refresh);
      expect(cards[0].textContent).toContain(empty);
      expect(cards[0].querySelector('img')).toBeNull();
      expect(cards[1].querySelector('img')).not.toBeNull();
    }
  );

  it.each([
    ['zh', '缩略图不可用；刷新页面可重试。', '刷新'],
    ['en', 'Thumbnail unavailable; refresh to retry.', 'Refresh']
  ] as const)(
    'uses only the card fallback on failure in %s and retries the same image',
    async (locale, fallback, refresh) => {
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
      vi.mocked(listHistoryRuns).mockResolvedValue(thumbnailPage(0, 'Vanessa'));
      vi.mocked(prepareHistoryThumbnails).mockRejectedValue(
        new Error('port occupied')
      );
      await render('/history');
      expect(container.textContent).toContain('Vanessa');
      expect(container.textContent).toContain(fallback);
      expect(container.textContent).not.toMatch(
        /Stream|service|服务|直播|port occupied/
      );
      expect(container.querySelector('a[href="/stream"]')).toBeNull();
      vi.mocked(prepareHistoryThumbnails).mockResolvedValue(null);
      await click(refresh);
      const image = container.querySelector('.bpp-history-run-preview img');
      expect(image).not.toBeNull();
      await act(async () => image!.dispatchEvent(new Event('error')));
      expect(
        container.querySelector('.bpp-history-run-preview img')
      ).toBeNull();
      expect(container.textContent).toContain(fallback);
      await click(refresh);
      expect(
        container
          .querySelector('.bpp-history-run-preview img')
          ?.getAttribute('src')
      ).toBe(thumbnailUrl(0));
    }
  );

  it.each([
    [
      'zh',
      '缩略图服务无法启动，本地端口可能被占用；战绩仍可正常浏览。',
      '缩略图不可用；刷新页面可重试。',
      '刷新'
    ],
    [
      'en',
      "Thumbnails can't load: the local image service couldn't start (its port may be in use). History still works.",
      'Thumbnail unavailable; refresh to retry.',
      'Refresh'
    ]
  ] as const)(
    'tells %s readers when the image service cannot start, above cards that keep their fallback',
    async (locale, notice, fallback, refresh) => {
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
      vi.mocked(listHistoryRuns).mockResolvedValue(thumbnailPage(0, 'Vanessa'));
      vi.mocked(prepareHistoryThumbnails).mockRejectedValue({
        code: 'history_thumbnails_unavailable',
        params: { operation: 'prepare_history_thumbnails' },
        diagnostic: 'Address already in use (os error 48)'
      });
      await render('/history');
      const status = [...container.querySelectorAll('[role="status"]')].find(
        (element) => element.textContent?.includes(notice)
      );
      expect(status).toBeDefined();
      expect(status!.querySelector('button, a')).toBeNull();
      expect(
        status!.compareDocumentPosition(
          container.querySelector('.bpp-history-run-list')!
        ) & Node.DOCUMENT_POSITION_FOLLOWING
      ).toBeTruthy();
      expect(container.textContent).toContain('Vanessa');
      expect(
        container.querySelector('.bpp-history-run-preview-empty')?.textContent
      ).toBe(fallback);

      vi.mocked(prepareHistoryThumbnails).mockResolvedValue(null);
      await click(refresh);
      expect(container.textContent).not.toContain(notice);
      expect(
        container
          .querySelector('.bpp-history-run-preview img')
          ?.getAttribute('src')
      ).toBe(thumbnailUrl(0));
    }
  );

  it('keeps loaded thumbnails through a focus refresh and retries only failed ones', async () => {
    vi.mocked(listHistoryRuns).mockResolvedValue({
      ...loadedPage(0, 2),
      runs: [
        { ...runs[0], screenshot_id: 'shot-1', thumbnail_url: thumbnailUrl(0) },
        {
          ...runs[1],
          screenshot_id: 'shot-2',
          thumbnail_url: thumbnailUrl(0, 'shot-2')
        }
      ]
    });
    await render('/history');
    const [loaded, broken] = container.querySelectorAll(
      '.bpp-history-run-preview img'
    );
    await act(async () => broken.dispatchEvent(new Event('error')));
    expect(
      container.querySelectorAll('.bpp-history-run-preview img')
    ).toHaveLength(1);

    let ready!: () => void;
    vi.mocked(prepareHistoryThumbnails).mockReturnValueOnce(
      new Promise<null>((resolve) => {
        ready = () => resolve(null);
      })
    );
    await act(async () => window.dispatchEvent(new Event('focus')));
    expect(container.querySelector('.bpp-history-run-preview img')).toBe(
      loaded
    );
    await act(async () => ready());
    const images = container.querySelectorAll('.bpp-history-run-preview img');
    expect(images).toHaveLength(2);
    expect(images[0]).toBe(loaded);
    expect(images[1]).not.toBe(broken);
    expect(images[1].getAttribute('src')).toBe(thumbnailUrl(0, 'shot-2'));
  });

  it('refreshes the current list and images when the same window becomes visible or regains focus', async () => {
    vi.mocked(listHistoryRuns).mockResolvedValue(
      thumbnailPage(0, 'Installation A')
    );
    await render('/history');
    const visibility = vi.spyOn(document, 'visibilityState', 'get');
    visibility.mockReturnValue('hidden');
    await act(async () =>
      document.dispatchEvent(new Event('visibilitychange'))
    );
    // B acquired a database while this window was hidden; its list and URLs must refresh together.
    vi.mocked(listHistoryRuns).mockResolvedValue(
      thumbnailPage(1, 'Installation B')
    );
    visibility.mockReturnValue('visible');
    await act(async () =>
      document.dispatchEvent(new Event('visibilitychange'))
    );
    expect(container.textContent).toContain('Installation B');
    expect(container.textContent).not.toContain('Installation A');
    expect(
      container
        .querySelector('.bpp-history-run-preview img')
        ?.getAttribute('src')
    ).toBe(thumbnailUrl(1));
    vi.mocked(prepareHistoryThumbnails).mockRejectedValue(new Error('stopped'));
    await act(async () => window.dispatchEvent(new Event('focus')));
    expect(container.querySelector('.bpp-history-run-preview img')).toBeNull();
    vi.mocked(prepareHistoryThumbnails).mockResolvedValue(null);
    await act(async () => window.dispatchEvent(new Event('focus')));
    expect(
      container.querySelector('.bpp-history-run-preview img')
    ).not.toBeNull();
  });
});
