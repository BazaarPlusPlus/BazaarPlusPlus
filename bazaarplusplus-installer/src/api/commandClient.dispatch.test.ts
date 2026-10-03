import { afterEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import type { CommandAdapter } from './commandAdapter';

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
      commandClient.listHistoryRuns(null, null)
    ).rejects.toMatchObject({
      message: 'raw backend failure'
    });
  });

  it.each([
    {
      call: 'listHistoryRuns',
      run: (client: CommandAdapter) => client.listHistoryRuns(50, 100),
      command: 'list_history_runs',
      args: { limit: 50, offset: 100 },
      problem: {
        code: 'history_read_failed',
        params: { operation: 'list_runs' },
        diagnostic: 'database is locked'
      }
    },
    {
      call: 'resetBppData',
      run: (client: CommandAdapter) => client.resetBppData('/game'),
      command: 'reset_bpp_data',
      args: { gamePath: '/game' },
      problem: {
        code: 'install_partial_failure',
        params: {
          operation: 'reset_bpp_data',
          count: '2',
          paths: '/tmp/a\u001f/tmp/b'
        },
        diagnostic: null
      }
    },
    {
      call: 'applyOverlayCropCode',
      run: (client: CommandAdapter) => client.applyOverlayCropCode('bad'),
      command: 'apply_overlay_crop_code',
      args: { code: 'bad' },
      problem: {
        code: 'stream_crop_failed',
        params: { operation: 'apply_code' },
        diagnostic: 'invalid crop payload'
      }
    },
    {
      call: 'executeStorageCleanup',
      run: (client: CommandAdapter) =>
        client.executeStorageCleanup('run_data', 'all'),
      command: 'execute_storage_cleanup',
      args: { scope: 'run_data', preset: 'all' },
      problem: {
        code: 'history_action_failed',
        params: { operation: 'execute_storage_cleanup' },
        diagnostic: 'database is locked'
      }
    }
  ])(
    'preserves the semantic problem a generated $call rejects with',
    async ({ run, command, args, problem }) => {
      vi.stubGlobal('window', { __TAURI_INTERNALS__: {} });
      invokeMock.mockRejectedValueOnce(problem);
      const { commandClient } = await import('./commandClient');

      await expect(run(commandClient)).rejects.toMatchObject({
        name: 'SemanticProblemError',
        message: problem.code,
        problem
      });
      expect(invokeMock).toHaveBeenCalledWith(command, args);
    }
  );
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
