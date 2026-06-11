import { useCallback, useEffect, useMemo, useState } from 'react';
import type { HistoryRunList, HistoryRunRow } from '../../types/backend';
import { toErrorMessage } from '../shared/errors';
import { ensureStreamSession } from '../shared/streamSessionApi';
import { optionalStripPreviewUrl } from './stripPreview';
import { emptyHistoryRunList, listHistoryRuns } from './historyApi';

export function useHistoryPage() {
  const [payload, setPayload] = useState<HistoryRunList>(emptyHistoryRunList);
  const [baseUrl, setBaseUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [session, list] = await Promise.all([
        ensureStreamSession(),
        listHistoryRuns()
      ]);
      setBaseUrl(session.base_url);
      setPayload(list);
    } catch (caught) {
      setError(toErrorMessage(caught));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const previewUrl = useCallback(
    (run: HistoryRunRow) => optionalStripPreviewUrl(baseUrl, run.strip_url),
    [baseUrl]
  );

  const summary = useMemo(
    () => ({
      runs: String(payload.summary.runs),
      videos: String(payload.summary.videos),
      winRate:
        payload.summary.win_rate === null
          ? '-'
          : `${Math.round(payload.summary.win_rate * 100)}%`
    }),
    [payload.summary]
  );

  return {
    payload,
    summary,
    loading,
    error,
    previewUrl,
    refresh
  };
}
