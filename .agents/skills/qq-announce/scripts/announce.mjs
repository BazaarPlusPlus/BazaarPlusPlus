#!/usr/bin/env node
// Posts a published BazaarPlusPlus release to QQ groups through the QQ official Bot:
// the Markdown notes, then one file message per installer (Win, Mac, Android).
//
//   node announce.mjs --target test|release [--notes notes.md]
//                     [--platforms win,mac,android] [--groups "name,name"] [--send]
//
// --target test sends to the test group in ../groups.json; --target release to every release group.
// Without --send it prints the plan and sends nothing.
// Installers come from the published manifests on the CDN, so only promoted builds go out.
// Each platform carries its own published version (desktop and Android can differ),
// and the dry run prints it for the user to check.
// Credentials: $BPP_CONFIG_HOME/qqbot.env (default ~/.config/bazaarplusplus/qqbot.env)
// with QQ_BOT_APP_ID and QQ_BOT_APP_SECRET.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { parseArgs } from 'node:util';

const CDN = 'https://cdn.bazaarplusplus.com/installer';
const API = 'https://api.sgroup.qq.com';
const MAX_FILE_BYTES = 200 * 1024 * 1024; // QQ group file hard limit
const MD5_10M_BYTES = 10002432;

// Send order. Group display names are the QQ upload file_name, not the R2 key.
const PLATFORMS = {
  win: {
    manifest: `${CDN}/latest.json`,
    pick: (m) => m.downloads?.['windows-x86_64'],
    name: (v) => `BPP_Win_${v}.exe`
  },
  mac: {
    manifest: `${CDN}/latest.json`,
    pick: (m) => m.downloads?.['darwin-aarch64'],
    name: (v) => `BPP_Mac_${v}.dmg`
  },
  android: {
    manifest: `${CDN}/latest/android-aarch64.json`,
    pick: (m) => m.downloads?.['android-aarch64']?.app,
    name: (v) => `BPP_Android_${v}.apk`
  }
};

const { values: args } = parseArgs({
  options: {
    target: { type: 'string' },
    notes: { type: 'string' },
    platforms: { type: 'string', default: Object.keys(PLATFORMS).join(',') },
    groups: { type: 'string' },
    send: { type: 'boolean', default: false }
  }
});

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(1);
}

if (!['test', 'release'].includes(args.target))
  fail('--target must be test or release');
const wanted = args.platforms
  .split(',')
  .map((p) => p.trim())
  .filter(Boolean);
for (const p of wanted)
  if (!PLATFORMS[p]) fail(`unknown platform ${p}; use win, mac, android`);
const selected = Object.keys(PLATFORMS).filter((p) => wanted.includes(p));

const groupsFile = new URL('../groups.json', import.meta.url);
const groupsConfig = JSON.parse(fs.readFileSync(groupsFile, 'utf8'));
let groups =
  args.target === 'test' ? [groupsConfig.test] : groupsConfig.release;
if (args.groups) {
  const names = args.groups.split(',').map((n) => n.trim());
  for (const n of names)
    if (!groups.some((g) => g.name === n))
      fail(`no ${args.target} group named ${n} in groups.json`);
  groups = groups.filter((g) => names.includes(g.name));
}

async function getJson(url) {
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) fail(`${url} returned ${res.status}`);
  return res.json();
}

// Plan: each selected platform's latest published installer, named with its version.
const manifests = {};
const files = [];
for (const p of selected) {
  const spec = PLATFORMS[p];
  manifests[spec.manifest] ??= await getJson(spec.manifest);
  const manifest = manifests[spec.manifest];
  const entry = spec.pick(manifest);
  if (!entry?.url)
    fail(
      `${p}: the published ${manifest.version} manifest has no installer entry`
    );
  if (entry.size > MAX_FILE_BYTES)
    fail(`${p}: ${entry.url} is ${entry.size} bytes, over the QQ 200 MB limit`);
  files.push({
    platform: p,
    version: manifest.version,
    name: spec.name(manifest.version),
    url: entry.url,
    size: entry.size,
    sha256: entry.sha256
  });
}
const notes = args.notes ? fs.readFileSync(args.notes, 'utf8').trim() : null;
if (args.notes && !notes) fail(`${args.notes} is empty`);

console.log(`Plan → ${args.target}: ${groups.map((g) => g.name).join('、')}`);
console.log('Each group gets, in order:');
let n = 1;
if (notes)
  console.log(
    `  ${n++}. Markdown notes from ${args.notes} (${notes.length} chars)`
  );
else console.log('  -  no notes message (no --notes)');
for (const f of files)
  console.log(
    `  ${n++}. ${f.name}  ${f.platform} ${f.version}  ${(f.size / 1048576).toFixed(1)} MB  from ${f.url}`
  );
if (!args.send) {
  console.log('\nDry run: nothing sent. Rerun with --send to post.');
  process.exit(0);
}

// Credentials.
const configHome =
  process.env.BPP_CONFIG_HOME ||
  path.join(os.homedir(), '.config', 'bazaarplusplus');
