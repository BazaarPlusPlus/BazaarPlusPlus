import { afterEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';

import { commands } from '../types/generated/commands';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const invokeMock = vi.mocked(invoke);

describe('generated Tauri command client', () => {
  afterEach(() => invokeMock.mockReset());

  it('serializes the install target from the Rust signature', async () => {
    invokeMock.mockResolvedValueOnce({ marker: true });

    await commands.installMod('/game');

    expect(invokeMock).toHaveBeenCalledWith('install_mod', {
      gamePath: '/game'
    });
  });
});
