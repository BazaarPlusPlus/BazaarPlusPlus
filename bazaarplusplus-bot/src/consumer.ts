import type { Env, Job } from './env';
import {
  type Names,
  HELP,
  UserError,
  parseCommand,
  queryWindow
} from './commands';
import { Data, builds, cardArt, heroes, names, type CardArt } from './data';
import { decodeBundle, readBounded } from './bundle';
import type { Run } from './model';
import { accepted } from './stats';
import { heroHtml, statsHtml, statsText, type ReportInput } from './report';
import { QQClient, QQError } from './qq';
import { releaseMessage } from './releases';
import type { Browser } from '@cloudflare/puppeteer';
import { openBrowser, render } from './render';

export interface Reply {
  text: string;
  html?: string;
}
/** Milliseconds from the job start to the end of each step, and the Bundle count, for the timing log. */
export type Marks = Record<string, number>;
export interface Hooks {
  mark?: (step: string, value?: number) => void;
  /** Called once the command is known to reply with an image. */
  image?: () => void;
}
export async function replyFor(
  job: Job,
  env: Env,
  signal: AbortSignal,
  { mark = () => {}, image = () => {} }: Hooks = {}
): Promise<Reply> {
  const translations: Names = await names(signal).catch(() => ({
    heroes: {},
    names: {}
  }));
  mark('names');
  const command = parseCommand(job.content, translations);
  if (command.kind === '帮助') return { text: HELP };
  if (command.kind === '状态' || command.kind === '下载')
    return { text: await releaseMessage(command.kind === '状态', signal) };
  const data = new Data(env, signal);
  if (command.kind === '解绑') return { text: await data.unbind(job.memberId) };
  const now = Date.now();
  // All pages see one fixed upload cutoff.
  const window = queryWindow(job.timestamp);
  if (command.kind === '绑定')
    return {
      text: await data.bind(job.memberId, command.name!, window.from, now)
    };
  // Without the index the board still draws each card's name.
  const art = cardArt(signal).catch((): CardArt => ({}));
  if (command.kind === '英雄') {
    image();
    const [snapshot, corpus] = await Promise.all([
      heroes(signal).catch(() => null),
      builds(signal).catch(() => null)
    ]);
    if (!snapshot) throw new UserError('英雄数据暂时无法读取，请稍后再试。');
    const hero = command.hero!;
    const rows = snapshot.days
      .flatMap((d) => d.rows)
      .filter((r) => r.hero === hero);
    return {
      text: `${translations.heroes[hero] ?? hero} · ${snapshot.window.start} 至 ${snapshot.window.end}（UTC）\n完成 ${rows.reduce((s, r) => s + r.runs.completed, 0)} 局，十胜 ${rows.reduce((s, r) => s + r.runs.ten_win, 0)} 局`,
      html: heroHtml(hero, snapshot, corpus, translations, await art)
    };
  }
  const player = await data.resolve(job.memberId, command.name);
  mark('resolve');
  image();
  const runs: Run[] = [];
  let downloaded = 0;
  let skipped = 0;
  let cursor: Awaited<ReturnType<Data['page']>>['next_after'] = null;
  do {
    const page = await data.page(player.account_id, window.from, now, cursor);
    // Up to four downloads run ahead of the one being decoded (each Bundle is at most 8 MB).
    const downloads = new Map<number, Promise<Uint8Array>>();
    const download = (i: number) => {
      const ref = page.items[i];
      if (!ref || downloads.has(i)) return;
      const bytes = fetch(ref.download_url, { signal }).then((r) =>
        readBounded(r, 8_388_607)
      );
      // A download left behind by an early exit must not raise an unhandled rejection.
      bytes.catch(() => {});
      downloads.set(i, bytes);
    };
    for (const [i, ref] of page.items.entries()) {
      signal.throwIfAborted();
      for (let ahead = i; ahead < i + 4; ahead++) download(ahead);
      const bytes = await downloads.get(i)!;
      downloads.delete(i);
      downloaded++;
      let run: Run;
      try {
        run = await decodeBundle(bytes, ref, player.account_id);
      } catch {
        skipped++;
        continue;
      }
      if (
        !accepted(run) ||
        run.ended === null ||
        run.ended < window.from ||
        run.ended >= window.before
      )
        continue;
      runs.push(run);
    }
    cursor = page.next_after;
  } while (cursor);
  mark('bundles');
  mark('count', downloaded);
  runs.sort((a, b) => a.ended! - b.ended! || a.id.localeCompare(b.id));
  if (!runs.length)
    throw new UserError(
      skipped
        ? `有 ${skipped} 份 Bundle 无法读取，暂时无法生成战绩。`
        : '这段时间没有符合条件的已上传完成局。刚结束的对局请等上传完成后再查。'
    );
  const input: ReportInput = {
    name: player.display_name,
    runs,
    from: window.from,
    before: window.before,
    names: translations,
    art: await art,
    skipped
  };
  return { text: statsText(input), html: statsHtml(input) };
}
export async function handleJob(job: Job, env: Env): Promise<void> {
  const remaining = job.timestamp + 280_000 - Date.now();
  if (remaining <= 0) return;
  const signal = AbortSignal.timeout(Math.max(1, Math.min(240_000, remaining)));
  const qq = new QQClient(env, signal);
  const started = Date.now();
  const marks: Marks = { queued: started - job.timestamp };
  const mark = (step: string, value = Date.now() - started) => {
    marks[step] = value;
  };
  const log = (outcome: string) =>
    console.log(
      JSON.stringify({
        event: 'bot.timing',
        outcome,
        ...marks,
        total: Date.now() - started
      })
    );
  const session: { browser?: Promise<Browser> } = {};
  const image = () => {
    session.browser = openBrowser(env.BROWSER);
    // A launch failure surfaces in render and falls back to text.
    session.browser.then(
      () => mark('launch'),
      () => {}
    );
  };
  try {
    let reply: Reply;
    try {
      reply = await replyFor(job, env, signal, { mark, image });
    } catch (error) {
      if (!(error instanceof UserError))
        console.error(
          JSON.stringify({
            event: 'bot.query_failed',
            error: error instanceof Error ? error.name : 'Unknown'
          })
        );
      reply = {
        text:
          error instanceof UserError
            ? error.message
            : '查询暂时失败，请稍后再试。'
      };
    }
    if (reply.html && !signal.aborted) {
      try {
        const shot = await render(
          reply.html,
          (session.browser ??= openBrowser(env.BROWSER)),
          mark
        );
        mark('render');
        mark('bytes', shot.length);
        await qq.image(job, shot);
        mark('upload');
        log('image');
        return;
      } catch (error) {
        if (error instanceof QQError && error.code === 40054005) return;
        console.error(
          JSON.stringify({
            event: 'bot.image_failed',
            code: error instanceof QQError ? error.code : null
          })
        );
      }
    }
    // Leave time for a text fallback even when calculating the image timed out.
    if (Date.now() >= job.timestamp + 290_000) return;
    const fallback = new QQClient(env, AbortSignal.timeout(8_000));
    try {
      await fallback.text(job, reply.text);
      log('text');
    } catch (error) {
      if (!(error instanceof QQError && error.code === 40054005)) throw error;
    }
  } finally {
    await session.browser?.then((b) => b.close()).catch(() => {});
  }
}
export default {
  async queue(batch: MessageBatch<Job>, env: Env): Promise<void> {
    for (const message of batch.messages) {
      try {
        await handleJob(message.body, env);
        message.ack();
      } catch (error) {
        console.error(
          JSON.stringify({
            event: 'bot.reply_failed',
            code: error instanceof QQError ? error.code : null
          })
        );
        if (Date.now() < message.body.timestamp + 270_000)
          message.retry({ delaySeconds: 5 });
        else message.ack();
      }
    }
  }
};
