import type { HistoryRunList, HistoryRunRow } from '../../types/backend';
import type { PageRefreshState } from '../shared/pageState';
import type { getStreamStatus } from '../shared/streamSessionApi';
import type { endGameProcess, listHistoryRuns } from './historyApi';
import {
  loadHistoryPreviewCapability,
  type HistoryPreviewState
} from './historyPreview';
import {
  historyProblemFromError,
  type HistoryPageProblem
} from './historyProblems';
import { HISTORY_PAGE_SIZE, parseHistoryPage } from './pagination';
import { optionalStripPreviewUrl } from './stripPreview';

type HistoryListCommands = {
  listHistoryRuns: typeof listHistoryRuns;
  endGameProcess: typeof endGameProcess;
  getStreamStatus: typeof getStreamStatus;
};

export type EndGameProcessOutcome = 'terminated' | 'already-exited' | 'failed';

type HistoryListState =
  | { phase: 'initial-loading' }
  | { phase: 'blocking-failure'; problem: HistoryPageProblem }
  | {
      phase: 'ready-empty' | 'ready-content';
      data: HistoryRunList;
      refresh: PageRefreshState<HistoryPageProblem>;
    };

export function createHistoryListWorkflow(
  commands: HistoryListCommands,
  options: { initialPage: number; replacePage: (page: number) => void }
) {
  const listeners = new Set<() => void>();
  let active = false;
  let pageNumber = parseHistoryPage(String(options.initialPage));
  let state: HistoryListState = { phase: 'initial-loading' };
  let preview: HistoryPreviewState = {
    phase: 'checking',
    baseUrl: null,
    problem: null
  };
  let historyRequest: object | null = null;
  let previewRequest: object | null = null;
  let recovery: Promise<EndGameProcessOutcome> | null = null;
  let snapshot = deriveSnapshot();

  function deriveSnapshot() {
    const data = 'data' in state ? state.data : null;
    const busy =
      state.phase === 'initial-loading' ||
      ('refresh' in state && state.refresh.phase === 'refreshing');
    const pageCount = data
      ? Math.max(1, Math.ceil(data.summary.runs / HISTORY_PAGE_SIZE))
      : pageNumber;
    const baseUrl = preview.baseUrl;
    return {
      state,
      busy,
      endingGameProcess: recovery !== null,
      previewProblem: preview.problem,
      previewUrl: (run: HistoryRunRow) =>
        optionalStripPreviewUrl(baseUrl, run.strip_url),
      pagination: {
        page: pageNumber,
        pageCount,
        start: data?.runs.length ? (pageNumber - 1) * HISTORY_PAGE_SIZE + 1 : 0,
        end: data?.runs.length
          ? (pageNumber - 1) * HISTORY_PAGE_SIZE + data.runs.length
          : 0,
        total: data?.summary.runs ?? null,
        previousDisabled: busy || pageNumber <= 1,
        nextDisabled: busy || pageNumber >= pageCount
      }
    };
  }

  function publish() {
    snapshot = deriveSnapshot();
    for (const listener of listeners) listener();
  }

  async function loadHistory(): Promise<void> {
    if (!active) return;
    const request = {};
    historyRequest = request;
    state =
      'data' in state
        ? { ...state, refresh: { phase: 'refreshing' } }
        : { phase: 'initial-loading' };
    publish();
    try {
      const data = await commands.listHistoryRuns(
        HISTORY_PAGE_SIZE,
        (pageNumber - 1) * HISTORY_PAGE_SIZE
      );
      if (!active || historyRequest !== request) return;
      const lastPage = Math.max(
        1,
        Math.ceil(data.summary.runs / HISTORY_PAGE_SIZE)
      );
      if (pageNumber > lastPage) {
        pageNumber = lastPage;
        state = { phase: 'initial-loading' };
        options.replacePage(lastPage);
        await loadHistory();
        return;
      }
      state = {
        phase: data.runs.length ? 'ready-content' : 'ready-empty',
        data,
        refresh: { phase: 'idle' }
      };
    } catch (caught) {
      if (!active || historyRequest !== request) return;
      const problem = historyProblemFromError(caught);
      state =
        'data' in state
          ? { ...state, refresh: { phase: 'failed', problem } }
          : { phase: 'blocking-failure', problem };
    }
    publish();
  }

  async function refreshPreview(): Promise<void> {
    if (!active) return;
    const request = {};
    previewRequest = request;
    preview = { phase: 'checking', baseUrl: null, problem: null };
    publish();
    const result = await loadHistoryPreviewCapability(commands.getStreamStatus);
    if (!active || previewRequest !== request) return;
    preview = result;
    publish();
  }

  async function refresh(): Promise<void> {
    await Promise.all([loadHistory(), refreshPreview()]);
  }

  async function selectPage(page: number): Promise<void> {
    const nextPage = parseHistoryPage(String(page));
    if (pageNumber === nextPage) return;
    pageNumber = nextPage;
    historyRequest = null;
    state = { phase: 'initial-loading' };
    publish();
    await loadHistory();
  }

  function endLeftoverGameProcess(): Promise<EndGameProcessOutcome> {
    if (!active) return Promise.resolve('failed');
    if (recovery) return recovery;
    // Establish ownership before invoking a command that may throw synchronously.
    const operation = Promise.resolve().then<EndGameProcessOutcome>(
      async () => {
        if (!active || recovery !== operation) return 'failed';
        try {
          return (await commands.endGameProcess())
            ? 'terminated'
            : 'already-exited';
        } catch {
          return 'failed';
        } finally {
          if (active && recovery === operation) {
            recovery = null;
            // Every outcome reloads the current page, never the page that started recovery.
            void refresh();
          }
        }
      }
    );
    recovery = operation;
    publish();
    return operation;
  }

  return {
    getSnapshot: () => snapshot,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    start: async () => {
      if (active) return;
      active = true;
      state = { phase: 'initial-loading' };
      await refresh();
    },
    dispose: () => {
      active = false;
      historyRequest = null;
      previewRequest = null;
      recovery = null;
    },
    selectPage,
    intents: { refresh, endLeftoverGameProcess }
  };
}
