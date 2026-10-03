import { expect, test, type Page } from '@playwright/test';

import {
  SUPPORTERS_URL,
  expectNoUnexpectedRequests,
  goldenSources,
  installGoldenRoutes,
  type GoldenRoutes,
  type SourceReply,
} from './golden';
import { settle } from './page';

let routes: GoldenRoutes;

async function openSupport(page: Page, href: string, supporters?: SourceReply) {
  routes = await installGoldenRoutes(page, goldenSources(supporters ? { supporters } : {}));
  await page.goto(href);
  await settle(page);
}

function supporterList(page: Page, heading: string) {
  return page
    .locator('section')
    .filter({ has: page.getByRole('heading', { level: 2, name: heading }) })
    .getByRole('listitem');
}

test.afterEach(() => {
  expectNoUnexpectedRequests(routes);
});

test.describe('support options', () => {
  test('English shows the WeChat Pay and Ko-fi cards with the Ko-fi link', async ({ page }) => {
    await openSupport(page, '/support?lang=en');

    await expect(page.getByText('Your support helps keep BazaarPlusPlus going.')).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'WeChat Pay' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Ko-fi' })).toBeVisible();
    const kofi = page.getByRole('link', { name: /Open Ko-fi/ });
    await expect(kofi).toHaveAttribute('href', 'https://ko-fi.com/cauyxy');
    await expect(kofi).toHaveAttribute('target', '_blank');
    await expect(kofi).toHaveAttribute('rel', 'noreferrer');
  });

  test('Chinese keeps the Ko-fi card in English', async ({ page }) => {
    await openSupport(page, '/support');

    await expect(page.getByRole('heading', { level: 2, name: '微信赞赏' })).toBeVisible();
    await expect(page.getByText('Global', { exact: true })).toBeVisible();
    await expect(page.getByText('Buy BazaarPlusPlus a drink on Ko-fi.')).toBeVisible();
    await expect(page.getByText('在 Ko-fi 上请 BazaarPlusPlus 喝一杯。')).toHaveCount(0);
    await expect(page.getByRole('link', { name: /Open Ko-fi/ })).toHaveAttribute(
      'href',
      'https://ko-fi.com/cauyxy'
    );
  });
});

test.describe('supporters', () => {
  test('lists tiers 4 to 1, then unknown tiers, and drops malformed entries (zh)', async ({
    page,
  }) => {
    await openSupport(page, '/support');

    await expect(page.getByText('感谢每一位支持 BazaarPlusPlus 的朋友。')).toBeVisible();
    await expect(page.getByText('也感谢所有未署名的支持者。')).toBeVisible();
    // e2e/fixtures/supporters.json holds one supporter per tier, so the shuffle cannot reorder.
    await expect(supporterList(page, '支持者名单')).toHaveText([
      'Tier4',
      'Tier3',
      'Tier2',
      'Tier1',
      'Tier5',
    ]);
    expect(routes.hits.supporters).toBe(1);
  });

  test('uses English copy for the roll call', async ({ page }) => {
    await openSupport(page, '/support?lang=en');

    await expect(
      page.getByText(
        'Thanks for backing BazaarPlusPlus. Your support keeps the project moving further'
      )
    ).toBeVisible();
    await expect(
      page.getByText('And thanks to everyone who supported BazaarPlusPlus without leaving a name')
    ).toBeVisible();
    await expect(supporterList(page, 'Roll call').first()).toHaveText('Tier4');
  });

  test('a failing supporter list shows the error note', async ({ page }) => {
    await openSupport(page, '/support', { status: 503 });

    await expect(page.getByText('暂时无法加载支持者名单，请稍后再试。')).toBeVisible();
    // The page asks the supporters source once and React Query retries once.
    expect(routes.hits.supporters).toBe(2);
  });

  test('an empty supporter list shows the empty note', async ({ page }) => {
    await openSupport(page, '/support', { json: [] });

    await expect(page.getByText('支持者名单还在整理中，请稍后再来看看。')).toBeVisible();
  });

  test('the supporter list is read from the static origin', async ({ page }) => {
    const request = page.waitForRequest(SUPPORTERS_URL);
    await openSupport(page, '/support');
    expect(await (await request).headerValue('accept')).toBe('application/json');
  });
});

async function openDialog(page: Page) {
  const trigger = page.getByRole('button', { name: 'Show QR code' });
  await trigger.focus();
  await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'Buy BazaarPlusPlus a drink' });
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('img', { name: 'WeChat Pay QR code' })).toBeVisible();
  expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden');
  return { trigger, dialog };
}

async function expectClosed(page: Page) {
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await page.evaluate(() => document.body.style.overflow)).toBe('');
}

test.describe('WeChat Pay dialog', () => {
  test('Escape closes the dialog, restores scrolling, and returns focus to the trigger', async ({
    page,
  }) => {
    await openSupport(page, '/support?lang=en');
    const { trigger } = await openDialog(page);

    await page.keyboard.press('Escape');

    await expectClosed(page);
    await expect(trigger).toBeFocused();
  });

  test('the close button closes the dialog', async ({ page }) => {
    await openSupport(page, '/support?lang=en');
    const { dialog } = await openDialog(page);

    await dialog.getByRole('button', { name: 'Close' }).click();

    await expectClosed(page);
  });

  test('a backdrop click closes the dialog', async ({ page }) => {
    await openSupport(page, '/support?lang=en');
    await openDialog(page);

    await page.mouse.click(8, 8);

    await expectClosed(page);
  });
});
