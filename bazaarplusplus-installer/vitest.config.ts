import viteConfig from './vite.config.ts';
import { configDefaults, defineConfig, mergeConfig } from 'vitest/config';

// The UI formats times in the viewer's zone; pin the zone so date assertions
// give the same result on every machine. Workers inherit it.
process.env.TZ = 'UTC';

export default defineConfig(
  mergeConfig(viteConfig, {
    test: {
      environment: 'node',
      // The repository-wide golden regeneration switch; `-u` still works.
      update: process.env.BPP_UPDATE_GOLDENS === '1',
      exclude: [
        ...configDefaults.exclude,
        '.claude/**',
        '.superpowers/**',
        '.worktrees/**'
      ]
    }
  })
);
