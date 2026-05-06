import { expect, test } from 'vitest';

import { createLocaleStore } from './locale.ts';

test('locale apply syncs the tray language', async () => {
  const synced: string[] = [];
  const store = createLocaleStore({
    syncTrayLocale: async (nextLocale) => {
      synced.push(nextLocale);
    }
  });

  store.apply('en');
  await Promise.resolve();

  expect(synced).toEqual(['en']);
});

test('locale toggle syncs the tray language after switching locale', async () => {
  const synced: string[] = [];
  const store = createLocaleStore({
    syncTrayLocale: async (nextLocale) => {
      synced.push(nextLocale);
    }
  });

  store.apply('zh');
  store.toggle();
  await Promise.resolve();

  expect(synced).toEqual(['zh', 'en']);
});
