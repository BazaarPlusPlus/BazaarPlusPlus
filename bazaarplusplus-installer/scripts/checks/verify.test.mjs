import { expect, test } from 'vitest';

import { runVerification, verificationSteps } from './verify.mjs';

test('release verification generates bindings exactly once', () => {
  const steps = verificationSteps({
    mode: 'release',
    releasePlatform: 'macos'
  });
  const bindingSteps = steps.filter(
    ({ command, args }) =>
      command === 'npm' && args.join(' ') === 'run generate:bindings:test'
  );

  expect(bindingSteps).toHaveLength(1);
  expect(
    steps.some(
      ({ command, args }) =>
        command === 'cargo' &&
        args.includes('clippy') &&
        args.includes('--locked')
    )
  ).toBe(true);
  expect(
    steps.some(
      ({ command, args }) =>
        command === 'npm' && args.includes('prebuild-check:after-bindings')
    )
  ).toBe(true);
});

test('source verification omits private release payload validation', () => {
  const steps = verificationSteps({ mode: 'source' });

  expect(
    steps.some(
      ({ command, args }) =>
        command === 'npm' &&
        args.includes('prebuild-check:source:after-bindings')
    )
  ).toBe(true);
  expect(
    steps.some(({ args }) => args.includes('prebuild-check:after-bindings'))
  ).toBe(false);
});

test('verification preserves the failing command status and stops', () => {
  const observed = [];
  const status = runVerification({
    rootDir: process.cwd(),
    mode: 'source',
    platform: 'linux',
    log() {},
    run(command, args) {
      observed.push([command, ...args].join(' '));
      return { status: args.includes('check:ts') ? 17 : 0 };
    }
  });

  expect(status).toBe(17);
  expect(observed.at(-1)).toBe('npm run check:ts');
  expect(observed.some((command) => command.includes('build:frontend'))).toBe(
    false
  );
});

test('Windows verification runs npm command shims through ComSpec', () => {
  const observed = [];
  const status = runVerification({
    rootDir: process.cwd(),
    mode: 'source',
    platform: 'win32',
    commandShell: 'C:\\Windows\\System32\\cmd.exe',
    log() {},
    run(command, args) {
      observed.push({ command, args });
      return { status: 0 };
    }
  });

  expect(status).toBe(0);
  expect(observed[0]).toEqual({
    command: 'C:\\Windows\\System32\\cmd.exe',
    args: ['/d', '/s', '/c', 'npm.cmd', 'run', 'generate:bindings:test']
  });
  expect(observed.find(({ command }) => command === 'cargo')).toBeDefined();
});
