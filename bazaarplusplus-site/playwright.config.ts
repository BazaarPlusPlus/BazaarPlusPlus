import { defineConfig, devices } from '@playwright/test';

// `BPP_UPDATE_GOLDENS=1` is the repository-wide way to rewrite goldens; `--update-snapshots`
// still works because the CLI flag overrides this value.
const updateGoldens = process.env.BPP_UPDATE_GOLDENS === '1';

export default defineConfig({
  testDir: 'e2e',
  outputDir: 'test-results',
  snapshotPathTemplate: '{testDir}/__snapshots__/{arg}{ext}',
  updateSnapshots: updateGoldens ? 'all' : 'missing',
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['github']] : 'list',
  expect: {
    toHaveScreenshot: { pathTemplate: '{testDir}/__snapshots__/{arg}-{platform}{ext}' },
  },
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://localhost:3000',
    // Dates and numbers in the goldens are formatted by the browser, so pin both.
    locale: 'en-US',
    timezoneId: 'UTC',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'golden', testIgnore: /live\.spec\.ts$/ },
    { name: 'live', testMatch: /live\.spec\.ts$/ },
  ],
  webServer: {
    command: 'npm run preview',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
