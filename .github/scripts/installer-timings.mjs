import { execFileSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';

function seconds(start, end) {
  if (!start || !end) return null;
  const value = (Date.parse(end) - Date.parse(start)) / 1000;
  return Number.isFinite(value) && value >= 0 ? value : null;
}

export function installerTimingReport(jobs) {
  const lines = [
    '## Installer job timings',
    '',
    'Seconds from the Jobs API for this run attempt. Queue time starts at job creation; other time includes tool setup, cleanup and runner overhead. Verification details are in each Installer job summary.',
    ''
  ];
  for (const job of jobs.filter(({ name }) =>
    /^Installer \(.+\)$/.test(name)
  )) {
    const stages = new Map([
      ['Rust cache restore', 'Restore Installer Rust cache'],
      ['npm ci', 'Install Installer npm dependencies'],
      ['Verification', 'Verify Installer'],
      ['Rust cache save / cleanup', 'Post Restore Installer Rust cache']
    ]);
    const total = seconds(job.started_at, job.completed_at);
    let measured = 0;
    lines.push(
      `### [${job.name}](${job.html_url}) — ${job.conclusion ?? job.status}`,
      '',
      '| Stage | Seconds | Result |',
      '| --- | ---: | --- |',
      `| Queue | ${seconds(job.created_at, job.started_at) ?? 'unavailable'} | |`
    );
    for (const [label, name] of stages) {
      const step = job.steps.find((entry) => entry.name === name);
      const duration =
        step?.conclusion === 'skipped'
          ? 0
          : seconds(step?.started_at, step?.completed_at);
      measured += duration ?? 0;
      lines.push(
        `| ${label} | ${duration ?? 'unavailable'} | ${step?.conclusion ?? 'not run'} |`
      );
    }
    lines.push(
      `| Other job time | ${total === null ? 'unavailable' : Math.max(0, total - measured)} | |`,
      `| Total (excluding queue) | ${total ?? 'unavailable'} | ${job.conclusion ?? job.status} |`,
      ''
    );
  }
  return lines.join('\n');
}

if (import.meta.main) {
  const {
    GITHUB_REPOSITORY,
    GITHUB_RUN_ID,
    GITHUB_RUN_ATTEMPT,
    GITHUB_STEP_SUMMARY
  } = process.env;
  const pages = JSON.parse(
    execFileSync(
      'gh',
      [
        'api',
        '--paginate',
        '--slurp',
        `repos/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}/attempts/${GITHUB_RUN_ATTEMPT}/jobs?per_page=100`
      ],
      { encoding: 'utf8' }
    )
  );
  const report = installerTimingReport(pages.flatMap(({ jobs }) => jobs));
  appendFileSync(GITHUB_STEP_SUMMARY, report);
  console.log(report);
}
