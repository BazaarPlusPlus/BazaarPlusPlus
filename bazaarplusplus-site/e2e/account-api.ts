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
    game_mode: 'Ranked',
    ended_at_ms: ENDED_AT_MS,
    has_detail: true,
    ...overrides
  };
}

const RATINGS = [
  1104, 1110, 1098, 1121, 1135, 1129, 1142, 1150, 1138, 1156, 1163, 1149, 1170,
  1176, 1168, 1181, 1189, 1177, 1192, 1200, 1186, 1194, 1206, 1199, 1210, 1196,
  1203, 1214, 1200, 1218
];

export const UNLINKED_ME = {
  email: 'player@example.test',
  game: null,
  stats: {
    runs: 0,
    victories: 0,
    ten_win_runs: 0,
    heroes: [],
    rating: []
  }
};

export const LINKED_ME = {
  email: 'player@example.test',
  game: { user_name: 'SamplePlayer', linked_at_ms: Date.UTC(2026, 10, 18) },
  stats: {
    runs: 42,
    victories: 214,
    ten_win_runs: 6,
    heroes: [
      { hero: 'Stelle', runs: 21, avg_victories: 5.62, best_victories: 10 },
      { hero: 'Hero8', runs: 12, avg_victories: 4.75, best_victories: 10 },
      { hero: 'Vanessa', runs: 9, avg_victories: 4.11, best_victories: 8 }
    ],
    rating: RATINGS.map((rating, index) => ({
      ended_at_ms: ENDED_AT_MS - (RATINGS.length - index) * 3_600_000,
      rating,
      rank: 'Gold'
    }))
  }
};

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
    if (file === 'index.json') {
      await route.fulfill({
        json: { version: 1, game: {}, cards: files, names }
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
      body: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40"><defs><radialGradient id="g"><stop offset="0" stop-color="#fff" stop-opacity=".45"/><stop offset="1" stop-color="${color}"/></radialGradient></defs><rect width="40" height="40" fill="${color}"/><circle cx="20" cy="18" r="11" fill="url(#g)"/></svg>`
    });
  });
}
