import { env } from 'cloudflare:test';
import { afterEach, expect, test, vi } from 'vitest';
import webhook from '../src/webhook';
import { signature, validSignature } from '../src/qq';
import { Data } from '../src/data';
import { handleJob } from '../src/consumer';
import type { Job } from '../src/env';
const job: Job = {
  messageId: 'msg',
  groupId: 'group',
  memberId: 'member',
  content: '帮助',
  timestamp: Date.now()
};
afterEach(() => vi.restoreAllMocks());
test('matches QQ official challenge and signature vectors', () => {
  expect(signature('DG5g3B4j9X2KOErG', '1725442341Arq0D5A61EgUu4OxUvOp')).toBe(
    '87befc99c42c651b3aac0278e71ada338433ae26fcb24307bdc5ad38c1adc2d01bcfcadc0842edac85e85205028a1132afe09280305f13aa6909ffc2d652c706'
  );
  const headers = new Headers({
    'X-Signature-Timestamp': '1725442341',
    'X-Signature-Ed25519':
      '87befc99c42c651b3aac0278e71ada338433ae26fcb24307bdc5ad38c1adc2d01bcfcadc0842edac85e85205028a1132afe09280305f13aa6909ffc2d652c706'
  });
  expect(
    validSignature('DG5g3B4j9X2KOErG', headers, 'Arq0D5A61EgUu4OxUvOp')
  ).toBe(true);
  expect(validSignature('DG5g3B4j9X2KOErG', headers, '{}')).toBe(false);
});
test('ACKs after queue acceptance and rejects unsigned events', async () => {
  const send = vi.fn().mockResolvedValue(undefined);
  const bindings = { ...env, JOBS: { send } as unknown as Queue<Job> };
  const body = JSON.stringify({
    op: 0,
    t: 'GROUP_AT_MESSAGE_CREATE',
    d: {
      id: 'm',
      group_openid: 'g',
      author: { member_openid: 'u' },
      content: '战绩',
      timestamp: new Date().toISOString()
    }
  });
  const headers = new Headers({
    'X-Signature-Timestamp': '123',
    'X-Signature-Ed25519': signature(env.QQ_BOT_APP_SECRET, '123' + body)
  });
  expect(
    (
      await webhook.fetch(
        new Request('https://test/webhook', { method: 'POST', body }),
        bindings
      )
    ).status
  ).toBe(401);
  expect(send).not.toHaveBeenCalled();
  expect(
    await (
      await webhook.fetch(
        new Request('https://test/webhook', { method: 'POST', body, headers }),
        bindings
      )
    ).json()
  ).toEqual({ op: 12 });
  expect(send).toHaveBeenCalledWith(
    expect.objectContaining({ memberId: 'u', groupId: 'g', messageId: 'm' })
  );
  send.mockRejectedValueOnce(new Error('Queue down'));
  await expect(
    webhook.fetch(
      new Request('https://test/webhook', { method: 'POST', body, headers }),
      bindings
    )
  ).rejects.toThrow('Queue down');
});
test('bindings are independent of groups and only bound accounts are visible', async () => {
  vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
    Response.json({
      players: [{ account_id: 'account', display_name: '玩家' }]
    })
  );
  const data = new Data(env, AbortSignal.timeout(10_000));
  await expect(data.resolve('other', '玩家')).rejects.toThrow('没有绑定');
  await data.bind('member', '玩家', 1, Date.now());
  expect(await data.resolve('member')).toEqual({
    account_id: 'account',
    display_name: '玩家'
  });
  expect(await data.resolve('other', '玩家')).toEqual({
    account_id: 'account',
    display_name: '玩家'
  });
  await data.unbind('member');
  await expect(data.resolve('other', '玩家')).rejects.toThrow('没有绑定');
});
test('ambiguous names never bind silently; account fallback requires recent data', async () => {
  const mock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
    Response.json({
      players: [
        { account_id: 'a', display_name: '同名' },
        { account_id: 'b', display_name: '同名' }
      ]
    })
  );
  const data = new Data(env, AbortSignal.timeout(10_000));
  await expect(data.bind('m', '同名', 1, Date.now())).rejects.toThrow('多个');
  mock.mockImplementation(async () => Response.json({ items: [] }));
  await expect(data.bind('m', 'account:a', 1, Date.now())).rejects.toThrow(
    '没有上传'
  );
});
test('help uses passive replies and a fixed sequence; expired jobs do no work', async () => {
  const requests: { url: string; body: Record<string, unknown> }[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
    const path = url instanceof Request ? url.url : String(url);
    requests.push({
      url: path,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : {}
    });
    return Response.json(
      path.includes('getAppAccessToken')
        ? { access_token: 'token', expires_in: 7200 }
        : path.includes('zh-names')
          ? { heroes: {}, names: {} }
          : { id: 'sent' }
    );
  });
  await handleJob({ ...job, timestamp: Date.now() }, env);
  expect(requests.at(-1)?.body).toMatchObject({
    msg_id: 'msg',
    msg_seq: 1,
    msg_type: 0
  });
  const count = requests.length;
  await handleJob({ ...job, timestamp: Date.now() - 300_000 }, env);
  expect(requests).toHaveLength(count);
});
