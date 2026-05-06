import { test, expect } from 'vitest';

import { createAboutPageModel } from './page-model.ts';

test('createAboutPageModel returns English copy', () => {
  const model = createAboutPageModel('en');

  expect(model.localeBadge).toBe('EN');
  expect(model.paymentModalTitle).toBe('Support the Project');
  expect(model.paymentActionLabel).toBe('WePay');
});

test('createAboutPageModel returns Chinese copy', () => {
  const model = createAboutPageModel('zh');

  expect(model.localeBadge).toBe('中');
  expect(model.paymentModalCloseLabel).toBe('关闭');
  expect(model.supporterEntrySubtitle).toBe('查看名单');
});
