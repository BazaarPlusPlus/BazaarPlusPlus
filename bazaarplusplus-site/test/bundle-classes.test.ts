import { join } from 'node:path';
import { describe, expect, it } from 'vite-plus/test';
import {
  libraryClassTokens,
  libraryRoot,
  productBppTokens,
  tsxFiles,
  undefinedClasses
} from '../../design/ui-classes';

// Every class a component draws with exists in design/components/bundle.css.
describe('bpp-* classes', () => {
  it('the Web component library uses only classes bundle.css defines', () => {
    expect(
      undefinedClasses(tsxFiles(libraryRoot), libraryClassTokens, libraryRoot)
    ).toEqual([]);
  });

  it('the site uses only bpp-* classes bundle.css defines', () => {
    const src = join(import.meta.dirname, '..', 'src');
    expect(undefinedClasses(tsxFiles(src), productBppTokens, src)).toEqual([]);
  });
});
