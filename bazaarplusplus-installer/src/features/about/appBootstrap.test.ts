import { describe, expect, it, vi } from 'vitest';
import type { AppBootstrap } from '../../types/backend';
import {
  createAppBootstrapMachine,
  type AppBootstrapLoadResult
} from './appBootstrap';

function bootstrap(
  appVersion: string,
  bundledBppVersion: string | null = '4.5.0'
): AppBootstrap {
  return {
    app_version: appVersion,
    bundled_bpp_version: bundledBppVersion,
    links: {
      github: 'https://example.com/github',
      x: 'https://example.com/x',
      bilibili_project: 'https://example.com/bilibili-project',
      bilibili_author: 'https://example.com/bilibili-author',
      bilibili_core_dev: 'https://example.com/bilibili-core-dev',
      xiaohongshu: 'https://example.com/xiaohongshu',
      kofi: 'https://example.com/kofi',
      supporter_list: 'https://example.com/supporters'
    },
    credits: [],
    licenses: []
  };
}

function authoritative(data: AppBootstrap): AppBootstrapLoadResult {
  return { source: 'native', data };
}

describe('About bootstrap resource state', () => {
  it('starts in loading and replaces fallback with authoritative native data', async () => {
    const fallback = bootstrap('4.5.0', null);
    const native = bootstrap('4.5.1');
    const machine = createAppBootstrapMachine({
      fallback,
      load: vi.fn().mockResolvedValue(authoritative(native))
    });

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'initial-loading',
      data: fallback,
      source: 'packaged-fallback',
      unavailableFields: ['bundled_bpp_version']
    });

    await machine.start();

    expect(machine.getSnapshot()).toEqual({
      phase: 'authoritative',
      data: native,
      source: 'native',
      unavailableFields: [],
      problem: null,
      retrying: false
    });
  });

  it('keeps usable packaged data and exposes a semantic failure', async () => {
    const fallback = bootstrap('4.5.0', null);
    const machine = createAppBootstrapMachine({
      fallback,
      load: vi.fn().mockRejectedValue(new Error('IPC unavailable'))
    });

    await machine.start();

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'fallback',
      data: fallback,
      source: 'packaged-fallback',
      unavailableFields: ['bundled_bpp_version'],
      retrying: false,
      problem: {
        code: 'about_bootstrap_failed',
        params: { operation: 'load_bootstrap' },
        diagnostic: 'IPC unavailable'
      }
    });
  });

  it('retries in place and replaces fallback after recovery', async () => {
    const fallback = bootstrap('4.5.0', null);
    const native = bootstrap('4.5.1');
    const load = vi
      .fn<() => Promise<AppBootstrapLoadResult>>()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(authoritative(native));
    const machine = createAppBootstrapMachine({ fallback, load });

    await machine.start();
    const retry = machine.retry();
    expect(machine.getSnapshot()).toMatchObject({
      phase: 'fallback',
      data: fallback,
      retrying: true
    });
    await retry;

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'authoritative',
      data: native,
      source: 'native'
    });
  });

  it('retains fallback and the latest semantic problem after repeated failure', async () => {
    const fallback = bootstrap('4.5.0', null);
    const load = vi
      .fn<() => Promise<AppBootstrapLoadResult>>()
      .mockRejectedValueOnce(new Error('first failure'))
      .mockRejectedValueOnce(new Error('second failure'));
    const machine = createAppBootstrapMachine({ fallback, load });

    await machine.start();
    await machine.retry();

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'fallback',
      data: fallback,
      retrying: false,
      problem: {
        code: 'about_bootstrap_failed',
        diagnostic: 'second failure'
      }
    });
  });

  it('uses a blocking failure only when no usable fallback exists', async () => {
    const machine = createAppBootstrapMachine({
      fallback: null,
      load: vi.fn().mockRejectedValue(new Error('no bootstrap data'))
    });

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'initial-loading',
      data: null
    });
    await machine.start();

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'blocking-failure',
      data: null,
      problem: { code: 'about_bootstrap_failed' },
      retrying: false
    });
  });

  it('marks Browser Preview results as fallback without inventing a failure', async () => {
    const fallback = bootstrap('4.5.0', null);
    const machine = createAppBootstrapMachine({
      fallback,
      load: vi.fn().mockResolvedValue({ source: 'preview', data: fallback })
    });

    await machine.start();

    expect(machine.getSnapshot()).toMatchObject({
      phase: 'fallback',
      data: fallback,
      source: 'packaged-fallback',
      problem: null
    });
  });
});
