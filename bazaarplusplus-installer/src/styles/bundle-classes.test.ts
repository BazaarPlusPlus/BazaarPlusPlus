import { join } from 'node:path';
import { describe, expect, it } from 'vite-plus/test';
import {
  productBppTokens,
  tsxFiles,
  undefinedClasses
} from '../../../packages/ui/ui-classes';

describe('bpp-* classes', () => {
  it('the installer uses only bpp-* classes a stylesheet defines', () => {
    const src = join(import.meta.dirname, '..');
    expect(
      undefinedClasses(tsxFiles(src), productBppTokens, src, [
        join(import.meta.dirname, 'index.css'),
        join(import.meta.dirname, 'shell.css')
      ])
    ).toEqual([]);
  });
});
