import fs from 'node:fs';
import path from 'node:path';
import { readChanges } from './checks-scope.ts';
import type { ChecksEvent, DiffChange } from './checks-scope.ts';

export const CODEQL_LANGUAGES = ['javascript-typescript', 'python'] as const;
export type CodeQLLanguage = (typeof CODEQL_LANGUAGES)[number];
export type CodeQLPlan = Record<CodeQLLanguage, string[]>;

export interface PlanCodeQLOptions {
  eventName?: string;
  event: ChecksEvent;
  cwd?: string;
  runGit?: (args: readonly string[]) => string;
  historyFetchOutcome?: string;
}

export interface CodeQLNeed {
  result?: string;
  outputs?: Record<string, string>;
}

export interface CodeQLNeeds {
  classify?: CodeQLNeed;
  'javascript-typescript'?: CodeQLNeed;
  python?: CodeQLNeed;
}

export interface CodeQLSummaryRow {
  language: CodeQLLanguage;
  status: string;
  reasons: string[];
}

export interface CodeQLSummaryResult {
  ok: boolean;
  errors: string[];
  rows: CodeQLSummaryRow[];
}

const FULL_CODEQL_INPUTS = new Set([
  '.github/workflows/codeql.yml',
  '.github/scripts/codeql-scope.ts',
  'scripts/git-command.ts'
]);

const JS_TS_FILE = /\.(?:[cm]?[jt]sx?|html?)$/i;
const JS_TS_MANIFEST = /^(?:package\.json|tsconfig(?:\.[^/]+)?\.json)$/;
const PYTHON_FILE = /\.pyi?$/i;

export function fullCodeQLPlan(reason: string): CodeQLPlan {
  return {
    'javascript-typescript': [reason],
    python: [reason]
  };
}

export function classifyCodeQLChanges(
  changes: DiffChange[] | null
): CodeQLPlan {
  if (!Array.isArray(changes) || !changes.length)
    return fullCodeQLPlan('Diff unavailable or empty');
  const plan: CodeQLPlan = {
    'javascript-typescript': [],
    python: []
  };
  const select = (language: CodeQLLanguage, reason: string) => {
    if (!plan[language].includes(reason)) plan[language].push(reason);
  };
  for (const { status, file } of changes) {
    if (
      typeof file !== 'string' ||
      !/^[AMDT]$/.test(status) ||
      file.startsWith('/') ||
      file.split('/').includes('..')
    )
      return fullCodeQLPlan('Unrecognized diff entry');
    if (FULL_CODEQL_INPUTS.has(file) || file.startsWith('.github/codeql/')) {
      return fullCodeQLPlan(`CodeQL configuration: ${file}`);
    }
    const base = path.posix.basename(file);
    if (JS_TS_FILE.test(file) || JS_TS_MANIFEST.test(base)) {
      select('javascript-typescript', `JavaScript/TypeScript input: ${file}`);
    }
    if (PYTHON_FILE.test(file) || base === 'pyproject.toml') {
      select('python', `Python input: ${file}`);
    }
  }
  return plan;
}

export function planCodeQLRun({
  eventName,
  event,
  cwd,
  runGit,
  historyFetchOutcome = 'success'
}: PlanCodeQLOptions): CodeQLPlan {
  if (eventName === 'schedule' || eventName === 'workflow_dispatch')
    return fullCodeQLPlan(`Scheduled or manual run: ${eventName}`);
  if (historyFetchOutcome !== 'success')
    return fullCodeQLPlan('History fetch failed');
  try {
    const changes = readChanges(eventName, event, cwd, runGit);
    return classifyCodeQLChanges(changes);
  } catch (error) {
    return fullCodeQLPlan(`Conservative full run: ${(error as Error).message}`);
  }
}

export function summarizeCodeQL(needs: CodeQLNeeds): CodeQLSummaryResult {
  const errors: string[] = [];
  const rows: CodeQLSummaryRow[] = [];
  let plan: CodeQLPlan | null = null;
  try {
    if (needs.classify?.result !== 'success')
      throw new Error('CodeQL classification did not succeed');
    const outputs = needs.classify.outputs ?? {};
    const parsed = JSON.parse(outputs.plan ?? '') as Record<string, unknown>;
    if (
      !parsed ||
      typeof parsed !== 'object' ||
      Object.keys(parsed).length !== CODEQL_LANGUAGES.length ||
      !CODEQL_LANGUAGES.every(
        (lang) =>
          Array.isArray(parsed[lang]) &&
          (parsed[lang] as unknown[]).every(
            (reason) => typeof reason === 'string' && reason.length > 0
          )
      )
    )
      throw new Error('Invalid CodeQL classification plan');
    for (const lang of CODEQL_LANGUAGES) {
      const selected = (parsed[lang] as string[]).length > 0;
      if (outputs[lang] !== String(selected))
        throw new Error(`Inconsistent CodeQL selection: ${lang}`);
    }
    plan = parsed as CodeQLPlan;
  } catch (error) {
    errors.push((error as Error).message);
  }

  if (plan) {
    for (const language of CODEQL_LANGUAGES) {
      const reasons = plan[language];
      let status = needs[language]?.result ?? 'missing';
      if (!reasons.length) {
        status = '不受影响';
      } else if (status !== 'success') {
        errors.push(`CodeQL (${language}): selected but ${status}`);
      }
      rows.push({ language, status, reasons });
    }
  }
  return { ok: errors.length === 0, errors, rows };
}

export function renderCodeQLSummary(result: CodeQLSummaryResult): string {
  const escape = (text: string) =>
    text
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('|', '&#124;')
      .replaceAll('\n', ' ');
  return [
    '| Language | Result | Selection reason |',
    '| --- | --- | --- |',
    ...result.rows.map(
      ({ language, status, reasons }) =>
        `| ${language} | ${escape(status)} | ${reasons.map(escape).join('<br>') || '不受影响'} |`
    ),
    '',
    ...result.errors.map((error) => `- ${escape(error)}`),
    ''
  ].join('\n');
}

if (import.meta.main) {
  const mode = process.argv[2] ?? 'classify';
  if (mode === 'classify') {
    const event = JSON.parse(
      fs.readFileSync(process.env.GITHUB_EVENT_PATH as string, 'utf8')
    ) as ChecksEvent;
    const plan = planCodeQLRun({
      eventName: process.env.GITHUB_EVENT_NAME,
      event,
      historyFetchOutcome: process.env.HISTORY_FETCH_OUTCOME
    });
    const output = CODEQL_LANGUAGES.map(
      (lang) => `${lang}=${plan[lang].length > 0}`
    ).join('\n');
    fs.appendFileSync(
      process.env.GITHUB_OUTPUT as string,
      `${output}\nplan=${JSON.stringify(plan)}\n`
    );
    console.log(JSON.stringify(plan, null, 2));
  } else if (mode === 'summary') {
    const needs = JSON.parse(process.env.CODEQL_NEEDS as string) as CodeQLNeeds;
    const result = summarizeCodeQL(needs);
    const report = renderCodeQLSummary(result);
    fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY as string, report);
    console.log(report);
    if (!result.ok) process.exitCode = 1;
  } else {
    throw new Error(`Unknown mode: ${mode}`);
  }
}
