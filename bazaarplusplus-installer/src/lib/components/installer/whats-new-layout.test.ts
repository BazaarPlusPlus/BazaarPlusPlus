import { test, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const workspaceRoot = resolve(import.meta.dirname, '../../../..');

function readSource(relativePath: string) {
  return readFileSync(resolve(workspaceRoot, relativePath), 'utf8');
}

const headerSource = readSource('src/lib/components/installer/InstallerHeader.svelte');
const bppStepSource = readSource('src/lib/components/installer/InstallerBppStep.svelte');
const bazaarStepSource = readSource(
  'src/lib/components/installer/InstallerBazaarStep.svelte'
);
const stepSources = [bppStepSource, bazaarStepSource];

function noStepContains(needle: string) {
  return stepSources.every((source) => !source.includes(needle));
}

test('installer header no longer renders the featured whats new card', () => {
  expect(headerSource.includes('header-link-featured')).toBe(false);
  expect(headerSource.includes('查看 WhatsNew')).toBe(false);
  expect(headerSource.includes("Open What's New")).toBe(false);
});

test('step I no longer renders a whats new action rail', () => {
  expect(noStepContains('step-body step-body-bpp')).toBe(true);
  expect(noStepContains('step-bpp-action')).toBe(true);
  expect(noStepContains('step-bpp-content')).toBe(true);
  expect(noStepContains('mismatch-link-button')).toBe(true);
  expect(noStepContains("What's New")).toBe(true);
});

test('version mismatch section removes the old inline link copy', () => {
  expect(noStepContains('查看更新内容')).toBe(true);
  expect(noStepContains("View what's new")).toBe(true);
});

test('version mismatch pills are stacked vertically', () => {
  expect(bppStepSource).toMatch(
    /\.mismatch-versions\s*\{[^}]*display:\s*flex;[^}]*flex-direction:\s*column;/
  );
});

test('latest-version state removes the green status frame and uses the latest-state copy', () => {
  expect(
    bppStepSource.includes(
      `<span class="tag tag-ok">{t('statusInstalled')}{env?.bpp_version ? \` · v\${env?.bpp_version}\` : ''}</span>`
    )
  ).toBe(false);
  expect(bppStepSource.includes('modInstalledHint')).toBe(false);
  expect(
    bppStepSource.includes('BazaarPlusPlus 当前已处于最新状态。')
  ).toBe(false);
  expect(bppStepSource.includes('BazaarPlusPlus 当前已处于最新状态')).toBe(true);
  expect(bppStepSource.includes('{:else if modInstalled}')).toBe(true);
});

test('bazaar found UI requires a non-empty effective path', () => {
  expect(
    bazaarStepSource.includes(
      'class:step-found={bazaarFound && Boolean(effectiveGamePath)}'
    )
  ).toBe(true);
  expect(
    bazaarStepSource.includes('{#if bazaarFound && effectiveGamePath}')
  ).toBe(true);
});
