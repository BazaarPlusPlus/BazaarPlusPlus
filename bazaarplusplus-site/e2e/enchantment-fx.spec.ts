import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve as resolvePath } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { type FxMaterial } from '@bazaarplusplus/ui/game';
import {
  mockAccountApi,
  runDetailHandlers,
  SAMPLE_RUN_ID
} from './account-api';

const directory = resolvePath('e2e/fixtures/enchantment-fx');
const sampleMaterialFile = 'fx-edf136207698e6e0fd7a.json';
const sampleMaterial = JSON.parse(
  readFileSync(resolvePath(directory, sampleMaterialFile), 'utf8')
) as FxMaterial;
const tambourine = 'd6c55b70-fcc7-454c-af9c-06b99d2d01e3';

for (const webgl of [true, false]) {
  test(`Run shows the enchanted Tambourine with WebGL2 ${webgl ? 'enabled' : 'disabled'}`, async ({
    page
  }) => {
    if (!webgl)
      await page.addInitScript(() => {
        const getContext = HTMLCanvasElement.prototype.getContext;
        HTMLCanvasElement.prototype.getContext = function (
          this: HTMLCanvasElement,
          ...args: Parameters<typeof getContext>
        ) {
          return args[0] === 'webgl2'
            ? null
            : Reflect.apply(getContext, this, args);
        } as typeof getContext;
      });
    await mockAccountApi(page, runDetailHandlers());
    await page.route('**/card-art/**', async (route) => {
      const name =
        new URL(route.request().url()).pathname.split('/').pop() ?? '';
      if (name === 'index.json')
        return route.fulfill({
          json: {
            version: 2,
            cards: { [tambourine]: sampleMaterial.textures['_MainTex'].file },
            fx: { [tambourine]: sampleMaterialFile }
          }
        });
      return route.fulfill({ path: resolvePath(directory, name) });
    });
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto('/records/' + SAMPLE_RUN_ID + '?lang=en');
    const tile = page.getByLabel(/Tambourine.*Shiny/).first();
    await tile.scrollIntoViewIfNeeded();
    await expect(tile.getByRole('img')).toBeVisible();
    const canvas = tile.locator('canvas');
    await expect(canvas).toHaveCount(1);
    if (webgl) {
      await expect
        .poll(() => canvas.evaluate((c) => Number(c.getAttribute('width'))))
        .toBeGreaterThan(0);
      await tile.click();
      await expect
        .poll(() =>
          page
            .getByRole('dialog')
            .locator('canvas')
            .evaluate((c) => Number(c.getAttribute('width')))
        )
        .toBeGreaterThan(0);
    } else {
      await expect(canvas).toHaveAttribute('width', '0');
      await expect(tile).toContainText('Shiny');
    }
  });
}

async function openRendererFixture(page: Page) {
  const module = execFileSync(
    'bun',
    ['build', '../packages/ui/src/game/enchantment-fx/renderer.ts'],
    { encoding: 'utf8' }
  );
  await page.route('**/fx-check/**', async (route) => {
    const name = new URL(route.request().url()).pathname.split('/').pop() ?? '';
    if (!name)
      return route.fulfill({
        contentType: 'text/html',
        body: '<canvas width="0" height="0" style="width:400px;height:400px"></canvas>'
      });
    if (name === 'runtime.js')
      return route.fulfill({
        body: module,
        contentType: 'application/javascript'
      });
    return route.fulfill({ path: resolvePath(directory, name) });
  });
  await page.goto('/fx-check/');
}

test('a visible face recovers after context loss and releases its backing store on close', async ({
  page
}) => {
  await openRendererFixture(page);
  const result = await page.evaluate(async (descriptor) => {
    const path = './runtime.js';
    const { observeFx } = (await import(
      /* @vite-ignore */ path
    )) as typeof import('../../packages/ui/src/game/enchantment-fx/renderer');
    const face = document.querySelector('canvas');
    if (!face) throw new Error('Missing face');
    let buffer: HTMLCanvasElement | undefined;
    // Capture the detached renderer canvas to exercise the real context-loss events.
    const original = document.createElement.bind(document);
    document.createElement = (tag: string) => {
      const element = original(tag);
      if (element instanceof HTMLCanvasElement) buffer = element;
      return element;
    };
    const frames = async () => {
      let count = 0;
      while (!face.width && count++ < 120) {
        // eslint-disable-next-line no-await-in-loop
        await new Promise(requestAnimationFrame);
      }
      return face.width;
    };
    const source = {
      material: async () => descriptor,
      texture: async (file: string) => (await fetch(file)).blob()
    };
    const stop = observeFx(face, source, 'Shiny', true);
    const first = await frames();
    document.createElement = original;
    const context = buffer?.getContext('webgl2');
    const extension = context?.getExtension('WEBGL_lose_context');
    if (!extension || !buffer)
      throw new Error('Missing WebGL2 context-loss extension');
    const lost = new Promise((resolve) =>
      buffer?.addEventListener('webglcontextlost', resolve, { once: true })
    );
    extension.loseContext();
    await lost;
    const fallback = face.width;
    const restored = new Promise((resolve) =>
      buffer?.addEventListener('webglcontextrestored', resolve, { once: true })
    );
    // Chromium enables restoration after the lost event has finished dispatching.
    await new Promise(requestAnimationFrame);
    extension.restoreContext();
    await restored;
    const second = await frames();
    stop?.();
    return { first, fallback, second, closed: face.width };
  }, sampleMaterial);
  expect(result).toEqual({ first: 400, fallback: 0, second: 400, closed: 0 });
});
