import { describe, expect, it, vi } from 'vitest';
import { idleStreamStatus } from '../../api/previewDefaults';
import { loadHistoryPreviewCapability } from './historyPreview';

describe('History preview capability', () => {
  it('uses status-only discovery and degrades a stopped Stream non-blockingly', async () => {
    const getStatus = vi.fn().mockResolvedValue(idleStreamStatus);

    await expect(
      loadHistoryPreviewCapability(getStatus)
    ).resolves.toMatchObject({
      phase: 'unavailable',
      baseUrl: null,
      problem: {
        code: 'history_preview_unavailable',
        params: { reason: 'stream_stopped' }
      }
    });
    expect(getStatus).toHaveBeenCalledOnce();
  });

  it('turns status failures into thumbnail-only diagnostics', async () => {
    await expect(
      loadHistoryPreviewCapability(() => Promise.reject(new Error('offline')))
    ).resolves.toMatchObject({
      phase: 'unavailable',
      problem: {
        code: 'history_preview_unavailable',
        params: { reason: 'status_failed' },
        diagnostic: 'offline'
      }
    });
  });

  it('returns a base URL only for a running Stream service', async () => {
    const running = {
      ...idleStreamStatus,
      running: true,
      base_url: 'http://127.0.0.1:17654'
    };
    await expect(
      loadHistoryPreviewCapability(() => Promise.resolve(running))
    ).resolves.toEqual({
      phase: 'available',
      baseUrl: 'http://127.0.0.1:17654',
      problem: null
    });
  });
});
