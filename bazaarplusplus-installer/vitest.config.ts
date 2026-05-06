import viteConfig from './vite.config.js';
import { defineConfig, mergeConfig } from 'vitest/config';

export default defineConfig(async (configEnv) =>
  mergeConfig(
    typeof viteConfig === 'function' ? await viteConfig(configEnv) : viteConfig,
    {
      test: {
        environment: 'node'
      }
    }
  )
);
