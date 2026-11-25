// @vitest-environment node

import {
  afterEach,
  beforeEach,
  describe,
  expect,
  test,
  vi
} from 'vite-plus/test';
import worker from '../worker';

function context() {
  const pending: Array<Promise<unknown>> = [];
  return {
    pending,
    waitUntil: (promise: Promise<unknown>) => void pending.push(promise)
  };
}

function bindings() {
  return {
    ACCOUNT_API: { fetch: vi.fn() },
    ASSETS: { fetch: vi.fn() }
  };
}

test.each([
  ['/api/login', 'ACCOUNT_API'],
  ['/records', 'ASSETS']
] as const)(
  '%s forwards the original request and response through %s',
  async (path, target) => {
    const request = new Request('https://site.example' + path, {
      method: 'POST',
      body: '{}'
    });
    const response = new Response(null, {
      headers: { 'Set-Cookie': 'session=test; HttpOnly' }
    });
    const env = bindings();
    env[target].fetch.mockResolvedValue(response);
    expect(await worker.fetch(request, env, context())).toBe(response);
    expect(env[target].fetch.mock.calls[0][0]).toBe(request);
    expect(
      env[target === 'ASSETS' ? 'ACCOUNT_API' : 'ASSETS'].fetch
    ).not.toHaveBeenCalled();
  }
);

async function get(path: string, method = 'GET') {
  const ctx = context();
  const env = bindings();
  const response = await worker.fetch(
    new Request('https://site.example' + path, { method }),
    env,
    ctx
  );
  await Promise.all(ctx.pending);
  expect(env.ASSETS.fetch).not.toHaveBeenCalled();
  expect(env.ACCOUNT_API.fetch).not.toHaveBeenCalled();
  return response;
}

describe('Card Art proxy', () => {
  const store = new Map<string, Response>();
  const upstream = vi.fn<typeof fetch>();

  beforeEach(() => {
    store.clear();
    upstream.mockReset();
    vi.stubGlobal('fetch', upstream);
    vi.stubGlobal('caches', {
      default: {
        match: async (key: Request) => store.get(key.url)?.clone(),
        put: async (key: Request, response: Response) =>
          void store.set(key.url, response)
      }
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test.each([
    [
      '/card-art/0123456789abcdef0123.webp',
      'image/webp',
      'public, max-age=31536000, immutable'
    ],
    ...['en', 'zh'].map((locale) => [
      `/card-art/text-${locale}-0123456789abcdef0123.json`,
      'application/json',
      'public, max-age=31536000, immutable'
    ]),
    [
      '/card-art/index.json',
      'application/json',
      'public, max-age=3600, stale-while-revalidate=86400'
    ]
  ])(
    '%s is fetched once and then served from the cache',
    async (path, type, cacheControl) => {
      upstream.mockResolvedValue(
        new Response('body', {
          headers: { 'Content-Type': type, Vary: 'Origin' }
        })
      );

      const first = await get(path + '?v=1');
      const second = await get(path + '?v=2');
      for (const response of [first, second]) {
        expect(response.status).toBe(200);
        expect(response.headers.get('Cache-Control')).toBe(cacheControl);
        expect(response.headers.get('Content-Type')).toBe(type);
        expect(response.headers.get('Vary')).toBeNull();
      }
      expect([await first.text(), await second.text()]).toEqual([
        'body',
        'body'
      ]);
      expect(upstream).toHaveBeenCalledTimes(1);
      expect(upstream.mock.calls[0][0] as string).toBe(
        'https://cdn.bazaarplusplus.com' + path
      );
    }
  );

  test('a missing file is neither cached nor marked immutable', async () => {
    upstream.mockResolvedValue(new Response('nope', { status: 404 }));
    const response = await get('/card-art/0123456789abcdef0123.webp');
    expect(response.status).toBe(404);
    expect(response.headers.get('Cache-Control')).toBe('no-store');
    expect(store.size).toBe(0);
  });

  test('an upstream failure is a 502', async () => {
    upstream.mockResolvedValue(new Response('', { status: 503 }));
    expect((await get('/card-art/index.json')).status).toBe(502);
    expect(store.size).toBe(0);
  });

  test.each([
    ['/card-art/', 'GET'],
    ['/card-art/sub/index.json', 'GET'],
    ['/card-art/text-en-latest.json', 'GET'],
    ['/card-art/text-fr-0123456789abcdef0123.json', 'GET'],
    ['/card-art/other.json', 'GET'],
    ['/card-art/index.json', 'POST']
  ])('%s %s is a 404 without an upstream request', async (path, method) => {
    expect((await get(path, method)).status).toBe(404);
    expect(upstream).not.toHaveBeenCalled();
  });
});
