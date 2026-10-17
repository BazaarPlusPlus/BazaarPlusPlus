import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Playwright owns e2e/*.spec.ts; without this, Vitest's default glob collects them too.
    include: ['test/**/*.test.ts'],
    environment: 'jsdom',
  },
});
