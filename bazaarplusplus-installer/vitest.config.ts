import viteConfig from './vite.config';
import { defineConfig, mergeConfig } from 'vitest/config';

export default defineConfig(
  mergeConfig(viteConfig, {
    test: {
      environment: 'node'
    }
  })
);
