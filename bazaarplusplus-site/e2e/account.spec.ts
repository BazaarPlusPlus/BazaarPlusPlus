import { expect, test } from '@playwright/test';

test('a signed-out player signs in, pages through records and confirms unbinding', async ({
  page
}) => {
  let signedIn = false;
  let unbindRequests = 0;
  const email = 'player@example.test';
  await page.route('**/api/login/start', async (route) => {
    expect(route.request().postDataJSON()).toEqual({ email });
    await route.fulfill({ json: {} });
  });
  await page.route('**/api/login/confirm', async (route) => {
    expect(route.request().postDataJSON()).toEqual({ email, code: '012345' });
    signedIn = true;
    await route.fulfill({ json: {} });
  });
  await page.route('**/api/me*', async (route) => {
    if (!signedIn) {
      await route.fulfill({ status: 401 });
      return;
    }
    const cursor = new URL(route.request().url()).searchParams.get('cursor');
    expect([null, 'older+run']).toContain(cursor);
    await route.fulfill({
      json: {
        user_name: 'Player One',
        email_masked: 'p***@example.test',
        next_cursor: cursor ? null : 'older+run',
        runs: [
          {
            run_id: cursor ?? 'new',
            hero: cursor ? 'UnknownHero' : 'Hero8',
            victories: 10,
            losses: 2,
            final_player_rating: 1506,
            final_player_rating_delta: 6,
            final_player_rank: 'Legendary',
            ended_at_ms: 0,
            game_mode: 'Ranked'
          }
        ]
      }
    });
  });
  await page.route('**/api/unbind', async (route) => {
    unbindRequests++;
    signedIn = false;
    await route.fulfill({ json: {} });
  });
  await page.goto('/records?lang=en');
  await expect(page).toHaveURL(/\/login\?lang=en$/);
  await page.getByLabel('Bound email address').fill(email);
  await page.getByRole('button', { name: 'Send code' }).click();
  await expect(page.getByRole('status')).toContainText(
    'If this email is bound'
  );
  await page.getByLabel('Six-digit code').fill('012345');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('main')).toContainText('Player One');
  await expect(page.getByRole('cell', { name: 'TheDragons' })).toBeVisible();
  await expect(page.getByRole('cell', { name: '1506 (+6)' })).toBeVisible();
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByRole('row')).toHaveCount(3);
  await expect(page.getByRole('cell', { name: 'UnknownHero' })).toBeVisible();
  await page.getByRole('button', { name: 'Unbind account' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText(
    'permanently deletes your stored run summaries'
  );
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  expect(unbindRequests).toBe(0);
  await page.getByRole('button', { name: 'Unbind account' }).click();
  await dialog.getByRole('button', { name: 'Unbind account' }).click();
  await expect(page).toHaveURL(/\/login\?lang=en$/);
  expect(unbindRequests).toBe(1);
});

test('bound players with no records can sign out', async ({ page }) => {
  await page.route('**/api/me', (route) =>
    route.fulfill({
      json: {
        user_name: 'Player',
        email_masked: 'p***',
        runs: [],
        next_cursor: null
      }
    })
  );
  await page.route('**/api/logout', (route) => route.fulfill({ status: 204 }));
  await page.goto('/records');
  await expect(page.getByRole('main')).toContainText('历史面板绑定邮箱');
  await page.getByRole('button', { name: '退出登录' }).click();
  await expect(page).toHaveURL(/\/login$/);
});
