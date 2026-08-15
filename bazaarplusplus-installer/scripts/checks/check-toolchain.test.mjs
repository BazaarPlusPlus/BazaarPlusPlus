import { expect, test } from 'vitest';

import { assertSupportedVersion } from './check-toolchain.mjs';

test('toolchain guard accepts versions inside the declared range', () => {
  expect(() =>
    assertSupportedVersion('node', '24.16.0', '>=22.12.0 <25')
  ).not.toThrow();
  expect(() =>
    assertSupportedVersion('npm', '11.17.0', '>=10 <12')
  ).not.toThrow();
});

test('toolchain guard rejects versions below the declared minimum', () => {
  expect(() =>
    assertSupportedVersion('node', '22.11.0', '>=22.12.0 <25')
  ).toThrow(/node 22\.11\.0 is unsupported/);
});

test('toolchain guard rejects versions at the exclusive upper bound', () => {
  expect(() => assertSupportedVersion('npm', '12.0.0', '>=10 <12')).toThrow(
    /npm 12\.0\.0 is unsupported/
  );
});
