import { expect, test } from 'vitest';
import { assertSupportedVersion } from './check-toolchain.ts';

test('toolchain guard accepts the pinned Bun version', () => {
  expect(() => assertSupportedVersion('1.4.4', '1.4.4')).not.toThrow();
});

test.each(['1.4.2', '1.4.5', '1.4.4-canary'])(
  'toolchain guard rejects %s',
  (version) => {
    expect(() => assertSupportedVersion(version, '1.4.4')).toThrow(
      /requires 1\.4\.4/
    );
  }
);
