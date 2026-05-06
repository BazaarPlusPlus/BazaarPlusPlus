import { test, expect } from 'vitest';

import {
  loadPersistedDetectedGamePath,
  loadPersistedCustomGamePath,
  persistDetectedGamePath,
  persistCustomGamePath
} from './storage.ts';

function createStorage() {
  const values = new Map<string, string>();

  return {
    getItem(key: string) {
      return values.has(key) ? values.get(key)! : null;
    },
    setItem(key: string, value: string) {
      values.set(key, value);
    },
    removeItem(key: string) {
      values.delete(key);
    }
  };
}

test('persistCustomGamePath stores and clears the selected game path', () => {
  const originalWindow = globalThis.window;
  const localStorage = createStorage();

  Object.defineProperty(globalThis, 'window', {
    configurable: true,
    value: { localStorage }
  });

  try {
    expect(loadPersistedCustomGamePath()).toBe('');

    persistCustomGamePath('  /games/the-bazaar  ');
    expect(loadPersistedCustomGamePath()).toBe('/games/the-bazaar');

    persistCustomGamePath('   ');
    expect(loadPersistedCustomGamePath()).toBe('');
  } finally {
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: originalWindow
    });
  }
});

test('persistDetectedGamePath stores and clears the detected game path cache', () => {
  const originalWindow = globalThis.window;
  const localStorage = createStorage();

  Object.defineProperty(globalThis, 'window', {
    configurable: true,
    value: { localStorage }
  });

  try {
    expect(loadPersistedDetectedGamePath()).toBe('');

    persistDetectedGamePath('  /games/the-bazaar-auto  ');
    expect(loadPersistedDetectedGamePath()).toBe('/games/the-bazaar-auto');

    persistDetectedGamePath('   ');
    expect(loadPersistedDetectedGamePath()).toBe('');
  } finally {
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: originalWindow
    });
  }
});
