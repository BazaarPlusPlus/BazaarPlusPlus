import type { HistoryRunList, HistoryRunRow } from '../../types/backend';
import type { PageRefreshState } from '../shared/pageState';
import type { CommandAdapter } from '../../api/commandAdapter';
import {
  historyProblemFromError,
  type HistoryPageProblem
} from './historyProblems';
import { HISTORY_PAGE_SIZE, parseHistoryPage } from './pagination';

type HistoryListCommands = Pick<
  CommandAdapter,
  'listHistoryRuns' | 'endGameProcess' | 'prepareHistoryThumbnails'
>;

export type EndGameProcessOutcome = 'terminated' | 'already-exited' | 'failed';

/** Whether History Thumbnail URLs can be shown, independently of the rows. */
export type HistoryThumbnails = 'pending' | 'ready' | 'unavailable';

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
  let thumbnails: HistoryThumbnails = 'pending';
  let thumbnailProblem: HistoryPageProblem | null = null;
  let thumbnailAttempt = 0;
  let historyRequest: object | null = null;
  let thumbnailRequest: object | null = null;
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
    const thumbnailsReady = thumbnails === 'ready';
    return {
      state,
      busy,
      endingGameProcess: recovery !== null,
      thumbnails,
      // Only a service that cannot start earns a notice; other failures keep
      // to the card fallback.
      thumbnailsUnavailable:
        thumbnailProblem?.code === 'history_thumbnails_unavailable',
      // Advances with each applied preparation so a card can retry a failed image
      // without remounting cards whose image already loaded.
      thumbnailAttempt,
      thumbnailUrl: (run: HistoryRunRow) =>
        thumbnailsReady ? run.thumbnail_url : null,
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

  async function prepareThumbnails(): Promise<void> {
    if (!active) return;
    const request = {};
    thumbnailRequest = request;
    // The current state stays until preparation settles; clearing it first
    // would remount and refetch every card on each refresh.
    let next: HistoryThumbnails = 'ready';
    let problem: HistoryPageProblem | null = null;
    try {
      await commands.prepareHistoryThumbnails();
    } catch (caught) {
      // Cards use their thumbnail fallback; the list is unaffected.
      next = 'unavailable';
      problem = historyProblemFromError(caught);
    }
    if (!active || thumbnailRequest !== request) return;
    thumbnails = next;
    thumbnailProblem = problem;
    thumbnailAttempt += 1;
    publish();
  }

  async function refresh(): Promise<void> {
    await Promise.all([loadHistory(), prepareThumbnails()]);
  }

  async function selectPage(page: number): Promise<void> {
    const nextPage = parseHistoryPage(String(page));
    if (pageNumber === nextPage) return;
    pageNumber = nextPage;
    historyRequest = null;
    state = { phase: 'initial-loading' };
    publish();
    await refresh();
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
      thumbnails = 'pending';
      thumbnailProblem = null;
      await refresh();
    },
    dispose: () => {
      active = false;
      historyRequest = null;
      thumbnailRequest = null;
      recovery = null;
    },
    selectPage,
    intents: { refresh, endLeftoverGameProcess }
  };
}
