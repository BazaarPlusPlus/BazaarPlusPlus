import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve as resolvePath } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import type { FxMaterial } from '@bpp/ui';
import {
  mockAccountApi,
  runDetailHandlers,
  SAMPLE_RUN_ID
} from './account-api';
import fixture from './fixtures/enchantment-fx/cases.json';

const directory = resolvePath('e2e/fixtures/enchantment-fx');
const sampleMaterial = JSON.parse(
  readFileSync(resolvePath(directory, fixture.material), 'utf8')
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
            fx: { [tambourine]: fixture.material }
          }
        });
      return route.fulfill({ path: resolvePath(directory, name) });
    });
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto('/records/' + SAMPLE_RUN_ID + '?lang=en');
    const tile = page.getByTitle(/Tambourine.*Shiny/).first();
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
    ['build', '../design/ui/game/enchantment-fx/renderer.ts'],
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

for (const first of fixture.cases.filter((sample) => sample.time === 1)) {
  const name = first.image.replace(/1\.png$/, '');
  const samples = fixture.cases.filter(
    (sample) =>
      sample.effect === first.effect && sample.material === first.material
  );
  test(`${name} matches its Metal effect crop at both phases`, async ({
    page
  }) => {
    await openRendererFixture(page);
    const errors = await page.evaluate(
      async ({ cases, material }) => {
        const path = './runtime.js';
        const { observeFx } = (await import(
          /* @vite-ignore */ path
        )) as typeof import('../../design/ui/game/enchantment-fx/renderer');
        const canvas = document.querySelector('canvas');
        if (!canvas) throw new Error('Missing test face');
        const native = window.requestAnimationFrame;
        let seconds = 1;
        window.requestAnimationFrame = (callback) =>
          native(() => callback(seconds * 1000));
        const results = [];
        // Every phase reuses the same face, so it must finish before the next clock change.
        /* eslint-disable no-await-in-loop */
        for (const sample of cases) {
          seconds = sample.time;
          const effect = sample.effect as keyof FxMaterial['keywords'];
          const descriptor: FxMaterial = sample.material
            ? await (await fetch(sample.material)).json()
            : material;
          const stop = observeFx(
            canvas,
            {
              material: async () => descriptor,
              texture: async (file) => (await fetch(file)).blob(),
              tiny: effect === 'Tiny'
            },
            effect,
            false
          );
          for (let i = 0; i < 240 && !canvas.width; i++)
            await new Promise(native);
          if (!canvas.width) throw new Error('No frame for ' + effect);
          const bitmap = await createImageBitmap(
            await (await fetch(sample.image)).blob()
          );
          const crop = document.createElement('canvas');
          crop.width = bitmap.width;
          crop.height = bitmap.height;
          const ctx = crop.getContext('2d');
          if (!ctx) throw new Error('No 2D context');
          ctx.drawImage(bitmap, 0, 0);
          const expected = ctx.getImageData(0, 0, crop.width, crop.height).data;
          ctx.drawImage(
            canvas,
            sample.x,
            sample.y,
            crop.width,
            crop.height,
            0,
            0,
            crop.width,
            crop.height
          );
          const actual = ctx.getImageData(0, 0, crop.width, crop.height).data;
          const art = descriptor.textures['_MainTex'].file;
          if (!art) throw new Error('Missing control illustration');
          const plain = await createImageBitmap(
            await (await fetch(art)).blob()
          );
          ctx.drawImage(plain, -sample.x, -sample.y, 400, 400);
          const control = ctx.getImageData(0, 0, crop.width, crop.height).data;
          plain.close();
          let plainSum = 0;
          let sum = 0,
            outliers = 0;
          for (let i = 0; i < expected.length; i += 4) {
            const differences = [0, 1, 2].map((c) =>
              Math.abs(actual[i + c] - expected[i + c])
            );
            for (const c of [0, 1, 2])
              plainSum += Math.abs(control[i + c] - expected[i + c]);
            sum += differences[0] + differences[1] + differences[2];
            if (Math.max(...differences) > 12) outliers++;
          }
          results.push({
            image: sample.image,
            plainMean: plainSum / (3 * crop.width * crop.height),
            mean: sum / (3 * crop.width * crop.height),
            outliers: outliers / (crop.width * crop.height)
          });
          bitmap.close();
          stop?.();
        }
        /* eslint-enable no-await-in-loop */
        return results;
      },
      { cases: samples, material: sampleMaterial }
    );
    for (const { image, mean, plainMean, outliers } of errors) {
      expect(plainMean, image).toBeGreaterThan(2);
      expect(mean, image).toBeLessThanOrEqual(2);
      expect(outliers, image).toBeLessThanOrEqual(0.01);
    }
  });
}

test('a visible face recovers after context loss and releases its backing store on close', async ({
  page
}) => {
  await openRendererFixture(page);
  const result = await page.evaluate(async (descriptor) => {
    const path = './runtime.js';
    const { observeFx } = (await import(
      /* @vite-ignore */ path
    )) as typeof import('../../design/ui/game/enchantment-fx/renderer');
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
