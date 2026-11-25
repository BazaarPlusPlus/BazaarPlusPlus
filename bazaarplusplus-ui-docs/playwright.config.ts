import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: 'e2e',
  workers: 1,
  timeout: 90000,
  use: { baseURL: 'http://localhost:4176' },
  projects: [1280, 390].map((width) => ({
    name: String(width),
    use: { viewport: { width, height: 900 } }
  })),
  webServer: {
    command:
      process.env.BPP_UI_DOCS_PREBUILT === '1'
        ? 'bun run preview:built'
        : 'bun run preview',
    url: 'http://localhost:4176'
  }
});
