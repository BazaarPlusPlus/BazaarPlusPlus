import assert from 'node:assert/strict';
import test from 'node:test';
import { installerTimingReport } from './installer-timings.mjs';

const at = (second) =>
  new Date(Date.UTC(2026, 0, 1, 0, 0, second)).toISOString();
const step = (name, start, end, conclusion = 'success') => ({
  name,
  started_at: at(start),
  completed_at: at(end),
  conclusion
});

test('reports both platforms, queue and post-job save without counting another job', () => {
  const job = {
    name: 'Installer (windows-latest)',
    html_url: 'https://example.com/job',
    created_at: at(0),
    started_at: at(5),
    completed_at: at(45),
    conclusion: 'success',
    steps: [
      step('Restore Installer Rust cache', 10, 12),
      step('Install Installer npm dependencies', 12, 15),
      step('Verify Installer', 15, 35),
      step('Post Restore Installer Rust cache', 35, 39)
    ]
  };
  const report = installerTimingReport([
    job,
    { ...job, name: 'Installer (macos-14)' },
    { ...job, name: 'Release tooling' }
  ]);
  assert.match(report, /Queue \| 5/);
  assert.match(report, /Rust cache restore \| 2/);
  assert.match(report, /npm ci \| 3/);
  assert.match(report, /Verification \| 20/);
  assert.match(report, /Rust cache save \/ cleanup \| 4/);
  assert.match(report, /Other job time \| 11/);
  assert.match(report, /Total \(excluding queue\) \| 40/);
  assert.match(report, /Installer \(macos-14\)/);
  assert.doesNotMatch(report, /Release tooling/);
});

test('failed and incomplete jobs do not turn missing measurements into successes', () => {
  const report = installerTimingReport([
    {
      name: 'Installer (windows-latest)',
      html_url: 'https://example.com/job',
      started_at: at(0),
      completed_at: null,
      conclusion: 'cancelled',
      steps: [
        step('Verify Installer', 5, 8, 'failure'),
        { name: 'Post Restore Installer Rust cache', conclusion: 'skipped' }
      ]
    }
  ]);
  assert.match(report, /Queue \| unavailable/);
  assert.match(report, /Verification \| 3 \| failure/);
  assert.match(report, /npm ci \| unavailable \| not run/);
  assert.match(report, /Rust cache save \/ cleanup \| 0 \| skipped/);
  assert.match(report, /Total \(excluding queue\) \| unavailable \| cancelled/);
});
