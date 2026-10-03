// @vitest-environment jsdom

// Shell DOM snapshot anchor: the whole app (GlobalShell, nav rail, header and
// the routed page) rendered per route x state from Browser Preview commands
// seeded with src/test-fixtures/shell/. Each case writes
// src/__shell_snapshots__/<route>.<state>.html; review those diffs in the PR.
// Regenerate: BPP_UPDATE_GOLDENS=1 npx vitest run src/shell.snapshot.test.tsx

import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppRoutes } from './App';
import { commandClient } from './api/commandClient';
import { SemanticProblemError } from './api/problems';
import { LocaleProvider } from './i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from './i18n/messages';
import * as fixtures from './test-fixtures/shell/fixtures';
import type { CommandAdapter } from './api/commandAdapter';
import type { SemanticProblem } from './types/backend';

// Dates render through Intl in the host time zone; CI runs in UTC. The zone
// must be fixed before any formatter module is imported.
const host = vi.hoisted(() => {
  process.env.TZ = 'UTC';
  return { runtime: false, windows: false };
});

vi.mock('./api/runtime', () => ({ hasTauriRuntime: () => host.runtime }));
vi.mock('./features/shared/platform', () => ({
  isWindowsPlatform: () => host.windows
}));
vi.mock('./api/commandClient', async () => {
  const { createPreviewCommands } = await import('./api/previewCommands');
  const { nativeCommands } = await import('./api/nativeCommands');
  return { commandClient: createPreviewCommands(nativeCommands) };
});
vi.mock('@tauri-apps/plugin-updater', () => ({ check: vi.fn() }));
// Every case is a fresh app launch, so the once-per-process startup check is
// always claimable.
vi.mock('./features/about/updater', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./features/about/updater')>()),
  claimStartupCheck: () => true
}));

type Seed = (commands: CommandAdapter) => void;

type Case = {
  route: string;
  page: string;
  state: string;
  seed?: Seed;
  windows?: boolean;
  /** Runs inside the Tauri runtime; only the updater case needs it. */
  runtime?: boolean;
  update?: boolean;
};

const reject = (problem: SemanticProblem) => async () => {
  throw new SemanticProblemError(problem);
};

