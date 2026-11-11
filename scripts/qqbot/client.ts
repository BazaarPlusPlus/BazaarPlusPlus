// The QQ official Bot client that the qq-announce skill and the hero daily
// report share: credentials, access token, group messages and group uploads.
// Credentials come from QQ_BOT_APP_ID and QQ_BOT_APP_SECRET in the
// environment: config.toml [qqbot] through `bun scripts/workspace.ts run qqbot --`
// locally, the `qqbot` GitHub Environment in workflows.
import fs from 'node:fs';
import crypto from 'node:crypto';

const API = 'https://api.sgroup.qq.com';
const MD5_10M_BYTES = 10002432;
// Binary (file_data) uploads are limited to 5 MB; larger files use chunks.
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

/** One QQ group the bot posts to. */
export interface Group {
  name: string;
  openid: string;
}

/** groups.json: the openid of every group, and the names each sender uses. */
export interface GroupsConfig {
  test: Group;
  release: Group[];
  heroReport: string[];
}

export function loadGroups(): GroupsConfig {
  return JSON.parse(
    fs.readFileSync(new URL('./groups.json', import.meta.url), 'utf8')
  ) as GroupsConfig;
}

/** Resolves group names against every group groups.json knows. */
export function groupsNamed(config: GroupsConfig, names: string[]): Group[] {
  const known = [config.test, ...config.release];
  return names.map((name) => {
    const group = known.find((g) => g.name === name);
    if (!group) throw new Error(`no group named ${name} in groups.json`);
    return group;
  });
}

export class QQError extends Error {
  declare code: number | undefined;
  constructor(where: string, status: number, body: string) {
    super(`${where} ${status} ${body}`);
    this.code = (() => {
      try {
        return (JSON.parse(body) as { code?: number }).code;
      } catch {
        return undefined;
      }
    })();
  }
}

// The getAppAccessToken response.
interface AccessTokenResponse {
  access_token?: string;
  expires_in?: number | string;
}

// The upload_prepare response.
interface UploadPrepareResponse {
  block_size: number | string;
  upload_id: string;
  parts: { index: number; presigned_url: string }[];
  upload_config?: { retry_timeout?: number; retry_delay?: number };
}

/** The group files response; file_info goes into a msg_type 7 message. */
export interface GroupFileResponse {
  file_info: string;
}

const hash = (alg: string, buf: Buffer) =>
  crypto.createHash(alg).update(buf).digest('hex');
const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

export function createClient(env: NodeJS.ProcessEnv = process.env) {
  const appId = env.QQ_BOT_APP_ID;
  const appSecret = env.QQ_BOT_APP_SECRET;
  if (!appId || !appSecret)
    throw new Error(
      'QQ_BOT_APP_ID and QQ_BOT_APP_SECRET are not set; run through `bun scripts/workspace.ts run qqbot --` (config.toml [qqbot])'
    );

  // Tokens last 7200 s; a long multi-group send refreshes it.
  let token = { value: '', expires: 0 };
  async function authHeaders() {
    if (Date.now() > token.expires) {
      const res = (await (
        await fetch('https://bots.qq.com/app/getAppAccessToken', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ appId, clientSecret: appSecret })
        })
      ).json()) as AccessTokenResponse;
      if (!res.access_token)
        throw new Error(`access token request failed: ${JSON.stringify(res)}`);
      token = {
        value: res.access_token,
        expires: Date.now() + (Number(res.expires_in) - 120) * 1000
      };
    }
    return {
      Authorization: `QQBot ${token.value}`,
      'Content-Type': 'application/json'
    };
  }

  async function post<T>(route: string, body: object): Promise<T> {
    const res = await fetch(API + route, {
      method: 'POST',
      headers: await authHeaders(),
      body: JSON.stringify(body)
    });
    const text = await res.text();
    if (!res.ok) throw new QQError(route, res.status, text);
    return (text ? JSON.parse(text) : {}) as T;
  }

  // Chunked file upload. URL upload does not work: QQ cannot fetch from the
  // R2 CDN (40093007).
  async function uploadFile(
    group: string,
    buf: Buffer,
    name: string
  ): Promise<GroupFileResponse> {
    const prep = await post<UploadPrepareResponse>(
      `/v2/groups/${group}/upload_prepare`,
      {
        file_type: 4,
        file_size: String(buf.length),
        file_name: name,
        md5: hash('md5', buf),
        sha1: hash('sha1', buf),
        md5_10m: hash('md5', buf.subarray(0, MD5_10M_BYTES))
      }
    );
    const blockSize = Number(prep.block_size);
    const retryMs = (prep.upload_config?.retry_timeout ?? 300) * 1000;
    const delayMs = (prep.upload_config?.retry_delay ?? 1) * 1000;
    // Part indexes start at 1, not 0; the offset counts from the lowest index.
    const base = Math.min(...prep.parts.map((p) => p.index));
    for (const part of prep.parts) {
      const offset = (part.index - base) * blockSize;
      const chunk = buf.subarray(
        offset,
        Math.min(buf.length, offset + blockSize)
      );
      for (let attempt = 1; ; attempt++) {
        const put = await fetch(part.presigned_url, {
          method: 'PUT',
          body: chunk
        }).catch((e: Error) => ({ ok: false, status: e.message }));
        if (put.ok) break;
        if (attempt >= 3)
          throw new Error(
            `${name}: PUT part ${part.index} failed: ${put.status}`
          );
        await sleep(delayMs);
      }
      const deadline = Date.now() + retryMs;
      for (;;) {
        try {
          await post(`/v2/groups/${group}/upload_part_finish`, {
            upload_id: prep.upload_id,
            part_index: part.index,
            block_size: String(chunk.length),
            md5: hash('md5', chunk)
          });
          break;
        } catch (e) {
          const code = (e as QQError).code;
          if (
            ![850012, 40093001].includes(code ?? NaN) ||
            Date.now() > deadline
          )
            throw e;
          await sleep(delayMs);
        }
      }
    }
    return post<GroupFileResponse>(`/v2/groups/${group}/files`, {
      file_type: 4,
      srv_send_msg: false,
      file_name: name,
      upload_id: prep.upload_id
    });
  }

  // A png or jpg up to MAX_IMAGE_BYTES, sent as base64 binary data.
  async function uploadImage(
    group: string,
    buf: Buffer,
    name: string
  ): Promise<GroupFileResponse> {
    if (buf.length > MAX_IMAGE_BYTES)
      throw new Error(`${name} is ${buf.length} bytes, over the 5 MB limit`);
    return post<GroupFileResponse>(`/v2/groups/${group}/files`, {
      file_type: 1,
      srv_send_msg: false,
      file_name: name,
      file_data: buf.toString('base64')
    });
  }

  return {
    sendMarkdown: (group: string, content: string) =>
      post(`/v2/groups/${group}/messages`, {
        msg_type: 2,
        markdown: { content }
      }),
    sendMedia: (group: string, fileInfo: string) =>
      post(`/v2/groups/${group}/messages`, {
        msg_type: 7,
        media: { file_info: fileInfo }
      }),
    uploadFile,
    uploadImage
  };
}

/** The operator's next step for a QQ error code, when one is known. */
export function remedy(
  code: number | undefined,
  group?: string
): string | null {
  if (code === 40034105)
    return `${group ?? 'The group'}: the group owner must re-add the bot from mobile QQ and enable 「设置主动推送消息能力」 under 「设置权限」.`;
  if (code === 40093002)
    return 'The daily QQ file upload quota is used up; resume tomorrow.';
  return null;
}
