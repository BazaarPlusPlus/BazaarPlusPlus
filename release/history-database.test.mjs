import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vitest';

import {
  assertHistoryDatabaseCompatibility,
  assertWorkspaceHistoryDatabaseCompatibility
} from './history-database.mjs';
import { WORKSPACE_ROOT } from './product.mjs';

const roots = [];

afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

function fixture({ userVersion, supported }) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-history-db-'));
  roots.push(root);
  const contractPath = path.join(root, 'history-database.json');
  const compatibilityPath = path.join(root, 'compatibility.json');
  fs.writeFileSync(
    contractPath,
    JSON.stringify({
      formatVersion: 1,
      historyDatabaseUserVersion: userVersion,
      historyRowSchemaVersion: 1
    })
  );
  fs.writeFileSync(
    compatibilityPath,
    JSON.stringify({ formatVersion: 1, supportedUserVersions: supported })
  );
  return { contractPath, compatibilityPath };
}

test('the workspace mod schema is the newest the installer supports', () => {
  expect(
    assertWorkspaceHistoryDatabaseCompatibility(WORKSPACE_ROOT)
  ).toBeGreaterThan(0);
});

test('accepts a mod schema equal to the newest supported version', () => {
  expect(
    assertHistoryDatabaseCompatibility(
      fixture({ userVersion: 3, supported: [1, 2, 3] })
    )
  ).toBe(3);
});

test('rejects a mod schema the installer cannot read', () => {
  expect(() =>
    assertHistoryDatabaseCompatibility(
      fixture({ userVersion: 4, supported: [1, 2, 3] })
    )
  ).toThrow(/database schema 4[\s\S]*supports 1,2,3/);
});

test('rejects an installer that claims a schema the mod never wrote', () => {
  expect(() =>
    assertHistoryDatabaseCompatibility(
      fixture({ userVersion: 2, supported: [1, 2, 3] })
    )
  ).toThrow(/database schema 2[\s\S]*supports 1,2,3/);
});

test.each([
  [[], 'empty'],
  [[2, 1], 'unsorted'],
  [[1, 1], 'duplicated'],
  [[0, 1], 'non-positive']
])('rejects %j supported versions (%s)', (supported) => {
  expect(() =>
    assertHistoryDatabaseCompatibility(fixture({ userVersion: 1, supported }))
  ).toThrow(/Invalid installer history database compatibility contract/);
});

test('rejects an invalid mod contract', () => {
  expect(() =>
    assertHistoryDatabaseCompatibility(
      fixture({ userVersion: 0, supported: [1] })
    )
  ).toThrow(/Invalid BazaarPlusPlus history database contract/);
});
