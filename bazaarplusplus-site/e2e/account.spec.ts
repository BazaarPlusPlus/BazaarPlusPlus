import { expect, test, type Page } from '@playwright/test';

import {
  LINKED_ME,
  SAMPLE_RUN_ID,
  SAMPLE_TEMPLATE_IDS,
  SIGNED_OUT,
  STREAM_RUN_ID,
  UNLINKED_ME,
  apiError,
  mockAccountApi,
  mockCardArt,
  runDetailHandlers,
  sampleRecords,
  streamRunHandlers
} from './account-api';

const EMAIL = 'player@example.test';
const PASSWORD = 'correct horse';
// Microphone: its art answers 404, so the card shows its name.
const BROKEN_ART = 'a176e286-6309-4652-ab61-afcb7fbfa7de';

async function expectNoHorizontalScroll(page: Page) {
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth
    )
  ).toBe(true);
}

test('a signed-out player signs in with email and password', async ({
  page
}) => {
  let signedIn = false;
  const tokens: string[] = [];
  const calls = await mockAccountApi(page, {
    'GET me': () => (signedIn ? { json: LINKED_ME } : SIGNED_OUT),
    'POST login': (body) => {
      tokens.push(body?.turnstile_token ?? '');
      if (body?.password !== PASSWORD) {
        return apiError(401, 'invalid_credentials');
      }
      expect(body).toEqual({
        email: EMAIL,
        password: PASSWORD,
        turnstile_token: expect.stringMatching(/^turnstile-token-\d+$/)
      });
      signedIn = true;
      return { json: { email: EMAIL } };
    },
    'POST logout': () => {
      signedIn = false;
      return { json: { ok: true } };
    }
  });

  await page.goto('/account?lang=en');
  await expect(page).toHaveURL(/\/login\?lang=en$/);
  await expect(
    page.getByRole('link', { name: 'Create account' })
  ).toHaveAttribute('href', '/register?lang=en');
  await expect(
    page.getByRole('link', { name: 'Forgot password' })
  ).toHaveAttribute('href', '/reset?lang=en');

  await page.getByLabel('Email').fill(EMAIL);
  await page.getByLabel('Password').fill('wrong password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'The email or password is incorrect.'
  );

  // The failed sign-in spent its token; the widget reset and issued a new one.
  await expect
    .poll(() => page.evaluate(() => window.turnstileStub.resets))
    .toBe(1);
  expect(
    await page.evaluate(() => ({
      theme: window.turnstileStub.options.theme,
      appearance: window.turnstileStub.options.appearance,
      sitekey: window.turnstileStub.options.sitekey
    }))
  ).toEqual({
    theme: 'dark',
    appearance: 'interaction-only',
    sitekey: '1x00000000000000000000AA'
  });
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/account\?lang=en$/);
  expect(tokens).toHaveLength(2);
  expect(new Set(tokens).size).toBe(2);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(
    'My account'
  );
  await expect(page.getByRole('main')).toContainText(EMAIL);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login\?lang=en$/);
  expect(calls.filter((call) => call.startsWith('POST'))).toEqual([
    'POST login',
    'POST login',
    'POST logout'
  ]);
});

test('a new player registers with an emailed code', async ({ page }) => {
  let registered = false;
  await mockAccountApi(page, {
    'POST register/start': (body) => {
      expect(body).toEqual({
        email: EMAIL,
        turnstile_token: expect.stringMatching(/^turnstile-token-\d+$/)
      });
      return { json: { ok: true } };
    },
    'POST register/confirm': (body) => {
      expect(body).toEqual({
        email: EMAIL,
        code: '012345',
        password: PASSWORD
      });
      registered = true;
      return { json: { email: EMAIL } };
    },
    'GET me': () => (registered ? { json: UNLINKED_ME } : SIGNED_OUT)
  });

  await page.goto('/register');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('注册');
  await page.getByLabel('邮箱').fill(EMAIL);
  await page.getByRole('button', { name: '发送验证码' }).click();
  await expect(page.getByRole('status')).toHaveText(
    `如果 ${EMAIL} 还没有注册，验证码已发到这个邮箱，10 分钟内有效。`
  );
  await page.getByLabel('验证码').fill('012345');
  await page.getByLabel('密码', { exact: true }).fill(PASSWORD);
  await page.getByLabel('确认密码').fill('another password');
  await page.getByRole('button', { name: '注册' }).click();
  await expect(page.getByRole('alert')).toHaveText('两次输入的密码不一致。');
  expect(registered).toBe(false);

  await page.getByLabel('确认密码').fill(PASSWORD);
  await page.getByRole('button', { name: '注册' }).click();
  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByRole('button', { name: '生成绑定码' })).toBeVisible();
});