const envFile = path.join(configHome, 'qqbot.env');
if (!fs.existsSync(envFile))
  fail(`${envFile} is missing (QQ_BOT_APP_ID, QQ_BOT_APP_SECRET)`);
const env = Object.fromEntries(
  fs
    .readFileSync(envFile, 'utf8')
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith('#'))
    .map((l) => [
      l.slice(0, l.indexOf('=')).trim(),
      l.slice(l.indexOf('=') + 1).trim()
    ])
);
for (const key of ['QQ_BOT_APP_ID', 'QQ_BOT_APP_SECRET'])
  if (!env[key]) fail(`${envFile} has no ${key}`);

// Tokens last 7200 s; a long multi-group send refreshes it.
let token = { value: null, expires: 0 };
async function authHeaders() {
  if (Date.now() > token.expires) {
    const res = await (
      await fetch('https://bots.qq.com/app/getAppAccessToken', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          appId: env.QQ_BOT_APP_ID,
          clientSecret: env.QQ_BOT_APP_SECRET
        })
      })
    ).json();
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

class QQError extends Error {
  constructor(where, status, body) {
    super(`${where} ${status} ${body}`);
    this.code = (() => {
      try {
        return JSON.parse(body).code;
      } catch {
        return undefined;
      }
    })();
  }
}
async function post(route, body) {
  const res = await fetch(API + route, {
    method: 'POST',
    headers: await authHeaders(),
    body: JSON.stringify(body)
  });
  const text = await res.text();
  if (!res.ok) throw new QQError(route, res.status, text);
  return text ? JSON.parse(text) : {};
}
const hash = (alg, buf) => crypto.createHash(alg).update(buf).digest('hex');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Chunked upload. URL upload does not work: QQ cannot fetch from the R2 CDN (40093007).
async function upload(group, buf, name) {
  const prep = await post(`/v2/groups/${group}/upload_prepare`, {
    file_type: 4,
    file_size: String(buf.length),
    file_name: name,
    md5: hash('md5', buf),
    sha1: hash('sha1', buf),
    md5_10m: hash('md5', buf.subarray(0, MD5_10M_BYTES))
  });
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
      }).catch((e) => ({ ok: false, status: e.message }));
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
        if (![850012, 40093001].includes(e.code) || Date.now() > deadline)
          throw e;
        await sleep(delayMs);
      }
    }
  }
  return post(`/v2/groups/${group}/files`, {
    file_type: 4,
    srv_send_msg: false,
    file_name: name,
    upload_id: prep.upload_id
  });
}

// Download and verify each installer once; every group gets its own upload,
// because a file uploaded through one group's interface is bound to group sends.
const buffers = {};
const done = []; // "group: item" lines for the resume hint
let current = null;
try {
  for (const f of files) {
    const res = await fetch(f.url);
    if (!res.ok) throw new Error(`${f.url} returned ${res.status}`);
    const buf = Buffer.from(await res.arrayBuffer());
    if (f.size && buf.length !== f.size)
      throw new Error(
        `${f.name}: downloaded ${buf.length} bytes, manifest says ${f.size}`
      );
    if (f.sha256 && hash('sha256', buf) !== f.sha256)
      throw new Error(`${f.name}: sha256 does not match the manifest`);
    buffers[f.platform] = buf;
  }
  for (const g of groups) {
    current = { group: g, sent: [] };
    if (notes) {
      await post(`/v2/groups/${g.openid}/messages`, {
        msg_type: 2,
        markdown: { content: notes }
      });
      current.sent.push('notes');
      console.log(`${g.name}: sent notes`);
    }
    for (const f of files) {
      const { file_info } = await upload(g.openid, buffers[f.platform], f.name);
      await post(`/v2/groups/${g.openid}/messages`, {
        msg_type: 7,
        media: { file_info }
      });
      current.sent.push(f.platform);
      console.log(`${g.name}: sent ${f.name}`);
    }
    done.push(g.name);
  }
} catch (e) {
  console.error(`error: ${e.message}`);
  if (e.code === 40034105)
    console.error(
      `${current?.group.name}: the group owner must re-add the bot from mobile QQ and enable 「设置主动推送消息能力」 under 「设置权限」.`
    );
  if (e.code === 40093002)
    console.error(
      'The daily QQ file upload quota is used up; resume tomorrow.'
    );
  console.error(`Fully sent: ${done.join('、') || 'no group'}.`);
  if (current) {
    const left = files
      .map((f) => f.platform)
      .filter((p) => !current.sent.includes(p));
    const notesFlag =
      notes && !current.sent.includes('notes') ? ` --notes ${args.notes}` : '';
    console.error(
      `${current.group.name} got: ${current.sent.join(', ') || 'nothing'}. Resume it with --groups "${current.group.name}" --platforms ${left.join(',')}${notesFlag}`
    );
    const rest = groups
      .slice(groups.indexOf(current.group) + 1)
      .map((g) => g.name);
    if (rest.length)
      console.error(
        `Then the untouched groups with --groups "${rest.join(',')}" and the full flags.`
      );
  }
  process.exit(1);
}
console.log(`Done: ${done.join('、')}.`);
