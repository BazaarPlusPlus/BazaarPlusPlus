import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve as resolvePath } from 'node:path';
import { expect, test } from '@playwright/test';
import { enchantments, type FxMaterial } from '@bazaarplusplus/ui/game';

const catalogDirectory = resolvePath('public/card-art');
const catalogManifest = JSON.parse(
  readFileSync(resolvePath(catalogDirectory, 'manifest.json'), 'utf8')
) as {
  cards: Array<{
    id: string;
    file: string;
    fx: string;
    en: string;
    size: string;
  }>;
};

test('all 16 enchantments render visibly, animate, and restrict Tiny to Medium and Large cards', async ({
  page
}, testInfo) => {
  test.skip(
    testInfo.project.name !== '1280',
    'Viewport-independent WebGL2 shader verification'
  );
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
    return route.fulfill({ path: resolvePath(catalogDirectory, name) });
  });
  await page.goto('/fx-check/');

  const cases = [
    ...enchantments.map((effect, index) => ({
      effect,
      material: catalogManifest.cards[index].fx,
      tiny: effect === 'Tiny',
      slots:
        effect === 'Tiny'
          ? catalogManifest.cards[index].size === 'Large'
            ? 2
            : 1
          : undefined
    })),
    {
      effect: 'Tiny' as const,
      material: catalogManifest.cards[16].fx,
      tiny: true,
      slots: 2
    }
  ];
  const smallFx = catalogManifest.cards.find((c) => c.size === 'Small')!.fx;
  const report = await page.evaluate(
    async ({ cases: items, smallMaterial }) => {
      const path = './runtime.js';
      const { observeFx } = (await import(
        /* @vite-ignore */ path
      )) as typeof import('../../packages/ui/src/game/enchantment-fx/renderer');
      const canvas = document.querySelector('canvas');
      if (!canvas) throw new Error('Missing test face');
      const native = window.requestAnimationFrame;
      let seconds = 1;
      window.requestAnimationFrame = (callback) =>
        native(() => callback(seconds * 1000));

      const readPixels = (w: number, h: number) => {
        const scratch = document.createElement('canvas');
        scratch.width = w;
        scratch.height = h;
        const ctx = scratch.getContext('2d')!;
        ctx.drawImage(canvas, 0, 0, w, h);
        return ctx.getImageData(0, 0, w, h).data;
      };

      const rendered = [];
      /* eslint-disable no-await-in-loop */
      for (const item of items) {
        const descriptor: FxMaterial = await (
          await fetch(item.material)
        ).json();
        const sizeSlots =
          item.slots ?? { small: 1, medium: 2, large: 3 }[descriptor.size];
        const width = sizeSlots * 100;
        const height = 200;
        canvas.style.width = width + 'px';
        canvas.style.height = height + 'px';

        const capture = async (time: number) => {
          seconds = time;
          const stop = observeFx(
            canvas,
            {
              material: async () => descriptor,
              texture: async (file) => (await fetch(file)).blob(),
              tiny: item.tiny,
              slots: item.slots
            },
            item.effect,
            false
          );
          for (let i = 0; i < 240 && !canvas.width; i++)
            await new Promise(native);
          if (!canvas.width) throw new Error('No frame for ' + item.effect);
          const pixels = readPixels(width, height);
          stop?.();
          return pixels;
        };

        const frame1 = await capture(1);
        const frame2 = await capture(2.5);
        const artFile = descriptor.textures['_MainTex'].file;
        if (!artFile) throw new Error('Missing illustration');
        const bitmap = await createImageBitmap(
          await (await fetch(artFile)).blob()
        );
        const scratch = document.createElement('canvas');
        scratch.width = width;
        scratch.height = height;
        const ctx = scratch.getContext('2d')!;
        ctx.drawImage(bitmap, 0, 0, width, height);
        const plain = ctx.getImageData(0, 0, width, height).data;
        bitmap.close();

        let plainSum = 0;
        let motionSum = 0;
        for (let i = 0; i < frame1.length; i += 4) {
          plainSum +=
            Math.abs(frame1[i] - plain[i]) +
            Math.abs(frame1[i + 1] - plain[i + 1]) +
            Math.abs(frame1[i + 2] - plain[i + 2]);
          motionSum +=
            Math.abs(frame1[i] - frame2[i]) +
            Math.abs(frame1[i + 1] - frame2[i + 1]) +
            Math.abs(frame1[i + 2] - frame2[i + 2]);
        }
        const channels = 3 * (frame1.length / 4);
        rendered.push({
          effect: item.effect,
          size: descriptor.size,
          slots: sizeSlots,
          deltaFromPlain: plainSum / channels,
          motionDelta: motionSum / channels
        });
      }

      // Tiny on a Small card must stay disabled (canvas stays 0px wide).
      const smallDescriptor: FxMaterial = await (
        await fetch(smallMaterial)
      ).json();
      canvas.style.width = '100px';
      canvas.style.height = '200px';
      const stopSmall = observeFx(
        canvas,
        {
          material: async () => smallDescriptor,
          texture: async (file) => (await fetch(file)).blob(),
          tiny: true,
          slots: 1
        },
        'Tiny',
        false
      );
      for (let i = 0; i < 30; i++) await new Promise(native);
      const smallTinyWidth = canvas.width;
      stopSmall?.();
      /* eslint-enable no-await-in-loop */
      return { rendered, smallTinyWidth };
    },
    { cases, smallMaterial: smallFx }
  );

  for (const row of report.rendered) {
    expect(
      row.deltaFromPlain,
      `${row.effect} (${row.size}->${row.slots}) visibility`
    ).toBeGreaterThan(6);
    expect(
      row.motionDelta,
      `${row.effect} (${row.size}->${row.slots}) animation`
    ).toBeGreaterThan(0.5);
  }
  expect(report.smallTinyWidth).toBe(0);
});
