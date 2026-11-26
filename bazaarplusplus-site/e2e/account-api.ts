import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import type { Page, Request } from '@playwright/test';

import {
  SAMPLE_RUN_ID,
  SAMPLE_TEMPLATE_IDS,
  sampleRunPayload
} from '../test/fixtures/run-payload';

export { SAMPLE_RUN_ID, SAMPLE_TEMPLATE_IDS };

type Reply =
  | { status?: number; json: unknown }
  | { status?: number; body: Buffer; contentType: string };

/** Answers one `/api/*` call; the key is `<METHOD> <path after /api/>`. */
export type ApiHandler = (
  body: Record<string, string> | null,
  request: Request
) => Reply;

export function apiError(status: number, code: string): Reply {
  return {
    status,
    json: { error: { code, message: code, retryable: false, request_id: 't' } }
  };
}

export const SIGNED_OUT = apiError(401, 'unauthorized');

declare global {
  interface Window {
    turnstileStub: {
      renders: number;
      resets: number;
      issued: number;
      options: { sitekey: string; theme: string; appearance: string };
    };
  }
}

const TURNSTILE_SCRIPT =
  /^https:\/\/challenges\.cloudflare\.com\/turnstile\/v0\/api\.js\?render=explicit$/;

// Stands in for Cloudflare's api.js: every render and reset issues the next
// token `turnstile-token-<n>` and counts in window.turnstileStub.
const TURNSTILE_STUB = `(() => {
  const stub = { renders: 0, resets: 0, issued: 0, options: null };
  const widgets = new Map();
  const issue = (id) => {
    const options = widgets.get(id);
    if (!options) return;
    stub.issued += 1;
    const token = 'turnstile-token-' + stub.issued;
    setTimeout(() => options.callback(token), 0);
  };
  window.turnstileStub = stub;
  window.turnstile = {
    render(element, options) {
      const id = 'widget-' + (stub.renders += 1);
      stub.options = { ...options, callback: undefined };
      widgets.set(id, options);
      issue(id);
      return id;
    },
    reset(id) {
      stub.resets += 1;
      issue(id);
    },
    remove(id) {
      widgets.delete(id);
    }
  };
})();`;

/** Serves a stub of the Turnstile script so the account forms get tokens offline. */
export async function mockTurnstile(page: Page): Promise<void> {
  await page.route(TURNSTILE_SCRIPT, (route) =>
    route.fulfill({ contentType: 'text/javascript', body: TURNSTILE_STUB })
  );
}

/** Mocks the Website account API and Turnstile; unknown calls answer 404 and are recorded. */
export async function mockAccountApi(
  page: Page,
  handlers: Record<string, ApiHandler>
): Promise<string[]> {
  await mockTurnstile(page);
  const calls: string[] = [];
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const key = `${request.method()} ${url.pathname.replace(/^\/api\//, '')}`;
    calls.push(key + url.search);
    const handler = handlers[key];
    const reply = handler
      ? handler(
          request.postDataJSON() as Record<string, string> | null,
          request
        )
      : apiError(404, 'not_found');
    await route.fulfill(
      'json' in reply
        ? { status: reply.status ?? 200, json: reply.json }
        : {
            status: reply.status ?? 200,
            body: reply.body,
            contentType: reply.contentType
          }
    );
  });
  return calls;
}

const ENDED_AT_MS = Date.UTC(2026, 10, 20, 13, 0);

export function runSummary(overrides: Record<string, unknown> = {}) {
  return {
    run_id: SAMPLE_RUN_ID,
    hero: 'Stelle',
    victories: 8,
    losses: 3,
    final_player_rank: 'Gold',
    final_player_rating: 1218,
    final_player_rating_delta: 18,
    ended_at_ms: ENDED_AT_MS,
    ...overrides
  };
}

export const UNLINKED_ME = { email: 'player@example.test', game: null };

export const LINKED_ME = {
  email: 'player@example.test',
  game: { user_name: 'SamplePlayer', linked_at_ms: Date.UTC(2026, 10, 18) }
};

const HOUR_MS = 3_600_000;

/**
 * Six Records over three local days (today, yesterday, three days ago),
 * listed in upload order with one late upload, as the server returns them:
 * 6 runs, 37 wins (6.2 on average), two ten-win Runs, rating change +27.
 */
