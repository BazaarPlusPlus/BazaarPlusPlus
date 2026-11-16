import type { Job, WebhookEnv } from './env';
import { signature, validSignature } from './qq';

export default {
  async fetch(request: Request, env: WebhookEnv): Promise<Response> {
    if (new URL(request.url).pathname !== '/webhook')
      return new Response('Not found', { status: 404 });
    if (request.method !== 'POST')
      return new Response('Method not allowed', { status: 405 });
    const body = await request.text();
    let payload: { op?: number; t?: string; d?: Record<string, unknown> };
    try {
      payload = JSON.parse(body);
    } catch {
      return new Response('Invalid JSON', { status: 400 });
    }
    const d = payload?.d;
    if (
      payload?.op === 13 &&
      d &&
      typeof d.plain_token === 'string' &&
      typeof d.event_ts === 'string'
    ) {
      if (request.headers.get('X-Bot-Appid') !== env.QQ_BOT_APP_ID)
        return new Response('Unauthorized', { status: 401 });
      return Response.json({
        plain_token: d.plain_token,
        signature: signature(env.QQ_BOT_APP_SECRET, d.event_ts + d.plain_token)
      });
    }
    if (!validSignature(env.QQ_BOT_APP_SECRET, request.headers, body))
      return new Response('Unauthorized', { status: 401 });
    // Event type only: the message text and member ids stay out of logs.
    console.log(JSON.stringify({ op: payload.op, t: payload.t }));
    if (payload.op === 0 && payload.t === 'GROUP_AT_MESSAGE_CREATE' && d) {
      const author = d.author as Record<string, unknown> | undefined;
      if (
        typeof d.id !== 'string' ||
        typeof d.group_openid !== 'string' ||
        typeof author?.member_openid !== 'string' ||
        typeof d.timestamp !== 'string'
      ) {
        console.log(
          JSON.stringify({
            dropped: 'fields',
            keys: Object.keys(d),
            author: author ? Object.keys(author) : null
          })
        );
        return new Response('Invalid event', { status: 400 });
      }
      const timestamp = Date.parse(d.timestamp);
      if (!Number.isFinite(timestamp))
        return new Response('Invalid timestamp', { status: 400 });
      if (
        Date.now() - timestamp < 280_000 &&
        timestamp <= Date.now() + 30_000
      ) {
        const job: Job = {
          messageId: d.id,
          groupId: d.group_openid,
          memberId: author.member_openid,
          // A bare @ may arrive without content; it asks for the sender's stats.
          content: typeof d.content === 'string' ? d.content : '',
          timestamp
        };
        // A successful ACK means the Queue accepted the job, not just waitUntil.
        await env.JOBS.send(job);
      } else console.log(JSON.stringify({ dropped: 'stale', at: d.timestamp }));
    }
    return Response.json({ op: 12 });
  }
};
