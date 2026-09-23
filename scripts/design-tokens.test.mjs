// The installer owns the design tokens; the site carries a verbatim copy
// because the two projects cannot import across toolchains. This test keeps
// the copy from drifting. See docs/design.md.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const OWNER = 'bazaarplusplus-installer/src/styles/tokens.css';
const COPY = 'bazaarplusplus-site/src/styles/tokens.css';

function rootDeclarations(path) {
  const css = readFileSync(
    new URL(`../${path}`, import.meta.url),
    'utf8'
  ).replace(/\/\*[\s\S]*?\*\//g, '');
  const block = css.match(/:root\s*\{([\s\S]*?)\}/);
  assert.ok(block, `${path} has no :root block`);
  const declarations = new Map();
  for (const part of block[1].split(';')) {
    const match = part.match(/^\s*(--[\w-]+)\s*:\s*([\s\S]+?)\s*$/);
    if (match) declarations.set(match[1], match[2].replace(/\s+/g, ' '));
  }
  return declarations;
}

test('the site tokens match the installer tokens', () => {
  assert.deepEqual(
    Object.fromEntries(rootDeclarations(COPY)),
    Object.fromEntries(rootDeclarations(OWNER))
  );
});
