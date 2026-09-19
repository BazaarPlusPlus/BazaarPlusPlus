// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ModalCoordinatorProvider } from '../../components/ui/ModalCoordinator';
import { LocaleProvider } from '../../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../../i18n/messages';
import type { HistorySummary } from '../../types/backend';
import { executeStorageCleanup, previewStorageCleanup } from './historyApi';
import { HistoryOverview } from './HistoryOverview';

vi.mock('./historyApi', () => ({
  previewStorageCleanup: vi.fn(),
  executeStorageCleanup: vi.fn()
}));

let container: HTMLDivElement;
let root: Root;
const summary: HistorySummary = {
  runs: 7,
  videos: 0,
  win_rate: 0.25,
  last_run_at_utc: null
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
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
  vi.mocked(executeStorageCleanup).mockReset();
  vi.mocked(previewStorageCleanup)
    .mockReset()
    .mockResolvedValue({
      scope: 'screenshots',
      preview: {
        screenshots: 2,
        orphan_files: 0,
        estimated_bytes: 1024,
        skipped_pending_uploads: 0
      }
    });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  vi.unstubAllGlobals();
});

async function render(data = summary) {
  await act(async () =>
    root.render(
      <LocaleProvider>
        <ModalCoordinatorProvider>
          <HistoryOverview summary={data} onCompleted={() => undefined} />
        </ModalCoordinatorProvider>
      </LocaleProvider>
    )
  );
}

async function click(button: HTMLButtonElement) {
  await act(async () => button.click());
}

describe('history overview', () => {
  it('shows summary totals and keeps cleanup controls inaccessible until expanded', async () => {
    await render();
    expect(
      [...container.querySelectorAll('dd .bpp-history-stat-value')].map(
        (e) => e.textContent
      )
    ).toEqual(['7', '0', '25%']);
    expect(container.querySelector('dl')?.textContent).toContain(
      '十胜对局 / 已完成对局'
    );
    const region = container.querySelector('[role="region"]')!;
    const toggle =
      container.querySelector<HTMLButtonElement>('[aria-expanded]')!;
    expect(region.hasAttribute('inert')).toBe(true);
    await click(toggle);
    expect(region.hasAttribute('inert')).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(container.querySelectorAll('select')).toHaveLength(2);
    expect(previewStorageCleanup).not.toHaveBeenCalled();
  });

  it('distinguishes an unavailable ten-win rate from zero percent', async () => {
    await render({ ...summary, win_rate: null });
    expect(container.querySelectorAll('dd')[2]?.textContent).toContain('—');
    expect(container.textContent).toContain('尚无已完成对局');
    await render({ ...summary, win_rate: 0 });
    expect(container.querySelectorAll('dd')[2]?.textContent).toContain('0%');
  });

  it('previews the selected scope and range before any deletion can occur', async () => {
    await render();
    await click(container.querySelector<HTMLButtonElement>('[aria-expanded]')!);
    const select = container.querySelector<HTMLSelectElement>('select')!;
    await act(async () => {
      select.value = 'older_than_7_days';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await click(
      container.querySelector<HTMLButtonElement>(
        'button[aria-label="预览清理对局结算截图"]'
      )!
    );
    const dialog = container.querySelector('dialog')!;
    expect(dialog.textContent).toContain('对局结算截图');
    expect(dialog.textContent).toContain('7 天前');
    expect(dialog.textContent).toContain('2');
    expect(previewStorageCleanup).toHaveBeenCalledWith(
      'screenshots',
      'older_than_7_days'
    );
    expect(executeStorageCleanup).not.toHaveBeenCalled();
    const cancel = [...dialog.querySelectorAll('button')].find(
      (button) => button.textContent === '取消'
    )!;
    await click(cancel);
    expect(container.querySelector('dialog')).toBeNull();
    expect(select.value).toBe('older_than_7_days');
    expect(executeStorageCleanup).not.toHaveBeenCalled();
  });
});
