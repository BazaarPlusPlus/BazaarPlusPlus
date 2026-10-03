import { expect, test } from 'vitest';
import { runShell } from './shell.mjs';

test('shell environment preserves empty values, boundaries and shell syntax', () => {
  const env = {
    BPP_TEST_EMPTY: '',
    BPP_TEST_SPACES: 'path with spaces',
    BPP_TEST_SINGLE_QUOTES: "path'with'quotes",
    BPP_TEST_DOUBLE_QUOTES: 'path"with"quotes',
    BPP_TEST_SUBSTITUTION: '$(printf injected)',
    BPP_TEST_BACKTICKS: '`printf injected`',
    BPP_TEST_COMMAND: '; printf injected #',
    BPP_TEST_BACKSLASHES: 'C:\\path\\with\\backslashes',
    BPP_TEST_NEWLINE: 'line\nbreak',
    BPP_TEST_GLOB: '*?[glob]',
    BPP_TEST_OPTION: '-option'
  };

  const output = runShell(
    `printf '%s\\0' "$BPP_TEST_EMPTY" "$BPP_TEST_SPACES" \\
      "$BPP_TEST_SINGLE_QUOTES" "$BPP_TEST_DOUBLE_QUOTES" \\
      "$BPP_TEST_SUBSTITUTION" "$BPP_TEST_BACKTICKS" "$BPP_TEST_COMMAND" \\
      "$BPP_TEST_BACKSLASHES" "$BPP_TEST_NEWLINE" "$BPP_TEST_GLOB" "$BPP_TEST_OPTION"`,
    env
  );

  expect(output).toBe(
    Object.values(env)
      .map((value) => `${value}\0`)
      .join('')
  );
});