test('a player resets the password with an emailed code', async ({ page }) => {
  let reset = false;
  await mockAccountApi(page, {
    'POST password/reset/start': (body) => {
      expect(body?.turnstile_token).toMatch(/^turnstile-token-\d+$/);
      return { json: { ok: true } };
    },
    'POST password/reset/confirm': (body) => {
      if (body?.code !== '654321') {
        return apiError(400, 'invalid_verification_code');
      }
      expect(body).toEqual({
        email: EMAIL,
        code: '654321',
        password: PASSWORD
      });
      reset = true;
      return { json: { email: EMAIL } };
    },
    'GET me': () => (reset ? { json: LINKED_ME } : SIGNED_OUT)
  });

  await page.goto('/reset?lang=en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(
    'Reset password'
  );
  await page.getByLabel('Email').fill(EMAIL);
  await page.getByRole('button', { name: 'Send code' }).click();
  await page.getByLabel('Verification code').fill('000000');
  await page.getByLabel('New password').fill(PASSWORD);
  await page.getByLabel('Confirm password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Save new password' }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'The code is incorrect or expired.'
  );
  await page.getByLabel('Verification code').fill('654321');
  await page.getByRole('button', { name: 'Save new password' }).click();
  await expect(page).toHaveURL(/\/account\?lang=en$/);
});

test('an unlinked profile generates a bind code that links in the game', async ({
  page,
  context
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  let me: unknown = UNLINKED_ME;
  let codes = 0;
  await mockAccountApi(page, {
    'GET me': () => ({ json: me }),
    'POST bind-code': () => {
      codes += 1;
      return {
        json: { code: 'K7MQ2XPA', expires_at_ms: Date.now() + 10 * 60_000 }
      };
    }
  });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/account');
  await page.getByRole('button', { name: '生成绑定码' }).click();
  await expect(page.getByRole('status', { name: '生成绑定码' })).toHaveText(
    'K7MQ-2XPA'
  );
  await expect(page.getByRole('main')).toContainText(
    '在游戏内打开 对局历史 → BPP 账号，输入此码。'
  );
  await expect(page.getByRole('timer')).toHaveText(/^(10:00|9:5\d) 后过期$/);
  await page.getByRole('button', { name: '复制' }).click();
  await expect(page.getByRole('button', { name: '已复制' })).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    'K7MQ2XPA'
  );
  await expectNoHorizontalScroll(page);
  await page.screenshot({
    path: test.info().outputPath('account-bind-code-390.png'),
    fullPage: true
  });
  expect(codes).toBe(1);

  // The bind finishes in the game; returning to the tab refreshes the profile.
  me = LINKED_ME;
  await page.evaluate(() =>
    window.dispatchEvent(new Event('visibilitychange'))
  );
  await expect(page.getByRole('main')).toContainText('SamplePlayer');
});

