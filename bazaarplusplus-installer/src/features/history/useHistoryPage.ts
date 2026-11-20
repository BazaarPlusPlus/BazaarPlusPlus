import { useCallback, useEffect, useState, useSyncExternalStore } from 'react';
import { useSearchParams } from 'react-router-dom';
import { commandClient } from '../../api/commandClient';
import type { HistoryRunFilter } from '../../types/backend';
import { createHistoryListWorkflow } from './historyListWorkflow';
import { observeWindowResume } from '../shared/windowActivity';
import { historySearchParams, parseHistoryQuery } from './pagination';

export function useHistoryPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const route = parseHistoryQuery(searchParams);
  const [workflow] = useState(() =>
    createHistoryListWorkflow(commandClient, {
      initialPage: route.page,
      initialFilter: route.filter,
      replacePage: (page) =>
        setSearchParams(
          (current) =>
            historySearchParams({ ...parseHistoryQuery(current), page }),
          { replace: true }
        )
    })
  );
  const snapshot = useSyncExternalStore(
    workflow.subscribe,
    workflow.getSnapshot,
    workflow.getSnapshot
  );

  useEffect(() => {
    void workflow.start();
    const stopObserving = observeWindowResume(() => {
      void workflow.intents.refresh();
    });
    return () => {
      stopObserving();
      workflow.dispose();
    };
  }, [workflow]);
  const { page: routePage, filter: routeFilter } = route;
  const { hero, period, result, key_card } = routeFilter;
  useEffect(() => {
    void workflow.selectPage(routePage, { hero, period, result, key_card });
  }, [workflow, routePage, hero, period, result, key_card]);
  const refresh = useCallback(() => {
    void workflow.intents.refresh();
  }, [workflow]);

  return {
    ...snapshot,
    ...workflow.intents,
    refresh,
    search: searchParams.toString(),
    goToPage: (page: number) =>
      setSearchParams(historySearchParams({ page, filter: routeFilter })),
    setFilter: (filter: HistoryRunFilter) =>
      setSearchParams(historySearchParams({ page: 1, filter }))
  };
}
