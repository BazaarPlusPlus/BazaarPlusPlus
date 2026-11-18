import { describe, expect, it } from 'vite-plus/test';
import { commandClient } from './commandClient';
import { defaultCropSettings } from './previewDefaults';

describe('browser-preview command adapter', () => {
  it('reproduces input-dependent overlay fallbacks', async () => {
    expect(await commandClient.applyOverlayCropCode('x')).toEqual({
      ...defaultCropSettings,
      code: 'x'
    });
    expect(await commandClient.saveOverlayDisplayMode('hero')).toEqual({
      ...defaultCropSettings,
      display_mode: 'hero'
    });
  });

  it('passes native-only preview commands through to the normalized client', async () => {
    await expect(commandClient.installMod('x')).rejects.toBeInstanceOf(Error);
    await expect(
      commandClient.deleteBattleVideo('b', 'v')
    ).rejects.toBeInstanceOf(Error);
  });
});
