import { afterEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const invokeMock = vi.mocked(invoke);

afterEach(() => {
  vi.unstubAllGlobals();
  vi.resetModules();
  invokeMock.mockReset();
});

describe('native command adapter', () => {
  it('selects the generated native client once when the runtime is present', async () => {
    vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
    invokeMock.mockResolvedValueOnce({ marker: true });
    const { commandClient } = await import('./commandClient');

    await expect(commandClient.getInstallState('/game')).resolves.toEqual({
      marker: true
    });
    expect(invokeMock).toHaveBeenCalledWith('get_install_state', {
      gamePath: '/game'
    });
  });

  it('normalizes string rejections from generated commands', async () => {
    vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
    invokeMock.mockRejectedValueOnce('raw backend failure');
    const { commandClient } = await import('./commandClient');

    await expect(
      commandClient.deleteRunVideos('run', null)
    ).rejects.toMatchObject({
      message: 'raw backend failure'
    });
  });

  it('preserves semantic problem data from a generated command rejection', async () => {
    vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
    const problem = {
      code: 'history_read_failed',
      params: { operation: 'list_runs' },
      diagnostic: 'database is locked'
    };
    invokeMock.mockRejectedValueOnce(problem);
    const { commandClient } = await import('./commandClient');

    await expect(commandClient.listHistoryRuns(50)).rejects.toMatchObject({
      name: 'SemanticProblemError',
      message: 'history_read_failed',
      problem
    });
    expect(invokeMock).toHaveBeenCalledWith('list_history_runs', { limit: 50 });
  });

  it('preserves semantic install recovery parameters', async () => {
    vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
    const problem = {
      code: 'install_partial_failure',
      params: {
        operation: 'reset_bpp_data',
        count: '2',
        paths: '/tmp/a\u001f/tmp/b'
      },
      diagnostic: null
    };
    invokeMock.mockRejectedValueOnce(problem);
    const { commandClient } = await import('./commandClient');

    await expect(commandClient.resetBppData('/game')).rejects.toMatchObject({
      name: 'SemanticProblemError',
      message: 'install_partial_failure',
      problem
    });
  });

  it('preserves Stream capability and operation failures', async () => {
    vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
    const problem = {
      code: 'stream_crop_failed',
      params: { operation: 'apply_code' },
      diagnostic: 'invalid crop payload'
    };
    invokeMock.mockRejectedValueOnce(problem);
    const { commandClient } = await import('./commandClient');

    await expect(
      commandClient.applyOverlayCropCode('bad')
    ).rejects.toMatchObject({
      name: 'SemanticProblemError',
      message: 'stream_crop_failed',
      problem
    });
  });

  it('preserves cleanup operation failures for localized retry', async () => {
    vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
    const problem = {
      code: 'history_action_failed',
      params: { operation: 'execute_storage_cleanup' },
      diagnostic: 'database is locked'
    };
    invokeMock.mockRejectedValueOnce(problem);
    const { commandClient } = await import('./commandClient');

    await expect(
      commandClient.executeStorageCleanup('run_data', 'all')
    ).rejects.toMatchObject({
      name: 'SemanticProblemError',
      message: 'history_action_failed',
      problem
    });
    expect(invokeMock).toHaveBeenCalledWith('execute_storage_cleanup', {
      scope: 'run_data',
      preset: 'all'
    });
  });
});

describe('normalizeBackendError sentinel contract', () => {
  it('keeps string errors verbatim and passes Error instances through', async () => {
    const { normalizeBackendError } = await import('./nativeCommands');
    const error = new Error('boom');

    expect(
      normalizeBackendError('bpp_data_reset_blocked_by_game').message
    ).toBe('bpp_data_reset_blocked_by_game');
    expect(normalizeBackendError(error)).toBe(error);
  });

  it('wraps unknown shapes', async () => {
    const { normalizeBackendError } = await import('./nativeCommands');
    expect(normalizeBackendError({ weird: true }).message).toBe(
      'Backend command failed.'
    );
    expect(
      normalizeBackendError({
        code: 'toString',
        params: {},
        diagnostic: null
      }).message
    ).toBe('Backend command failed.');
  });
});
