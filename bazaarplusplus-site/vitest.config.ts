import viteConfig from './vite.config';
import { defineConfig, mergeConfig } from 'vite-plus';

// Merged so tests resolve `@bpp/ui` and React the same way the build does.
export default defineConfig(
  mergeConfig(viteConfig, {
    test: {
      // Playwright owns e2e/*.spec.ts; without this, Vitest's default glob collects them too.
      include: ['test/**/*.test.ts'],
      environment: 'jsdom'
    }
  })
);
