import { useCallback, useEffect, useState, useSyncExternalStore } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  endGameProcess,
  listHistoryRuns,
  prepareHistoryThumbnails
} from './historyApi';
import { createHistoryListWorkflow } from './historyListWorkflow';
import { observeHistoryWindowResume } from './historyWindowResume';
import { parseHistoryPage } from './pagination';

const commands = { listHistoryRuns, endGameProcess, prepareHistoryThumbnails };

export function useHistoryPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const routePage = parseHistoryPage(searchParams.get('page'));
  const [workflow] = useState(() =>
    createHistoryListWorkflow(commands, {
      initialPage: routePage,
      replacePage: (page) =>
        setSearchParams(page === 1 ? {} : { page: String(page) }, {
          replace: true
        })
    })
  );
  const snapshot = useSyncExternalStore(
    workflow.subscribe,
    workflow.getSnapshot,
    workflow.getSnapshot
  );

  useEffect(() => {
    void workflow.start();
    const stopObserving = observeHistoryWindowResume(() => {
      void workflow.intents.refresh();
    });
    return () => {
      stopObserving();
      workflow.dispose();
    };
  }, [workflow]);
  useEffect(() => {
    void workflow.selectPage(routePage);
  }, [workflow, routePage]);
  const refresh = useCallback(() => {
    void workflow.intents.refresh();
  }, [workflow]);

  return {
    ...snapshot,
    ...workflow.intents,
    refresh,
    goToPage: (page: number) =>
      setSearchParams(page === 1 ? {} : { page: String(page) })
  };
}
