# ADR 0005: The Worker's Cron Triggers start the analyzer and the QQ daily report

Status: Accepted
Date: 2026-11-23

## Context

`.github/workflows/daily-report.yml` must send the previous UTC day's report at about 10:00 Beijing time. Its own `on.schedule` (`50 1 * * *`) started between 08:05 and 09:50 UTC on every run from 2026-11-09 to 2026-11-23, so the groups got the report six to eight hours late. GitHub runs schedules on a best-effort basis; the hourly `analyzer.yml` schedule fires three or four times a day for the same reason. Cloudflare Cron Triggers have started this Worker's D1 retention on schedule, and the Worker already has one.

An analyzer run costs about two and a half minutes of setup, restore, and mirror, plus intake that grows with the Source Hours since the last run: about 75 seconds after two hours, 280 seconds after seven, 540 seconds after eight and a half. The report waits for its own analyzer run, so a long gap before 01:50 UTC delays the report.

## Decision

`WORKFLOW_DISPATCHES` in `src/modules/workflow-dispatch.ts` maps Cron Triggers to GitHub `workflow_dispatch` calls on `master`:

- `0 1 * * *` and `0 12 * * *` start `analyzer.yml`. The 01:00 run seals the previous UTC day, so the report's analyzer run at 01:50 has about one Source Hour of intake; the 12:00 run caps the gap before 01:00 at thirteen hours.
- `50 1 * * *` starts `daily-report.yml` with `send=report`. The workflow has no `on.schedule`, so GitHub's late run cannot send a second report.

`scheduled` looks up `controller.cron`; the retention trigger never dispatches, and a dispatch trigger never prunes. `analyzer.yml` keeps its hourly GitHub schedule; its `analyzer-v6` concurrency group serializes the Worker's runs with GitHub's.

The token is the Worker secret `GITHUB_DISPATCH_TOKEN`: a fine-grained token for `BazaarPlusPlus/BazaarPlusPlus` only, with Actions read and write and nothing else. A missing token, a failed request, or a rejected request fails the invocation and logs `workflow.dispatch.failed` with the workflow, and the HTTP status when GitHub answered. There is no retry: a second dispatch after a lost response would send the report twice.

## Consequences

A late report becomes a missing report when the token expires or is revoked. `workflow.dispatch.failed` in Workers Observability is the signal; rotate the token before its expiry date. A manual `workflow_dispatch` of `daily-report.yml` with `send=report` before the next 00:00 UTC sends a missed day; after that, the workflow's previous-UTC-day check sends the next day instead, so a missed day past midnight stays unsent.
