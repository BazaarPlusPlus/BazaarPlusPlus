import { test, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const workspaceRoot = resolve(import.meta.dirname, '../../../..');
const modalSource = readFileSync(
  resolve(
    workspaceRoot,
    'src/lib/components/installer/InstallerInstallPreviewModal.svelte'
  ),
  'utf8'
);

test('install preview modal points users to the latest tutorial video', () => {
  expect(
    modalSource.includes('查看 B 站 BazaarPlusPlus 最新视频获取使用教程。')
  ).toBe(true);
  expect(
    modalSource.includes(
      'Check the latest BazaarPlusPlus video on Bilibili for the usage tutorial.'
    )
  ).toBe(true);
  expect(modalSource.includes('查看最新视频')).toBe(true);
  expect(modalSource.includes('Watch Latest Video')).toBe(true);
  expect(modalSource.includes('href={bilibiliUrl}')).toBe(true);
  expect(modalSource.includes('onclick={onOpenBilibili}')).toBe(true);
});

test('install preview modal shows an installation risk disclaimer', () => {
  expect(
    modalSource.includes('我确认安装插件存在风险，并愿意自行承担相关责任')
  ).toBe(true);
  expect(
    modalSource.includes(
      'I understand that installing this plugin involves risk, and I accept responsibility for proceeding.'
    )
  ).toBe(true);
});

test('install preview modal no longer shows the old installation summary', () => {
  expect(
    modalSource.includes(
      'BazaarPlusPlus 包含几项最常用的功能：战绩记录、战斗回放、野怪预览、升级预览和附魔预览。'
    )
  ).toBe(false);
  expect(
    modalSource.includes(
      "This installation enables several of BazaarPlusPlus's most useful enhancements, including match history, battle replay, monster preview, level-up preview, and enchantment preview."
    )
  ).toBe(false);
});

test('install preview modal no longer uses feature-card headings', () => {
  expect(modalSource.includes('怪物预览增强')).toBe(false);
  expect(modalSource.includes('Enhanced Monster Preview')).toBe(false);
});
