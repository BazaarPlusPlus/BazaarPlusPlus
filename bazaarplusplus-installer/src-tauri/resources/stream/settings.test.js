// @vitest-environment jsdom

import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { describe, expect, test, vi } from 'vitest';

const DEFAULT_CROP = {
  left: 0.342,
  top: 0.313,
  width: 0.58,
  height: 0.22
};

describe('overlay calibration page', () => {
  test('previews, reverts, and saves crop changes', async () => {
    const html = await readFile(
      resolve(process.cwd(), 'src-tauri/resources/stream/settings.html'),
      'utf8'
    );
    document.open();
    document.write(html);
    document.close();
    window.history.replaceState({}, '', '/settings?lang=en');

    const requests = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input, init = {}) => {
        const url = String(input);
        requests.push({ url, init });

        if (
          url.includes('/api/overlay/crop-config') &&
          init.method === 'POST'
        ) {
          return okJson(JSON.parse(init.body));
        }
        if (url.includes('/api/overlay/crop-config')) {
          return okJson({ crop: DEFAULT_CROP });
        }
        if (url.includes('/api/stream/records/latest')) {
          return okJson({
            id: 'record-1',
            title: 'Mak',
            wins: 7,
            battle_count: 12
          });
        }

        throw new Error(`Unexpected request: ${url}`);
      })
    );

    await import('./settings.js');

    await vi.waitFor(() => {
      expect(document.getElementById('page-status-text').textContent).toBe(
        'Sample ready. Adjust the controls and check the overlay output before saving.'
      );
    });

    const left = document.getElementById('crop-left');
    const leftOutput = document.getElementById('crop-left-value');
    const stripPreview = document.getElementById('strip-preview-image');
    const save = document.getElementById('save-button');
    const reset = document.getElementById('reset-button');

    expect(leftOutput.textContent).toBe('34.2%');
    expect(stripPreview.getAttribute('src')).toContain('left=0.342');
    expect(save.disabled).toBe(true);
    expect(reset.disabled).toBe(true);

    left.value = '0.350';
    left.dispatchEvent(new Event('input', { bubbles: true }));

    expect(leftOutput.textContent).toBe('35.0%');
    expect(document.getElementById('page-status-text').textContent).toBe(
      'You have unsaved crop changes.'
    );
    expect(save.disabled).toBe(false);
    expect(reset.disabled).toBe(false);

    await vi.waitFor(() => {
      expect(stripPreview.getAttribute('src')).toContain('left=0.350');
    });

    reset.click();

    expect(leftOutput.textContent).toBe('34.2%');
    expect(document.getElementById('page-status-text').textContent).toBe(
      'Unsaved changes reverted.'
    );
    expect(save.disabled).toBe(true);
    expect(reset.disabled).toBe(true);

    left.value = '0.350';
    left.dispatchEvent(new Event('input', { bubbles: true }));
    save.click();

    await vi.waitFor(() => {
      expect(document.getElementById('page-status-text').textContent).toBe(
        'Crop saved. The overlay will use it on the next refresh.'
      );
    });

    const saveRequest = requests.find(({ init }) => init.method === 'POST');
    expect(JSON.parse(saveRequest.init.body)).toEqual({
      crop: { ...DEFAULT_CROP, left: 0.35 }
    });
    expect(save.disabled).toBe(true);
    expect(reset.disabled).toBe(true);
  });
});

function okJson(payload) {
  return {
    ok: true,
    json: async () => payload,
    text: async () => JSON.stringify(payload)
  };
}
