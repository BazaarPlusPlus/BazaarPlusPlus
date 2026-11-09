import assert from 'node:assert/strict';
import { test } from 'vite-plus/test';
import { dotenvValue } from './history-sample.ts';

test('the token comes from its own line, unquoted, and an empty value is absent', () => {
  const text = [
    '# comment',
    'BPP_API_BASE_URL="https://example.test"',
    "BPP_BUNDLE_SYNC_TOKEN='secret value'",
    'BPP_EMPTY='
  ].join('\n');
  assert.equal(dotenvValue(text, 'BPP_BUNDLE_SYNC_TOKEN'), 'secret value');
  assert.equal(dotenvValue(text, 'BPP_API_BASE_URL'), 'https://example.test');
  assert.equal(dotenvValue(text, 'BPP_EMPTY'), undefined);
  assert.equal(dotenvValue(text, 'MISSING'), undefined);
});
