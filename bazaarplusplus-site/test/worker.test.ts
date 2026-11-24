import { expect, test, vi } from 'vite-plus/test';
import worker from '../worker';

test.each([
  ['/api/login/confirm', 'ACCOUNT_API'],
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
    const env = {
      ACCOUNT_API: { fetch: vi.fn().mockResolvedValue(response) },
      ASSETS: { fetch: vi.fn().mockResolvedValue(response) }
    };
    expect(await worker.fetch(request, env)).toBe(response);
    expect(env[target].fetch.mock.calls[0][0]).toBe(request);
    expect(
      env[target === 'ASSETS' ? 'ACCOUNT_API' : 'ASSETS'].fetch
    ).not.toHaveBeenCalled();
  }
);
