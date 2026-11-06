import fs from 'node:fs';
import { SCOPES, manualSelection } from './checks-scope.ts';
import type { ChecksPlan, ManualInputs } from './checks-scope.ts';

// One entry of the workflow `needs` context.
export interface JobNeed {
  result?: string;
  outputs?: Record<string, string>;
}

// The classify job's entry: its outputs carry the plan and one flag per lane.
export interface ClassifyNeed extends JobNeed {
  outputs: Record<string, string>;
}

// The workflow `needs` context, as CHECK_NEEDS passes it.
export interface CheckNeeds {
  classify?: ClassifyNeed;
  [job: string]: JobNeed | undefined;
}

// The fields of the GitHub event payload that the summary reads.
export interface SummaryEvent {
  pull_request?: { head?: { repo?: { full_name?: string } } };
  repository?: { full_name?: string };
  inputs?: ManualInputs;
}

export interface SummarizeOptions {
  needs: CheckNeeds;
  eventName: string | undefined;
  event: SummaryEvent;
  modCredentials: string | undefined;
}

export interface SummaryRow {
  job: string;
  status: string;
  reasons: string[];
}

export interface SummaryResult {
  ok: boolean;
  errors: string[];
  rows: SummaryRow[];
  coverage: string[];
}

export interface RenderSummaryOptions {
  eventName: string | undefined;
  ref: string | undefined;
  sha: string | undefined;
  inputs?: ManualInputs;
}

export function summarize({
  needs,
  eventName,
  event,
  modCredentials
}: SummarizeOptions): SummaryResult {
  const errors: string[] = [];
  const rows: SummaryRow[] = [];
  const coverage: string[] = [];
  // Read only after the classification below validated it.
  let plan: ChecksPlan = {};
  try {
    if (needs.classify?.result !== 'success')
      throw new Error('Classification did not succeed');
    plan = JSON.parse(needs.classify.outputs.plan) as ChecksPlan;
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
    for (const flag of ['compatibility', 'performance'])
      if (!['true', 'false'].includes(needs.classify.outputs[flag]))
        throw new Error(`Invalid classification flag: ${flag}`);
    if (['mod-macos', 'mod-windows'].some((scope) => plan[scope].length)) {
      coverage.push('online');
      if (needs.classify.outputs.compatibility === 'true')
        coverage.push('staging / ptr');
      if (needs.classify.outputs.performance === 'true')
        coverage.push('performance');
    }
  } catch (error) {
    errors.push((error as Error).message);
  }
  const fork =
    eventName === 'pull_request' &&
    event.pull_request?.head?.repo?.full_name &&
    event.repository?.full_name &&
    event.pull_request.head.repo.full_name !== event.repository.full_name;
  if (!errors.length)
    for (const job of SCOPES) {
      const reasons = plan[job];
      let status = needs[job]?.result ?? 'missing';
      if (!reasons.length) status = '不受影响';
      else if (job.startsWith('mod-') && fork) {
        if (status !== 'skipped')
          errors.push(`${job}: fork exception expected skipped, got ${status}`);
        status = 'fork 例外：需本地 Mod 验证，不提供私有快照凭据';
      } else {
        if (job.startsWith('mod-') && modCredentials !== 'true')
          errors.push(
            `${job}: missing CLOUDFLARE_API_TOKEN or CLOUDFLARE_ACCOUNT_ID; Mod checks were not verified`
          );
        if (status !== 'success') errors.push(`${job}: selected but ${status}`);
      }
      rows.push({ job, status, reasons });
    }
  return { ok: errors.length === 0, errors, rows, coverage };
}

export function renderSummary(
  result: SummaryResult,
  { eventName, ref, sha, inputs }: RenderSummaryOptions
): string {
  const escape = (text: string | undefined) =>
    String(text)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('|', '&#124;')
      .replaceAll('\n', ' ');
  return [
    `Event: ${escape(eventName)} · Ref: ${escape(ref)} · SHA: ${escape(sha)}`,
    ...(eventName === 'workflow_dispatch'
      ? [
          `Coverage: ${manualSelection(inputs).scope}/${manualSelection(inputs).platform}`
        ]
      : []),
    ...(result.coverage.length
      ? [`Planned Mod desktop checks: ${result.coverage.join(' + ')}`]
      : []),
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
    fs.readFileSync(process.env.GITHUB_EVENT_PATH as string, 'utf8')
  ) as SummaryEvent;
  const result = summarize({
    needs: JSON.parse(process.env.CHECK_NEEDS as string) as CheckNeeds,
    eventName: process.env.GITHUB_EVENT_NAME,
    event,
    modCredentials: process.env.MOD_CREDENTIALS
  });
  const report = renderSummary(result, {
    eventName: process.env.GITHUB_EVENT_NAME,
    ref: process.env.GITHUB_REF,
    sha: process.env.GITHUB_SHA,
    inputs: event.inputs
  });
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY as string, report);
  console.log(report);
  if (!result.ok) process.exitCode = 1;
}
