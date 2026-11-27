import { join } from 'node:path';
import { describe, expect, it } from 'vite-plus/test';
import {
  productBppTokens,
  tsxFiles,
  undefinedClasses
} from '../../packages/ui/ui-classes';

describe('bpp-* classes', () => {
  it('the UI documentation site uses only bpp-* classes a stylesheet defines', () => {
    const src = join(import.meta.dirname, '..', 'src');
    expect(undefinedClasses(tsxFiles(src), productBppTokens, src)).toEqual([]);
  });
});
