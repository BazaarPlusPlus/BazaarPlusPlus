import fs from 'node:fs';
import { SCOPES } from './checks-scope.mjs';

export const JOB_SCOPES = {
  release: 'release',
  site: 'site',
  server: 'server',
  analyzer: 'analyzer',
  'installer-macos': 'installer',
  'installer-windows': 'installer',
  'installer-timings': 'installer',
  'mod-macos': 'mod',
  'mod-windows': 'mod',
  'macos-icon': 'macos-icon'
};

export function summarize({ needs, eventName, event, modCredentials }) {
  const errors = [];
  const rows = [];
  let plan;
  try {
    if (needs.classify?.result !== 'success')
      throw new Error('Classification did not succeed');
    plan = JSON.parse(needs.classify.outputs.plan);
    if (
      !plan ||
      Object.keys(plan).length !== SCOPES.length ||
      !SCOPES.every(
        (scope) =>
          Array.isArray(plan[scope]) &&
          plan[scope].every(
            (reason) => typeof reason === 'string' && reason.length
          )
      )
    )
      throw new Error('Invalid classification plan');
    if (!SCOPES.some((scope) => plan[scope].length))
      throw new Error('Classification selected no checks');
    for (const scope of SCOPES)
      if (needs.classify.outputs[scope] !== String(plan[scope].length > 0))
        throw new Error(`Inconsistent selection: ${scope}`);
  } catch (error) {
    errors.push(error.message);
  }
  const fork =
    eventName === 'pull_request' &&
    event.pull_request?.head?.repo?.full_name &&
    event.repository?.full_name &&
    event.pull_request.head.repo.full_name !== event.repository.full_name;
  if (!errors.length)
    for (const [job, scope] of Object.entries(JOB_SCOPES)) {
      const reasons = plan[scope];
      let status = needs[job]?.result ?? 'missing';
      if (!reasons.length) status = '不受影响';
      else if (scope === 'mod' && fork) {
        if (status !== 'skipped')
          errors.push(`${job}: fork exception expected skipped, got ${status}`);
        status = 'fork 例外：需本地 Mod 验证，不提供私有快照凭据';
      } else {
        if (scope === 'mod' && modCredentials !== 'true')
          errors.push(
            `${job}: missing BPP_GAME_LIBS_TOKEN or CLOUDFLARE_ACCOUNT_ID; Mod checks were not verified`
          );
        if (status !== 'success') errors.push(`${job}: selected but ${status}`);
      }
      rows.push({ job, status, reasons });
    }
  return { ok: errors.length === 0, errors, rows };
}

export function renderSummary(result, { eventName, ref, sha }) {
  const escape = (text) =>
    String(text)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('|', '&#124;')
      .replaceAll('\n', ' ');
  return [
    `Event: ${escape(eventName)} · Ref: ${escape(ref)} · SHA: ${escape(sha)}`,
    '',
    '| Job | Result | Selection reason |',
    '| --- | --- | --- |',
    ...result.rows.map(
      ({ job, status, reasons }) =>
        `| ${job} | ${escape(status)} | ${reasons.map(escape).join('<br>') || '不受影响'} |`
    ),
    '',
    ...result.errors.map((error) => `- ${escape(error)}`),
    ''
  ].join('\n');
}

if (import.meta.main) {
  const event = JSON.parse(
    fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')
  );
  const result = summarize({
    needs: JSON.parse(process.env.CHECK_NEEDS),
    eventName: process.env.GITHUB_EVENT_NAME,
    event,
    modCredentials: process.env.MOD_CREDENTIALS
  });
  const report = renderSummary(result, {
    eventName: process.env.GITHUB_EVENT_NAME,
    ref: process.env.GITHUB_REF,
    sha: process.env.GITHUB_SHA
  });
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, report);
  console.log(report);
  if (!result.ok) process.exitCode = 1;
}
