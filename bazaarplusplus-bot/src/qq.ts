import { createPrivateKey, createPublicKey, sign, verify } from 'node:crypto';
import { Buffer } from 'node:buffer';
import type { Job } from './env';

// QQ's published Ed25519 seed derivation and validation vectors:
// https://bot.q.qq.com/wiki/develop/api-v2/dev-prepare/interface-framework/sign.html
function privateKey(secret: string) {
  if (!secret) throw new Error('Missing QQ bot secret');
  let seed = Buffer.from(secret);
  while (seed.length < 32) seed = Buffer.concat([seed, seed]);
  return createPrivateKey({
    key: Buffer.concat([
      Buffer.from('302e020100300506032b657004220420', 'hex'),
      seed.subarray(0, 32)
    ]),
    format: 'der',
    type: 'pkcs8'
  });
}
export function signature(secret: string, value: string): string {
  return sign(null, Buffer.from(value), privateKey(secret)).toString('hex');
}
export function validSignature(
  secret: string,
  headers: Headers,
  body: string
): boolean {
  const sig = headers.get('X-Signature-Ed25519') ?? '';
  const timestamp = headers.get('X-Signature-Timestamp');
  return (
    !!timestamp &&
    /^[0-9a-f]{128}$/i.test(sig) &&
    verify(
      null,
      Buffer.from(timestamp + body),
      createPublicKey(privateKey(secret)),
      Buffer.from(sig, 'hex')
    )
  );
}
export class QQError extends Error {
  constructor(
    readonly code: number,
    readonly status: number
  ) {
    super(`QQ request failed (${status}, ${code})`);
  }
}
export class QQClient {
  private token = '';
  private expires = 0;
  constructor(
    private readonly credentials: {
      QQ_BOT_APP_ID: string;
      QQ_BOT_APP_SECRET: string;
    },
    private readonly signal: AbortSignal
  ) {}
  private async post(
    path: string,
    body: object
  ): Promise<Record<string, unknown>> {
    if (Date.now() >= this.expires) {
      const response = await fetch(
        'https://bots.qq.com/app/getAppAccessToken',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          signal: this.signal,
          body: JSON.stringify({
            appId: this.credentials.QQ_BOT_APP_ID,
            clientSecret: this.credentials.QQ_BOT_APP_SECRET
          })
        }
      );
      const data = await response.json<{
        access_token?: string;
        expires_in?: number;
      }>();
      if (!response.ok || !data.access_token)
        throw new QQError(0, response.status);
      this.token = data.access_token;
      this.expires = Date.now() + (Number(data.expires_in) - 120) * 1000;
    }
    const response = await fetch(`https://api.sgroup.qq.com${path}`, {
      method: 'POST',
      headers: {
        Authorization: `QQBot ${this.token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(body),
      signal: this.signal
    });
    const data = await response.json<Record<string, unknown>>();
    if (!response.ok)
      throw new QQError(Number(data.code ?? 0), response.status);
    return data;
  }
  async text(job: Job, content: string): Promise<void> {
    await this.post(`/v2/groups/${encodeURIComponent(job.groupId)}/messages`, {
      msg_type: 0,
      content,
      msg_id: job.messageId,
      msg_seq: 1
    });
  }
  async image(job: Job, image: Uint8Array): Promise<void> {
    if (image.length > 5 * 1024 * 1024)
      throw new Error('Report image exceeds QQ upload limit');
    const file = await this.post(
      `/v2/groups/${encodeURIComponent(job.groupId)}/files`,
      {
        file_type: 1,
        srv_send_msg: false,
        file_data: Buffer.from(image).toString('base64')
      }
    );
    if (typeof file.file_info !== 'string')
      throw new Error('QQ upload returned no image');
    await this.post(`/v2/groups/${encodeURIComponent(job.groupId)}/messages`, {
      msg_type: 7,
      media: { file_info: file.file_info },
      msg_id: job.messageId,
      msg_seq: 1
    });
  }
}
