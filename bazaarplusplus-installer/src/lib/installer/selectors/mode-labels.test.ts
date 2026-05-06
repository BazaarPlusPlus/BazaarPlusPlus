import { test, expect } from 'vitest';

import { selectModeLabels } from './mode-labels.ts';
import type { LocalizedText, TranslateText } from './types.ts';

const localized: LocalizedText = (zh, en) => en || zh;
const t: TranslateText = (key) => String(key);

test('selectModeLabels returns install-mode labels for English locale', () => {
  const selection = selectModeLabels({
    showStreamMode: false,
    locale: 'en',
    localized,
    t
  });

  expect(selection.modeTitle).toBe('subtitle');
  expect(selection.modeToggleLabel).toBe('Stream Mode');
  expect(selection.dotnetDownloadUrl).toBe(
    'https://dotnet.microsoft.com/en-us/download'
  );
  expect(selection.localeBadge).toBe('EN');
  expect(selection.localeButtonLabel).toBe('切换到中文');
});

test('selectModeLabels returns stream-mode labels for Chinese locale', () => {
  const zhLocalized: LocalizedText = (zh) => zh;
  const selection = selectModeLabels({
    showStreamMode: true,
    locale: 'zh',
    localized: zhLocalized,
    t
  });

  expect(selection.modeTitle).toBe('streamTitle');
  expect(selection.modeToggleLabel).toBe('安装模式');
  expect(selection.dotnetDownloadUrl).toBe(
    'https://dotnet.microsoft.com/zh-cn/download'
  );
  expect(selection.localeBadge).toBe('中');
  expect(selection.localeButtonLabel).toBe('Switch to English');
});
