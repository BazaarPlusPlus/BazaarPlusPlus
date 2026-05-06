import { test, expect } from 'vitest';

import { buildStreamCommandArgs } from './command-args.ts';

test('buildStreamCommandArgs includes trimmed custom game path', () => {
  expect(
    buildStreamCommandArgs('  D:\\Games\\The Bazaar  ', { limit: 5 })
  ).toEqual({
    gamePath: 'D:\\Games\\The Bazaar',
    limit: 5
  });
});

test('buildStreamCommandArgs omits blank custom game path', () => {
  expect(buildStreamCommandArgs('   ', { limit: 5 })).toEqual({
    limit: 5
  });
});
