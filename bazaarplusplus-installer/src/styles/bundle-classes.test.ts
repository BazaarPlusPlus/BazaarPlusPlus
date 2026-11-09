import { join } from 'node:path';
import { describe, expect, it } from 'vite-plus/test';
import {
  libraryClassTokens,
  libraryRoot,
  productBppTokens,
  tsxFiles,
  undefinedClasses
} from '../../../design/ui-classes';

// Every class a component draws with exists in a stylesheet: bundle.css for
// the shared library, plus this app's index.css for the installer's own pages.
describe('bpp-* classes', () => {
  it('the Web component library uses only classes bundle.css defines', () => {
    expect(
      undefinedClasses(tsxFiles(libraryRoot), libraryClassTokens, libraryRoot)
    ).toEqual([]);
  });

  it('the installer uses only bpp-* classes a stylesheet defines', () => {
    const src = join(import.meta.dirname, '..');
    expect(
      undefinedClasses(tsxFiles(src), productBppTokens, src, [
        join(import.meta.dirname, 'index.css')
      ])
    ).toEqual([]);
  });
});
