import { Buffer } from 'node:buffer';
import { env } from 'cloudflare:test';
import { afterEach, expect, test, vi } from 'vitest';
import encoded from '../../bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64?raw';
import { decodeBundle, digest } from '../src/bundle';
import { replyFor, handleJob } from '../src/consumer';
import type { BundleRef } from '../src/model';
import type { Browser } from '@cloudflare/puppeteer';
import { openBrowser, render } from '../src/render';
vi.mock('../src/render', () => ({
  openBrowser: vi.fn(async () => ({ close: vi.fn() })),
  render: vi.fn(async () => new Uint8Array([1, 2, 3]))
}));
afterEach(() => {
  vi.restoreAllMocks();
  vi.mocked(render).mockReset();
  vi.mocked(openBrowser).mockClear();
});
async function fixture() {
  const payload = Buffer.from(encoded.trim(), 'base64');
  const manifest = {
    bundle_version: 6,
    bundle_id: '01J00000000000000000000902',
    run: {
      run_id: 'payload-run',
      player_account_id: 'golden-account',
      run_format_version: 6,
      payload: {
        offset: 0,
        length: payload.length,
        sha256: await digest(payload),
        content_type: 'application/x-bpp-run-v6'
      }
    }
  };
  const header = new TextEncoder().encode(JSON.stringify(manifest));
  const bytes = new Uint8Array(16 + header.length + payload.length);
  bytes.set(new TextEncoder().encode('BPPBNDL6'));
  const view = new DataView(bytes.buffer);
  view.setUint32(8, 6);
  view.setUint32(12, header.length);
  bytes.set(header, 16);
  bytes.set(payload, 16 + header.length);
  const ref: BundleRef = {
    bundle_id: manifest.bundle_id,
    run_id: 'payload-run',
    sha256: await digest(bytes),
    download_url: 'https://r2.example.test/bundle',
    stored_at_ms: Date.now()
  };
  return { bytes, ref };
}
test('validates the complete Bundle, segment digests, bounds and identities before projecting', async () => {
  const f = await fixture();
  expect((await decodeBundle(f.bytes, f.ref, 'golden-account')).hero).toBe(
    'Vanessa'
  );
  await expect(decodeBundle(f.bytes, f.ref, 'other')).rejects.toThrow(
    'identity'
  );
  const corrupt = f.bytes.slice();
  corrupt[corrupt.length - 1] ^= 1;
  await expect(decodeBundle(corrupt, f.ref, 'golden-account')).rejects.toThrow(
    'digest'
  );
  await expect(
    decodeBundle(
      corrupt,
      { ...f.ref, sha256: await digest(corrupt) },
      'golden-account'
    )
  ).rejects.toThrow('Segment');
});
test('selects Runs by end time even when their Bundle was uploaded later', async () => {
  const now = Date.parse('2026-08-04T04:00:00Z');
  vi.spyOn(Date, 'now').mockReturnValue(now);
  const f = await fixture();
  await env.DB.prepare(
    "INSERT INTO bindings VALUES ('user','golden-account','玩家')"
  ).run();
  let candidateEnd = '';
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (url) => {
    const address =
      typeof url === 'string' ? url : url instanceof URL ? url.href : url.url;
    if (address.includes('/bot/bundles')) {
      candidateEnd =
        new URL(address).searchParams.get('stored_before_ms') ?? '';
      return Response.json({ items: [f.ref], next_after: null });
    }
    if (address === f.ref.download_url)
      return new Response(f.bytes.slice().buffer);
    if (address.includes('zh-names'))
      return Response.json({ heroes: {}, names: {} });
    return new Response('Unavailable', { status: 503 });
  });
  const reply = await replyFor(
    {
      messageId: 'm',
      groupId: 'g',
      memberId: 'user',
      content: '战绩',
      timestamp: now
    },
    env,
    AbortSignal.timeout(10_000)
  );
  expect(candidateEnd).toBe(String(now));
  expect(reply.text).toContain('完成 1 局');
  expect(reply.html).toContain('玩家');
});
test('a failed image upload falls back to text with the same message ID and sequence', async () => {
  vi.mocked(render).mockResolvedValue(new Uint8Array([1, 2, 3]));
  const bodies: Record<string, unknown>[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
    const address =
      typeof url === 'string' ? url : url instanceof URL ? url.href : url.url;
    if (address.includes('zh-names'))
      return Response.json({ heroes: { Vanessa: '瓦内莎' }, names: {} });
    if (address.includes('heroes/latest'))
      return Response.json({
        window: { start: '2026-08-01', end: '2026-08-03' },
        days: []
      });
    if (address.includes('builds/latest'))
      return new Response('', { status: 503 });
    if (address.includes('getAppAccessToken'))
      return Response.json({ access_token: 'token', expires_in: 7200 });
    if (address.endsWith('/files'))
      return Response.json({ code: 40093002 }, { status: 429 });
    bodies.push(JSON.parse(init?.body as string));
    return Response.json({ id: 'sent' });
  });
  await handleJob(
    {
      messageId: 'm',
      groupId: 'g',
      memberId: 'u',
      content: '英雄 Vanessa',
      timestamp: Date.now()
    },
    env
  );
  expect(render).toHaveBeenCalledOnce();
  expect(bodies).toEqual([
    expect.objectContaining({ msg_id: 'm', msg_seq: 1, msg_type: 0 })
  ]);
});
test('a query that fails after the browser started still closes it', async () => {
  const close = vi.fn();
  vi.mocked(openBrowser).mockResolvedValueOnce({
    close
  } as unknown as Browser);
  await env.DB.prepare(
    "INSERT INTO bindings VALUES ('bound','golden-account','玩家')"
  ).run();
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (url) => {
    const address =
      typeof url === 'string' ? url : url instanceof URL ? url.href : url.url;
    if (address.includes('zh-names'))
      return Response.json({ heroes: {}, names: {} });
    if (address.includes('getAppAccessToken'))
      return Response.json({ access_token: 'token', expires_in: 7200 });
    if (address.includes('/bot/bundles'))
      return Response.json({ items: [], next_after: null });
    if (address.includes('card-art')) return new Response('', { status: 503 });
    return Response.json({ id: 'sent' });
  });
  await handleJob(
    {
      messageId: 'm',
      groupId: 'g',
      memberId: 'bound',
      content: '',
      timestamp: Date.now()
    },
    env
  );
  expect(openBrowser).toHaveBeenCalledOnce();
  expect(render).not.toHaveBeenCalled();
  expect(close).toHaveBeenCalledOnce();
});
