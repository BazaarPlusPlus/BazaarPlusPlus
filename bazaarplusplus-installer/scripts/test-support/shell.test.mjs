import { expect, test } from 'vitest';
import { runShell } from './shell.mjs';

test('shell arguments preserve empty values, boundaries and shell syntax', () => {
  const args = [
    '',
    'path with spaces',
    "path'with'quotes",
    'path"with"quotes',
    '$(printf injected)',
    '`printf injected`',
    '; printf injected #',
    'C:\\path\\with\\backslashes',
    'line\nbreak',
    '*?[glob]',
    '-option'
  ];

  expect(runShell('printf "%s\\0" "$@"', args)).toBe(
    args.map((arg) => `${arg}\0`).join('')
  );
});
