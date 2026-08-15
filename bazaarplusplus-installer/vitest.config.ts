import viteConfig from './vite.config.ts';
import { configDefaults, defineConfig, mergeConfig } from 'vitest/config';

export default defineConfig(
  mergeConfig(viteConfig, {
    test: {
      environment: 'node',
      exclude: [
        ...configDefaults.exclude,
        '.claude/**',
        '.superpowers/**',
        '.worktrees/**'
      ]
    }
  })
);