test('a linked profile links to its records, unlinks after confirming and deletes the account', async ({
  page
}) => {
  let me: unknown = LINKED_ME;
  const calls = await mockAccountApi(page, {
    'GET me': () => ({ json: me }),
    'POST unlink': () => {
      me = UNLINKED_ME;
      return { json: { ok: true } };
    },
    'POST account/delete': (body) =>
      body?.password === PASSWORD
        ? { json: { ok: true } }
        : apiError(401, 'invalid_credentials')
  });

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/account');
  const main = page.getByRole('main');
  await expect(main).toContainText('SamplePlayer');
  await expect(main).toContainText('绑定于 2026年11月18日');
  await expect(page.getByRole('link', { name: '全部战绩 →' })).toHaveAttribute(
    'href',
    '/records'
  );
  await page.screenshot({
    path: test.info().outputPath('account-1280.png'),
    fullPage: true
  });

  await page.setViewportSize({ width: 390, height: 844 });
  await expectNoHorizontalScroll(page);
  await page.screenshot({
    path: test.info().outputPath('account-390.png'),
    fullPage: true
  });

  await page.getByRole('button', { name: '解绑' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('网站不再显示这个游戏账号的战绩');
  await dialog.getByRole('button', { name: '取消' }).click();
  expect(calls).not.toContain('POST unlink');
  await page.getByRole('button', { name: '解绑' }).click();
  await dialog.getByRole('button', { name: '解绑' }).click();
  await expect(page.getByRole('button', { name: '生成绑定码' })).toBeVisible();

  await page.getByRole('button', { name: '删除账号' }).click();
  await expect(dialog.getByRole('button', { name: '删除账号' })).toBeDisabled();
  await dialog.getByLabel('密码').fill('wrong password');
  await dialog.getByRole('button', { name: '删除账号' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('邮箱或密码不正确。');
  await dialog.getByLabel('密码').fill(PASSWORD);
  await dialog.getByRole('button', { name: '删除账号' }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test('the records page sums the last 8 days, groups Runs by day and opens one', async ({
  page
}) => {
  await mockAccountApi(page, {
    'GET me': () => ({ json: LINKED_ME }),
    'GET runs': () => ({ json: { runs: sampleRecords() } }),
    ...runDetailHandlers()
  });
  await mockCardArt(page, SAMPLE_TEMPLATE_IDS);

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/records?lang=en');
  const main = page.getByRole('main');
  await expect(main).toContainText('Ranked runs of the last 8 days');
  const totals = main.locator('dl').first();
  await expect(totals.locator('dt')).toHaveText([
    'Runs',
    'Avg wins',
    '10 wins',
    'Rating change'
  ]);
  await expect(totals.locator('dd')).toHaveText(['6', '6.2', '2', '+27']);
  await expect(
    page.getByRole('img', { name: 'Rating over 6 runs in the last 8 days' })
  ).toBeVisible();
  await expect(main).toContainText('1,218');
  // Hero table: a header row and Stelle, Vanessa, TheDragons by runs.
  const heroRows = main.getByRole('row');
  await expect(heroRows).toHaveCount(4);
  await expect(heroRows.nth(1)).toContainText(/Stelle\s*3\s*7\.0\s*1/);
  await expect(heroRows.nth(3)).toContainText(/TheDragons\s*1\s*10\.0\s*1/);

  // Days newest first; the late upload sorts by when it ended.
  const days = main.getByRole('heading', { level: 2 });
  await expect(days).toHaveCount(3);
  await expect(days.nth(0)).toHaveText('Today');
  await expect(days.nth(1)).toHaveText('Yesterday');
  const rows = main.getByRole('listitem');
  await expect(rows).toHaveCount(6);
  await expect(rows.nth(0)).toContainText('Gold victory');
  await expect(rows.nth(0)).toContainText('1218 (+22)');
  await expect(rows.nth(2)).toContainText('7–5');
  await expect(rows.nth(3)).toContainText('Diamond victory');
  await page.screenshot({
    path: test.info().outputPath('records-1280.png'),
    fullPage: true
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expectNoHorizontalScroll(page);
  await page.screenshot({
    path: test.info().outputPath('records-390.png'),
    fullPage: true
  });
  await expect(page.getByRole('link', { name: 'My account' })).toHaveAttribute(
    'href',
    '/account?lang=en'
  );

  await rows.first().getByRole('link').click();
  await expect(page).toHaveURL(
    new RegExp(`/records/${SAMPLE_RUN_ID}\\?lang=en$`)
  );
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Stelle');
  await expect(
    page.getByRole('navigation').first().locator('[aria-current="page"]')
  ).toHaveText('My records');
});

test('the records page sends an unlinked player to link, and says when 8 days hold no Runs', async ({
  page
}) => {
  let me: unknown = UNLINKED_ME;
  await mockAccountApi(page, {
    'GET me': () => ({ json: me }),
    'GET runs': () => ({ json: { runs: [] } })
  });
  await page.goto('/records');
  const main = page.getByRole('main');
  await expect(main).toContainText(
    '绑定游戏账号后，这里显示最近 8 天的排位战绩。'
  );
  await expect(main.getByRole('link', { name: '去绑定' })).toHaveAttribute(
    'href',
    '/account'
  );

  me = LINKED_ME;
  await page.reload();
  await expect(main).toContainText('最近 8 天没有排位对局。');
  await expect(main.getByRole('link', { name: '去绑定' })).toHaveCount(0);
});

test('a Run detail decodes the stored Run into a decision timeline', async ({
  page
}) => {
  await mockAccountApi(page, runDetailHandlers());
  await mockCardArt(page, SAMPLE_TEMPLATE_IDS, BROKEN_ART);

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(`/records/${SAMPLE_RUN_ID}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Stelle');
  await expect(page.getByRole('main')).toContainText('白银胜利');
  await expect(page.getByRole('main')).toContainText('8 胜 3 负');

  const steps = page.getByRole('main').locator('ol > li');
  await expect(steps).toHaveCount(7);
  await expect(page.getByText('当天决策')).toHaveCount(0);
  await expect(steps.nth(0)).toContainText('第 1 天');
  await expect(steps.nth(0)).toContainText('Pygmalien');
  await expect(steps.nth(0)).toContainText('负');
  // The first observed board has nothing to compare with.
  await expect(steps.nth(0).locator('dl')).toHaveCount(0);

  await expect(steps.nth(1)).toContainText('PvE');
  await expect(steps.nth(1)).toContainText('未记录阵容');
  await expect(steps.nth(1).locator('dl')).toHaveText('+ 新增Keen Eye');

  // Compared with day 1, the last observed board, across the Missing day 2.
  await expect(steps.nth(2).locator('dl')).toHaveText(
    [
      '+ 新增Flute · Saxophone',
      '↑ 升级Bangles 青铜→白银 · Trumpet 青铜→白银',
      '− 离场Microphone'
    ].join('')
  );
  // A PvE fight known only from its event: no board, and day 5 still compares with day 3.
  await expect(steps.nth(3)).toContainText('第 4 天');
  await expect(steps.nth(3)).toContainText('PvE');
  await expect(steps.nth(3)).toContainText('enc-tide-turner');
  await expect(steps.nth(3)).not.toContainText('未记录阵容');
  await expect(steps.nth(4).locator('dl')).toContainText(
    '− 离场Flute · Saxophone · Bangles · Trumpet · Tambourine'
  );
  await expect(steps.nth(5).locator('dl')).toContainText('Bass 白银→钻石');

  const board = steps.nth(0);
  await expect(board.getByRole('img', { name: 'ISO-Belle' })).toHaveAttribute(
    'src',
    /^\/card-art\/\d{20}\.webp$/
  );
  // The socket effect in the hand set is not a board item.
  await expect(board.getByLabel(/^Socket/)).toHaveCount(0);
  await expect(board.getByRole('img', { name: 'Microphone' })).toHaveCount(0);
  await expect(board.getByLabel('Microphone · 青铜')).toHaveText(
    'Microphone青铜'
  );

  await page.screenshot({
    path: test.info().outputPath('run-detail-1280.png'),
    fullPage: true
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expectNoHorizontalScroll(page);
  await page.screenshot({
    path: test.info().outputPath('run-detail-390.png'),
    fullPage: true
  });
});

test('a Run with a Run Message Stream groups shops, choices and fights inside its days', async ({
  page
}) => {
  test.slow();
  await mockAccountApi(page, streamRunHandlers());
  // The index names the first shop; the Run's own Battles name the cards it fought with.
  await mockCardArt(page, [], undefined, {
    '705c0d8e-8513-49ce-82e8-81782cdac316': 'Ande'
  });

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(`/records/${STREAM_RUN_ID}?lang=en`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Vanessa');
  const main = page.getByRole('main');
  const days = main.getByRole('heading', { level: 2 });
  await expect(days.first()).toHaveText('Day 1');
  const day = (n: number) =>
    main.locator('li.bpp-day-card').filter({
      has: page.getByRole('heading', { name: `Day ${n}`, exact: true })
    });
  await expect(day(1).getByText('Win', { exact: true }).first()).toBeVisible();
  // One replay for the whole Run, below the day cards.
  await expect(day(1).locator('details')).toHaveCount(0);
  const decisions = main.locator('.bpp-replay');
  await expect(
    main.getByRole('heading', { name: 'Run replay', level: 2 })
  ).toBeVisible();
  await expect(decisions.locator('.bpp-replay-day').first()).toHaveText(
    'Day 1'
  );
  const shop = decisions
    .locator('.bpp-replay-steps > li')
    .filter({ hasText: 'Visited Ande' })
    .first();
  await expect(shop).toContainText('Hour 1');
  await shop.locator('.bpp-visit-art').click();
  await expect(page.getByRole('dialog', { name: 'Ande' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(shop).toContainText('Bought');
  const shrimpArt = shop
    .locator('.bpp-decision-card')
    .getByRole('button', { name: /Mantis Shrimp/ })
    .locator('.bpp-card-chip-art');
  await expect(shrimpArt).toHaveAttribute('data-size', '1');
  const ratio = await shrimpArt.evaluate((el) => {
    const { width, height } = el.getBoundingClientRect();
    return width / height;
  });
  expect(ratio).toBe(0.5);
  await shop
    .locator('.bpp-decision-card')
    .getByRole('button', { name: /Mantis Shrimp/ })
    .click();
  await expect(
    page.getByRole('dialog', { name: 'Mantis Shrimp' })
  ).toBeVisible();
  await page.keyboard.press('Escape');
  // Each reroll is its own step with its own fee; the shop keeps its net gold.
  await expect(
    shop.locator('.bpp-decision-row').filter({ hasText: 'Rerolled' })
  ).toHaveCount(1);
  await expect(shop).toContainText('net −6 gold');
  // The encounter choice that led into the shop is a step of its own.
  await expect(
    shop.locator('.bpp-decision-row').filter({ hasText: 'Chose' }).first()
  ).toContainText('Ande');
  await expect(
    decisions
      .locator('.bpp-replay-steps > li')
      .filter({ hasText: 'Flute' })
      .filter({ hasText: 'Chose' })
      .first()
  ).toBeVisible();
  await expect(
    decisions
      .locator('.bpp-replay-steps > li')
      .filter({ hasText: 'PvP' })
      .first()
  ).toContainText('Win');
  await expect(
    decisions
      .locator('.bpp-decision-row')
      .filter({ hasText: 'Zoarcid' })
      .filter({ hasText: 'Upgraded' })
      .first()
  ).toContainText('→ Silver');
  await expect(
    page.getByRole('button', { name: 'Download all frames' })
  ).toHaveCount(0);
  await main.getByText('Raw frames', { exact: true }).click();
  await main.getByLabel('Frame sequence').selectOption('7');
  await expect(main.locator('pre')).toContainText('"RespondsToSeq": 6');
  await expect(
    main.getByRole('button', { name: 'Download bytes' })
  ).toBeVisible();
  await main.getByText('Raw frames', { exact: true }).click();
  const bought = shop.getByRole('button', {
    name: 'Bought · Mantis Shrimp',
    exact: true
  });
  await bought.click();
  const panel = decisions.locator('.bpp-moment');
  await expect(panel.locator('.bpp-moment-title')).toHaveText(
    'Day 1 · Hour 1 · Bought · Mantis Shrimp'
  );
  await expect(panel).toContainText('Chosen');
  await expect(panel.locator('[data-chosen=true]')).toContainText('2 gold');
  await bought.press('ArrowDown');
  await expect(
    shop.getByRole('button', { name: 'Rerolled', exact: true })
  ).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(bought).toBeFocused();
  await panel.getByRole('button', { name: 'Next' }).click();
  await bought.click();
  async function checkWidth(width: number) {
    await page.setViewportSize({ width, height: width === 1280 ? 900 : 844 });
    // Wide: the panel sits beside the steps; narrow: above them.
    const [box, list] = await Promise.all([
      panel.boundingBox(),
      decisions.locator('.bpp-replay-steps').boundingBox()
    ]);
    expect(box!.x > list!.x).toBe(width === 1280);
    await expectNoHorizontalScroll(page);
    expect(await panel.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(
      true
    );
    await expect(panel.locator('.bpp-board-slot')).toHaveCount(20);
    if (width === 390) {
      const face = panel.locator('.bpp-board .bpp-card-face').first();
      expect(
        await face.evaluate((el) => {
          const label = el
            .querySelector('.bpp-card-fallback')!
            .getBoundingClientRect();
          const tile = el.getBoundingClientRect();
          const board = el.closest('.bpp-board')!.getBoundingClientRect();
          return (
            label.left >= tile.left &&
            label.right <= tile.right &&
            label.top >= tile.top &&
            label.bottom <= tile.bottom &&
            tile.bottom <= board.bottom
          );
        })
      ).toBe(true);
    }
    await bought.evaluate((el) => el.scrollIntoView({ block: 'start' }));
    if (width !== 320)
      await page.screenshot({
        path: test.info().outputPath('moment-' + width + '.png')
      });
  }
  await checkWidth(1280);
  await checkWidth(390);
  await checkWidth(320);
});

test('a Run that is gone or never existed is not found', async ({ page }) => {
  await mockAccountApi(page, {});
  await page.goto('/records/missing-run?lang=en');
  await expect(page.getByRole('main')).toContainText(
    'This run wasn’t found. Runs older than 8 days are deleted.'
  );
});

test('after unlinking, a Run viewed before shows only that it is gone', async ({
  page
}) => {
  let linked = true;
  const detail = runDetailHandlers();
  await mockAccountApi(page, {
    'GET me': () => ({ json: linked ? LINKED_ME : UNLINKED_ME }),
    'GET runs': () => ({
      json: { runs: linked ? sampleRecords() : [] }
    }),
    'POST unlink': () => {
      linked = false;
      return { json: { ok: true } };
    },
    [`GET runs/${SAMPLE_RUN_ID}`]: (body, request) =>
      linked
        ? detail[`GET runs/${SAMPLE_RUN_ID}`](body, request)
        : apiError(404, 'not_found'),
    [`GET runs/${SAMPLE_RUN_ID}/payload`]: (body, request) =>
      linked
        ? detail[`GET runs/${SAMPLE_RUN_ID}/payload`](body, request)
        : apiError(404, 'not_found')
  });
  await mockCardArt(page, []);

  await page.goto(`/records/${SAMPLE_RUN_ID}?lang=en`);
  await expect(page.getByRole('main').locator('ol > li')).toHaveCount(7);
  // Client-side navigation keeps the query cache.
  await page.getByRole('link', { name: '← All runs' }).click();
  await page.getByRole('link', { name: 'My account' }).click();
  await page.getByRole('button', { name: 'Unlink' }).click();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Unlink' })
    .click();
  await expect(
    page.getByRole('button', { name: 'Generate bind code' })
  ).toBeVisible();

  await page.goBack();
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(`/records/${SAMPLE_RUN_ID}`));
  await expect(page.getByRole('main')).toContainText('This run wasn’t found.');
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(0);
  await expect(page.getByRole('main').locator('ol > li')).toHaveCount(0);
});
