import tokens from '../../../design/tokens.json';
import { BPP_MARK_PNG_BASE64 } from './email-mark.g';

// Verification-code emails. Colours and the font stack come from
// design/tokens.json; sizes are email-specific (EMAIL below) because a mail is
// read once, on a phone as often as a desktop. Email clients drop CSS
// variables, <style>, rgba and SVG, so every value is an inline solid colour
// and the brand mark travels as an inline PNG (design/build.ts draws it).

export type VerificationEmailPurpose = 'bind' | 'login';

export interface VerificationEmail {
  subject: string;
  text: string;
  html: string;
  attachments: EmailAttachment[];
}

const colors = new Map(
  tokens.color.tokens.map((token) => [token.name, token.value.dark])
);

function color(name: string): string {
  const value = colors.get(name);
  if (value === undefined) {
    throw new Error(`design token ${name} is missing`);
  }
  return value;
}

const EMAIL = {
  width: 480,
  pad: 24,
  radius: 12,
  mark: 28,
  brand: 'font-size:17px;line-height:28px;font-weight:600',
  title: 'font-size:22px;line-height:1.35;font-weight:600',
  body: 'font-size:15px;line-height:1.7;font-weight:400',
  key: 'font-size:14px;line-height:1.5;font-weight:400',
  name: 'font-size:15px;line-height:1.4;font-weight:600',
  code: 'font-size:36px;line-height:1.2;font-weight:700;letter-spacing:0.2em;text-indent:0.2em;font-variant-numeric:tabular-nums;-webkit-user-select:all;user-select:all;cursor:text',
  meta: 'font-size:13px;line-height:1.6;font-weight:400'
};

const MARK_CID = 'bpp-mark';
const FONT_UI = tokens.type.families.ui;

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

const COPY: Record<
  VerificationEmailPurpose,
  { subject: string; title: string; intro: string }
> = {
  bind: {
    subject: '绑定验证码',
    title: '确认绑定邮箱',
    intro:
      '你正在把下面的游戏账号绑定到这个邮箱。请在游戏里输入验证码完成绑定。'
  },
  login: {
    subject: '登录验证码',
    title: '确认登录',
    intro: '你正在登录 BazaarPlusPlus 网站。请在网页上输入验证码完成登录。'
  }
};

const ACCOUNT_KEY = '游戏账号';
const VALIDITY = '10 分钟内有效，只能使用一次';
const IGNORE =
  '请勿把验证码告诉任何人。如果不是你本人操作，请忽略此邮件，你的账号不会有任何变化。';

function markAttachment(): EmailAttachment {
  return {
    disposition: 'inline',
    contentId: MARK_CID,
    filename: 'bazaarplusplus.png',
    type: 'image/png',
    content: Uint8Array.from(atob(BPP_MARK_PNG_BASE64), (char) =>
      char.charCodeAt(0)
    )
  };
}

export function renderVerificationEmail(input: {
  purpose: VerificationEmailPurpose;
  userName: string;
  code: string;
}): VerificationEmail {
  const copy = COPY[input.purpose];
  const subject = `BazaarPlusPlus ${copy.subject}：${input.code}`;
  const text = [
    copy.intro,
    '',
    `${ACCOUNT_KEY}：${input.userName}`,
    `验证码：${input.code}（${VALIDITY}）`,
    '',
    IGNORE
  ].join('\n');

  const canvas = color('bg-canvas');
  const panel = color('bg-panel');
  const pad = `${EMAIL.pad}px`;
  const html = `<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark">
<meta name="supported-color-schemes" content="dark">
<title>${escapeHtml(subject)}</title>
</head>
<body style="margin:0;padding:0;background:${canvas};font-family:${FONT_UI};color:${color('text-1')};-webkit-font-smoothing:antialiased">
<div style="display:none;max-height:0;overflow:hidden;opacity:0">验证码 ${escapeHtml(input.code)}，${VALIDITY}。</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="${canvas}" style="background:${canvas}">
<tr><td align="center" style="padding:32px 16px">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:${EMAIL.width}px">
<tr><td style="padding:0 0 16px">
<table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
<td style="padding:0 10px 0 0;vertical-align:middle"><img src="cid:${MARK_CID}" width="${EMAIL.mark}" height="${EMAIL.mark}" alt="" style="display:block;border:0"></td>
<td style="vertical-align:middle;${EMAIL.brand};color:${color('text-1')}">BazaarPlusPlus</td>
</tr></table>
</td></tr>
<tr><td bgcolor="${panel}" style="background:${panel};border:1px solid ${color('line-strong')};border-radius:${EMAIL.radius}px;padding:${pad}">
<div style="${EMAIL.title};color:${color('text-1')}">${escapeHtml(copy.title)}</div>
<div style="padding-top:8px;${EMAIL.body};color:${color('text-2')}">${escapeHtml(copy.intro)}</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:20px;border-top:1px solid ${color('line')};border-bottom:1px solid ${color('line')}"><tr>
<td style="padding:12px 0;${EMAIL.key};color:${color('text-3')}">${ACCOUNT_KEY}</td>
<td align="right" style="padding:12px 0"><span style="display:inline-block;padding:4px 12px;border-radius:999px;background:${color('bg-selected')};border:1px solid ${color('line-strong')};${EMAIL.name};color:${color('text-1')}">${escapeHtml(input.userName)}</span></td>
</tr></table>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="${canvas}" style="margin-top:20px;background:${canvas};border:1px solid ${color('line')};border-radius:8px"><tr>
<td align="center" style="padding:20px 16px 4px;${EMAIL.code};color:${color('accent')}">${escapeHtml(input.code)}</td>
</tr><tr>
<td align="center" style="padding:0 16px 16px;${EMAIL.meta};color:${color('text-3')}">${VALIDITY}</td>
</tr></table>
</td></tr>
<tr><td style="padding:16px 0 0;${EMAIL.meta};color:${color('text-3')}">${IGNORE}</td></tr>
</table>
</td></tr>
</table>
</body>
</html>
`;

  return { subject, text, html, attachments: [markAttachment()] };
}
