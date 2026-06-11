import { readFileSync } from 'node:fs';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const host = process.env.TAURI_DEV_HOST;
const pkg = JSON.parse(readFileSync('package.json', 'utf-8')) as {
  version: string;
};

export default defineConfig({
  plugins: [tailwindcss(), react()],
  define: {
    __FRONTEND_VERSION__: JSON.stringify(pkg.version)
  },
  build: {
    outDir: 'build',
    emptyOutDir: true
  },
  clearScreen: false,
  server: {
    port: 14207,
    strictPort: true,
    host: host || '0.0.0.0',
    hmr: host
      ? {
          protocol: 'ws',
          host,
          port: 14208
        }
      : undefined,
    watch: {
      ignored: ['**/src-tauri/**']
    }
  }
});
