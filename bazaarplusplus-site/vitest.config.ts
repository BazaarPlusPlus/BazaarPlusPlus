import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    fs: {
      allow: ['.', '../bazaarplusplus-installer/static/support'],
    },
  },
  test: {
    // Playwright owns e2e/*.spec.ts; without this, Vitest's default glob collects them too.
    include: ['test/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./test/setup.ts'],
  },
});