const cases: Case[] = [
  {
    route: '/',
    page: 'install',
    state: 'game-missing',
    seed: (c) => {
      vi.spyOn(c, 'getInstallState').mockResolvedValue(fixtures.gameMissing);
    }
  },
  {
    route: '/',
    page: 'install',
    state: 'installable',
    seed: (c) => {
      vi.spyOn(c, 'getInstallState').mockResolvedValue(fixtures.installable);
    }
  },
  {
    route: '/',
    page: 'install',
    state: 'installed',
    seed: (c) => {
      vi.spyOn(c, 'getInstallState').mockResolvedValue(fixtures.installed);
    }
  },
  {
    route: '/',
    page: 'install',
    state: 'detection-failed',
    seed: (c) => {
      vi.spyOn(c, 'getInstallState').mockImplementation(
        reject(fixtures.installDetectionFailed)
      );
    }
  },
  {
    route: '/history',
    page: 'history',
    state: 'empty',
    seed: (c) => {
      vi.spyOn(c, 'listHistoryRuns').mockResolvedValue(fixtures.historyEmpty);
    }
  },
  {
    route: '/history',
    page: 'history',
    state: 'multi-page',
    seed: (c) => {
      vi.spyOn(c, 'listHistoryRuns').mockResolvedValue(
        fixtures.historyFirstPage
      );
    }
  },
  {
    route: '/history',
    page: 'history',
    state: 'read-failed',
    seed: (c) => {
      vi.spyOn(c, 'listHistoryRuns').mockImplementation(
        reject(fixtures.historyReadFailed)
      );
    }
  },
  {
    route: '/history/run-1',
    page: 'run-detail',
    state: 'loaded',
    seed: (c) => {
      vi.spyOn(c, 'getHistoryRunDetail').mockResolvedValue(fixtures.runDetail);
    }
  },
  {
    route: '/history/run-1',
    page: 'run-detail',
    state: 'read-failed',
    seed: (c) => {
      vi.spyOn(c, 'getHistoryRunDetail').mockImplementation(
        reject(fixtures.runDetailReadFailed)
      );
    }
  },
  {
    route: '/history/run-404',
    page: 'run-detail',
    state: 'not-found',
    seed: (c) => {
      vi.spyOn(c, 'getHistoryRunDetail').mockResolvedValue(null);
    }
  },
  {
    route: '/stream',
    page: 'stream',
    state: 'windows-idle',
    windows: true,
    seed: (c) => {
      vi.spyOn(c, 'getStreamStatus').mockResolvedValue(fixtures.streamIdle);
      vi.spyOn(c, 'ensureStreamSession').mockResolvedValue(fixtures.streamIdle);
    }
  },
  {
    route: '/stream',
    page: 'stream',
    state: 'windows-running',
    windows: true,
    seed: (c) => {
      vi.spyOn(c, 'getStreamStatus').mockResolvedValue(fixtures.streamRunning);
      vi.spyOn(c, 'ensureStreamSession').mockResolvedValue(
        fixtures.streamRunning
      );
    }
  },
  {
    route: '/stream',
    page: 'stream',
    state: 'windows-error',
    windows: true,
    seed: (c) => {
      vi.spyOn(c, 'getStreamStatus').mockResolvedValue(fixtures.streamError);
      vi.spyOn(c, 'ensureStreamSession').mockResolvedValue(
        fixtures.streamError
      );
    }
  },
  {
    route: '/stream',
    page: 'stream',
    state: 'macos-idle',
    seed: (c) => {
      vi.spyOn(c, 'getStreamStatus').mockResolvedValue(fixtures.streamIdle);
      vi.spyOn(c, 'ensureStreamSession').mockResolvedValue(fixtures.streamIdle);
    }
  },
  { route: '/about', page: 'about', state: 'current' },
  {
    route: '/about',
    page: 'about',
    state: 'loading',
    seed: (c) => {
      vi.spyOn(c, 'getAppBootstrap').mockReturnValue(new Promise(() => {}));
    }
  },
  {
    route: '/about',
    page: 'about',
    state: 'bootstrap-failed',
    seed: (c) => {
      vi.spyOn(c, 'getAppBootstrap').mockRejectedValue(
        new Error('fixture: IPC unavailable')
      );
    }
  },
  {
    route: '/about',
    page: 'about',
    state: 'update-available',
    runtime: true,
    update: true
  }
];

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    }
  );
  localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
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
  host.runtime = false;
  host.windows = false;
  localStorage.clear();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/** Each pass waits one animation frame and then a macrotask, so loads, their
 *  follow-up reads and frame-deferred UI (a toast's `is-present`) have all
 *  settled however fast the host runs. */
async function settle() {
  for (let pass = 0; pass < 5; pass += 1) {
    await act(async () => {
      await new Promise((resolve) =>
        requestAnimationFrame(() => setTimeout(resolve, 0))
      );
    });
  }
}

/** Drops only React's generated ids, the compiled-in package version (the
 *  header stamp and the packaged About fallback) and lucide's glyph paths,
 *  keeping each icon's tag and name, so a version or dependency bump does not
 *  rewrite every snapshot. Copy and ARIA attributes stay. One tag per line
 *  keeps the snapshot diff reviewable. */
function normalize(html: string): string {
  return html
    .replace(/«r[0-9a-z]+»|_r_[0-9a-z]+_|:r[0-9a-z]+:/g, '«id»')
    .replace(
      /(<svg\b[^>]*\bclass="lucide [^"]*"[^>]*>)[\s\S]*?<\/svg>/g,
      '$1</svg>'
    )
    .replaceAll(__FRONTEND_VERSION__, '«version»')
    .replaceAll('><', '>\n<');
}

describe('shell DOM snapshots', () => {
  it.each(cases)(
    '$page $state',
    async ({ route, page, state, seed, windows, runtime, update }) => {
      host.windows = windows ?? false;
      host.runtime = runtime ?? false;
      vi.spyOn(commandClient, 'getAppBootstrap').mockResolvedValue(
        fixtures.bootstrap
      );
      seed?.(commandClient);
      if (update) {
        const { check } = await import('@tauri-apps/plugin-updater');
        vi.mocked(check).mockResolvedValue({
          ...fixtures.availableUpdate,
          downloadAndInstall: vi.fn(),
          close: vi.fn()
        } as unknown as Awaited<ReturnType<typeof check>>);
      }

      await act(async () =>
        root.render(
          <StrictMode>
            <LocaleProvider>
              <MemoryRouter initialEntries={[route]}>
                <AppRoutes />
              </MemoryRouter>
            </LocaleProvider>
          </StrictMode>
        )
      );
      await settle();

      await expect(normalize(container.innerHTML)).toMatchFileSnapshot(
        `./__shell_snapshots__/${page}.${state}.html`
      );
    }
  );
});
