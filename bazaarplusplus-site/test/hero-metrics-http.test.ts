// @vitest-environment jsdom

import { describe, expect, test, vi } from 'vitest';

import {
  HeroMetricsTransportError,
  createHeroMetricsHttpTransport,
} from '../src/features/heroes/hero-metrics-dataset';

describe('createHeroMetricsHttpTransport', () => {
  test('loads unknown JSON from the default base at the v5 hero snapshot path', async () => {
    const fetchImpl = vi.fn(async () => ({
      ok: true,
      json: async () => ({ unknown: true }),
    })) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({ fetchImpl });

    await expect(transport.load('analyzer-v5/heroes/latest.json')).resolves.toEqual({
      unknown: true,
    });

    expect(fetchImpl).toHaveBeenCalledWith(
      'https://bpp-metrics.bazaarplusplus.com/analyzer-v5/heroes/latest.json',
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
  });

  test('uses a configured base URL', async () => {
    const fetchImpl = vi.fn(async () => ({
      ok: true,
      json: async () => ({}),
    })) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({
      metricsBaseUrl: 'https://metrics.example.com/root',
      fetchImpl,
    });

    await transport.load('analyzer-v5/heroes/latest.json');

    expect(fetchImpl).toHaveBeenCalledWith(
      'https://metrics.example.com/root/analyzer-v5/heroes/latest.json',
      expect.any(Object)
    );
  });

  test.each([408, 429, 500, 503])('retries retryable HTTP status %s', async (status) => {
    const fetchImpl = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ recovered: true }),
      }) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({
      fetchImpl,
      requestRetries: 1,
      requestRetryDelayMs: 0,
    });

    await expect(transport.load('analyzer-v5/heroes/latest.json')).resolves.toEqual({
      recovered: true,
    });
    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  test('retries network failures', async () => {
    const fetchImpl = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('network dropped'))
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ recovered: true }),
      }) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({
      fetchImpl,
      requestRetries: 1,
      requestRetryDelayMs: 0,
    });

    await expect(transport.load('analyzer-v5/heroes/latest.json')).resolves.toEqual({
      recovered: true,
    });
    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  test('returns a structured 404 without transport retries', async () => {
    const fetchImpl = vi.fn(async () => ({ ok: false, status: 404 })) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({
      fetchImpl,
      requestRetries: 2,
      requestRetryDelayMs: 0,
    });

    const error = await transport.load('analyzer-v5/heroes/latest.json').catch((reason) => reason);

    expect(error).toBeInstanceOf(HeroMetricsTransportError);
    if (!(error instanceof HeroMetricsTransportError)) {
      throw error;
    }
    expect(error).toMatchObject({ status: 404, kind: 'http' });
    expect(error.message).toMatch(/Failed to fetch .*latest\.json: 404/);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  test('aborts a hung request after the configured timeout with a structured failure', async () => {
    const fetchImpl = vi.fn(
      (_input: RequestInfo | URL, init?: RequestInit) =>
        new Promise<Response>((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('aborted', 'AbortError'))
          );
        })
    ) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({
      fetchImpl,
      requestTimeoutMs: 1,
      requestRetries: 0,
    });

    const error = await transport.load('analyzer-v5/heroes/latest.json').catch((reason) => reason);

    expect(error).toBeInstanceOf(HeroMetricsTransportError);
    if (!(error instanceof HeroMetricsTransportError)) {
      throw error;
    }
    expect(error).toMatchObject({ kind: 'timeout' });
    expect(error.message).toMatch(/Timed out fetching .*latest\.json/);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  test('propagates caller aborts without retrying or reclassifying them', async () => {
    const controller = new AbortController();
    const fetchImpl = vi.fn(
      (_input: RequestInfo | URL, init?: RequestInit) =>
        new Promise<Response>((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => reject(init.signal?.reason));
        })
    ) as unknown as typeof fetch;
    const transport = createHeroMetricsHttpTransport({
      fetchImpl,
      requestRetries: 2,
      requestRetryDelayMs: 0,
    });
    const request = transport.load('analyzer-v5/heroes/latest.json', { signal: controller.signal });

    controller.abort(new DOMException('caller aborted', 'AbortError'));

    await expect(request).rejects.toThrow(/caller aborted/i);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });
});
