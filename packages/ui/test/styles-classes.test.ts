import { describe, expect, it } from 'vite-plus/test';
import {
  libraryClassTokens,
  libraryRoot,
  undefinedClasses
} from '../ui-classes';

describe('UI kit CSS classes', () => {
  it('uses only classes defined in styles/primitives.css or styles/game.css', () => {
    expect(
      undefinedClasses(tsxFiles(), libraryClassTokens, libraryRoot)
    ).toEqual([]);
  });
});

import { tsxFiles as collectTsxFiles } from '../ui-classes';
const tsxFiles = () => collectTsxFiles(libraryRoot);
