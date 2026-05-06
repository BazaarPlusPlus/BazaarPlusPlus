import { test, expect } from 'vitest';
import fs from 'node:fs';

const packageJson = JSON.parse(
  fs.readFileSync(new URL('../package.json', import.meta.url), 'utf8')
);

test('package.json keeps the SvelteKit v2 toolchain versions', () => {
  expect(packageJson.devDependencies['@sveltejs/adapter-static']).toBe(
    '^3.0.10'
  );
  expect(packageJson.devDependencies['@sveltejs/kit']).toBe('^2.55.0');
  expect('wrangler' in packageJson.devDependencies).toBe(false);
});