export function sampleRecords(now = Date.now()) {
  const today = new Date(now);
  today.setHours(12, 0, 0, 0);
  // Noon plus a few hours stays inside its local day, across a DST change too.
  const at = (daysAgo: number, hour: number) =>
    today.getTime() - daysAgo * 24 * HOUR_MS + hour * HOUR_MS;
  return [
    // The newest is the sample Run, whose detail the Run page can open.
    runSummary({
      ended_at_ms: at(0, 0),
      victories: 10,
      losses: 2,
      final_player_rating: 1218,
      final_player_rating_delta: 22
    }),
    runSummary({
      run_id: 'run-5',
      ended_at_ms: at(0, -1),
      hero: 'Vanessa',
      victories: 3,
      losses: 5,
      final_player_rating: 1196,
      final_player_rating_delta: -14
    }),
    runSummary({
      run_id: 'run-3',
      ended_at_ms: at(1, 2),
      hero: 'Hero8',
      victories: 10,
      losses: 0,
      final_player_rating: 1190,
      final_player_rating_delta: 30
    }),
    // Uploaded late: listed after a newer Run.
    runSummary({
      run_id: 'run-4',
      ended_at_ms: at(1, 4),
      victories: 7,
      losses: 5,
      final_player_rating: 1210,
      final_player_rating_delta: 20
    }),
    runSummary({
      run_id: 'run-2',
      ended_at_ms: at(3, 0),
      victories: 4,
      losses: 5,
      final_player_rating: 1160,
      final_player_rating_delta: -6
    }),
    runSummary({
      run_id: 'run-1',
      ended_at_ms: at(3, -2),
      hero: 'Vanessa',
      victories: 3,
      losses: 5,
      final_player_rating: 1166,
      final_player_rating_delta: -25
    })
  ];
}

/** The signed-in Run detail calls for the sample Run. */
export function runDetailHandlers(
  summary = runSummary()
): Record<string, ApiHandler> {
  return {
    [`GET runs/${SAMPLE_RUN_ID}`]: () => ({ json: { run: summary } }),
    [`GET runs/${SAMPLE_RUN_ID}/payload`]: () => ({
      body: Buffer.from(sampleRunPayload()),
      contentType: 'application/x-bpp-run-v6'
    })
  };
}

/** The sanitized real Run with a Run Message Stream (test/fixtures/run-stream.payload.b64). */
export const STREAM_RUN_ID = '00000000-0000-4000-8000-000000000001';

/** The Run detail calls for the sanitized real Run with a Run Message Stream. */
export function streamRunHandlers(): Record<string, ApiHandler> {
  const payload = Buffer.from(
    readFileSync(
      resolve(import.meta.dirname, '../test/fixtures/run-stream.payload.b64'),
      'utf8'
    ).trim(),
    'base64'
  );
  return {
    [`GET runs/${STREAM_RUN_ID}`]: () => ({
      json: {
        run: runSummary({
          run_id: STREAM_RUN_ID,
          hero: 'Vanessa',
          victories: 10,
          losses: 3,
          final_player_rank: 'Legendary',
          final_player_rating: 938,
          final_player_rating_delta: 9
        })
      }
    }),
    [`GET runs/${STREAM_RUN_ID}/payload`]: () => ({
      body: payload,
      contentType: 'application/x-bpp-run-v6'
    })
  };
}

const ART_COLORS = ['#7a4fd0', '#2f7fb8', '#c2563a', '#3e9a6b', '#b58a2c'];

/**
 * Serves `/card-art/*`: every TemplateId in `withArt` maps to a generated image, except
 * `brokenTemplateId`, whose file answers 404 so the card falls back to its name.
 */
export async function mockCardArt(
  page: Page,
  withArt: string[],
  brokenTemplateId?: string,
  names: Record<string, string> = {}
): Promise<void> {
  const files = Object.fromEntries(
    withArt.map((id, index) => [id, `${String(index).padStart(20, '0')}.webp`])
  );
  await page.route('**/card-art/**', async (route) => {
    const file = new URL(route.request().url()).pathname.replace(
      '/card-art/',
      ''
    );
    const en = 'text-en-0123456789abcdef0123.json';
    if (file === en) {
      await route.fulfill({
        json: {
          version: 2,
          locale: 'en',
          tagLabels: { Instrument: 'Instrument' },
          enchantments: [
            {
              tags: [],
              lines: [
                {
                  id: 0,
                  available: true,
                  resolved: 'full',
                  tiers: ['Silver'],
                  segments: [{ text: 'This has +1 Multicast.' }]
                }
              ]
            }
          ],
          cards: {
            'd6c55b70-fcc7-454c-af9c-06b99d2d01e3': {
              title: 'Tambourine',
              size: 'Medium',
              tags: ['Instrument'],
              attributes: {},
              startingTier: 'Silver',
              lines: [],
              enchantments: { Shiny: 0 }
            }
          }
        }
      });
      return;
    }
    if (file === 'index.json') {
      await route.fulfill({
        json: { version: 3, game: {}, cards: files, names, text: { en } }
      });
      return;
    }
    const index = Number.parseInt(file, 10);
    if (brokenTemplateId && files[brokenTemplateId] === file) {
      await route.fulfill({ status: 404, body: '' });
      return;
    }
    const color = ART_COLORS[index % ART_COLORS.length];
    await route.fulfill({
      contentType: 'image/svg+xml',
      body: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40" preserveAspectRatio="none"><defs><radialGradient id="g"><stop offset="0" stop-color="#fff" stop-opacity=".45"/><stop offset="1" stop-color="${color}"/></radialGradient></defs><rect width="40" height="40" fill="${color}"/><circle cx="20" cy="18" r="11" fill="url(#g)"/></svg>`
    });
  });
}
