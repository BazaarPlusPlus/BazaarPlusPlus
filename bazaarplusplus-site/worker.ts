type Binding = { fetch(request: Request): Promise<Response> };
type Env = { ACCOUNT_API: Binding; ASSETS: Binding };
type Context = { waitUntil(promise: Promise<unknown>): void };

const CARD_ART_PREFIX = '/card-art/';
const CARD_ART_ORIGIN = 'https://cdn.bazaarplusplus.com';
// Art files are content-addressed; the index names the current set.
const INDEX_CACHE = 'public, max-age=3600, stale-while-revalidate=86400';
const ART_CACHE = 'public, max-age=31536000, immutable';

function cardArtCacheControl(file: string): string | null {
  if (file === 'index.json') return INDEX_CACHE;
  if (/^text-(?:en|zh)-[0-9a-f]{20}\.json$/.test(file)) return ART_CACHE;
  if (/^(?:fx-[0-9a-f]{20}\.json|tex-[0-9a-f]{20}\.png)$/.test(file))
    return ART_CACHE;
  return /^[0-9a-f]{8,64}\.webp$/.test(file) ? ART_CACHE : null;
}

/** Serves Card Art same-origin from the CDN through the colo cache. */
async function cardArt(request: Request, ctx: Context): Promise<Response> {
  const url = new URL(request.url);
  const cacheControl = cardArtCacheControl(
    url.pathname.slice(CARD_ART_PREFIX.length)
  );
  if (request.method !== 'GET' || cacheControl === null) {
    return new Response(null, { status: 404 });
  }
  const key = new Request(url.origin + url.pathname);
  const cache = (caches as unknown as { default: Cache }).default;
  const hit = await cache.match(key);
  if (hit) return hit;

  const upstream = await fetch(CARD_ART_ORIGIN + url.pathname);
  if (upstream.status !== 200) {
    return new Response(null, {
      status: upstream.status === 404 ? 404 : 502,
      headers: { 'Cache-Control': 'no-store' }
    });
  }
  const response = new Response(upstream.body, {
    headers: {
      'Content-Type':
        upstream.headers.get('Content-Type') ?? 'application/octet-stream',
      'Cache-Control': cacheControl
    }
  });
  ctx.waitUntil(cache.put(key, response.clone()));
  return response;
}

export default {
  fetch(request: Request, env: Env, ctx: Context) {
    const { pathname } = new URL(request.url);
    if (pathname.startsWith(CARD_ART_PREFIX)) return cardArt(request, ctx);
    const binding = pathname.startsWith('/api/') ? env.ACCOUNT_API : env.ASSETS;
    return binding.fetch(request);
  }
};
