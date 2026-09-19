import { useCallback, useEffect, useState, useSyncExternalStore } from 'react';
import { useSearchParams } from 'react-router-dom';
import { getStreamStatus } from '../shared/streamSessionApi';
import { endGameProcess, listHistoryRuns } from './historyApi';
import { createHistoryListWorkflow } from './historyListWorkflow';
import { parseHistoryPage } from './pagination';

const commands = { listHistoryRuns, endGameProcess, getStreamStatus };

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
    return () => workflow.dispose();
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
