import { defineConfig, devices } from '@playwright/test';

// The CLI's --update-snapshots takes precedence over BPP_UPDATE_GOLDENS.
const updateGoldens = process.env.BPP_UPDATE_GOLDENS === '1';
const prebuilt = process.env.BPP_SITE_PREBUILT === '1';

export default defineConfig({
  testDir: 'e2e',
  outputDir: 'test-results',
  snapshotPathTemplate: '{testDir}/__snapshots__/{arg}{ext}',
  updateSnapshots: updateGoldens ? 'all' : 'missing',
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['github']] : 'list',
  expect: {
    toHaveScreenshot: {
      pathTemplate: '{testDir}/__snapshots__/{arg}-{platform}{ext}'
    }
  },
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://localhost:3000',
    // Dates and numbers in the goldens are formatted by the browser, so pin both.
    locale: 'en-US',
    timezoneId: 'UTC',
    trace: 'retain-on-failure'
  },
  projects: [
    { name: 'golden', testIgnore: /live\.spec\.ts$/ },
    { name: 'live', testMatch: /live\.spec\.ts$/ }
  ],
  webServer: {
    command: prebuilt ? 'bun run preview:built' : 'bun run preview',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI && !prebuilt,
    timeout: 120_000
  }
});
