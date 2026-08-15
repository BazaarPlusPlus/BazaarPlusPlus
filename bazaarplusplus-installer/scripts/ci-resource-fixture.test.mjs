import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import { readZipEntries } from './release/payload-zip.mjs';
import { writeCiResourceFixtures } from './ci-resource-fixture.mjs';

test('source-only CI fixtures are visibly marked and cannot resemble release payloads', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-ci-fixture-'));
  try {
    const outputs = writeCiResourceFixtures(rootDir);
    expect(outputs).toHaveLength(2);
    for (const output of outputs) {
      const entries = readZipEntries(fs.readFileSync(output));
      expect(entries.map((entry) => entry.name)).toEqual([
        'CI_FIXTURE_ONLY_DO_NOT_RELEASE.txt'
      ]);
      expect(entries[0].data.toString('utf8')).toMatch(
        /not a release payload/i
      );
    }
  } finally {
    fs.rmSync(rootDir, { recursive: true, force: true });
  }
});
