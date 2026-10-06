import { expect, test, vi } from 'vitest';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

import { runVerification, verificationSteps, parseCliArgs } from './verify.mjs';

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

test('source verification excludes bundle resources in child processes without changing release configuration', () => {
  const config = {
    bundle: { resources: ['missing-release.zip'], active: true },
    identifier: 'com.example.fixture'
  };
  vi.stubEnv('TAURI_CONFIG', JSON.stringify(config));
  try {
    const observed = [];
    expect(
      runVerification({
        rootDir: process.cwd(),
        summaryPath: null,
        mode: 'source',
        log() {},
        run(command, args, options) {
          observed.push({
            command,
            args,
            config: JSON.parse(options.env.TAURI_CONFIG)
          });
          return { status: 0 };
        }
      })
    ).toBe(0);
    expect(
      observed.find(
        ({ command, args }) => command === 'cargo' && args.includes('clippy')
      ).config
    ).toEqual({ ...config, bundle: { ...config.bundle, resources: [] } });
    expect(
      observed.find(({ args }) => args.includes('generate:bindings:test'))
        .config.bundle.resources
    ).toEqual([]);
    expect(
      observed.find(
        ({ command, args }) => command === 'cargo' && args.includes('doc')
      ).config.bundle.resources
    ).toEqual([]);
    expect(JSON.parse(process.env.TAURI_CONFIG)).toEqual(config);
    runVerification({
      rootDir: process.cwd(),
      summaryPath: null,
      mode: 'release',
      log() {},
      run(_command, _args, options) {
        expect(JSON.parse(options.env.TAURI_CONFIG)).toEqual(config);
        return { status: 0 };
      }
    });
  } finally {
    vi.unstubAllEnvs();
  }
});

test('verification preserves the failing command status and stops', () => {
  const observed = [];
  const status = runVerification({
    rootDir: process.cwd(),
    summaryPath: null,
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

test.each([
  [{ status: 0 }, 0, 'passed'],
  [{ status: 17 }, 17, 'failed (17)'],
  [{ status: null, signal: 'SIGTERM' }, 1, 'failed (1)'],
  [{ error: new Error('spawn failed') }, 1, 'failed (1)']
])(
  'records elapsed time and outcomes while preserving the gate result: %j',
  (result, expectedStatus, outcome) => {
    const dir = mkdtempSync(path.join(tmpdir(), 'bpp-verify-timings-'));
    const summaryPath = path.join(dir, 'summary.md');
    writeFileSync(summaryPath, 'Existing summary\n');
    let tick = 0;
    const calls = [];
    const errorLog = vi.spyOn(console, 'error').mockImplementation(() => {});
    try {
      expect(
        runVerification({
          rootDir: process.cwd(),
          mode: 'source',
          summaryPath,
          now: () => tick++ * 1250,
          log() {},
          run(command, args) {
            calls.push([command, ...args]);
            return result;
          }
        })
      ).toBe(expectedStatus);
      const summary = readFileSync(summaryPath, 'utf8');
      expect(summary).toContain('Existing summary\n');
      expect(summary).toContain(`| Check formatting | 1.25 | ${outcome} |`);
      if (expectedStatus === 0) {
        expect(summary).toContain('| Run Rust Clippy | 1.25 | passed |');
        expect(summary).toContain(
          '| Generate bindings and run Rust tests | 1.25 | passed |'
        );
        expect(summary).toContain(
          '| Build production frontend | 1.25 | passed |'
        );
      } else {
        expect(calls).toHaveLength(1);
        expect(summary).not.toContain('| Run Rust Clippy |');
      }
    } finally {
      errorLog.mockRestore();
      rmSync(dir, { recursive: true, force: true });
    }
  }
);

test('an unwritable timing summary does not change a failing command status', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'bpp-verify-timings-'));
  const warning = vi.spyOn(console, 'warn').mockImplementation(() => {});
  try {
    expect(
      runVerification({
        rootDir: process.cwd(),
        mode: 'source',
        summaryPath: dir,
        log() {},
        run: () => ({ status: 17 })
      })
    ).toBe(17);
    expect(warning).toHaveBeenCalled();
  } finally {
    warning.mockRestore();
    rmSync(dir, { recursive: true, force: true });
  }
});

const unique = (steps) =>
  [...new Set(steps.map((step) => JSON.stringify(step)))].sort();

test.each(['linux', 'darwin', 'win32'])(
  'subsets cover the complete source gate once on %s',
  (platform) => {
    const options = { mode: 'source', platform };
    const full = verificationSteps(options);
    const frontend = verificationSteps({ ...options, subset: 'frontend' });
    const native = verificationSteps({ ...options, subset: 'native' });
    expect(unique([...frontend, ...native])).toEqual(unique(full));
    expect(unique(full)).toHaveLength(full.length);
    expect(unique(frontend)).toHaveLength(frontend.length);
    expect(unique(native)).toHaveLength(native.length);
    expect(frontend.some(({ command }) => command === 'cargo')).toBe(false);
    expect(
      frontend.some(({ args }) =>
        args.some((arg) => arg.startsWith('generate:bindings'))
      )
    ).toBe(false);
    expect(frontend.some(({ args }) => args.includes('build:frontend'))).toBe(
      true
    );
    expect(frontend.some(({ args }) => args.includes('docs:check'))).toBe(true);
    expect(native.some(({ args }) => args.includes('scripts'))).toBe(
      platform === 'win32'
    );
    expect(
      native.some(
        ({ args }) =>
          args.includes('build:frontend') ||
          args.includes('check:ts') ||
          args.includes('src')
      )
    ).toBe(false);
    for (const steps of [frontend, native]) {
      expect(
        steps.some(({ args }) =>
          args.includes('prebuild-check:source:after-bindings')
        )
      ).toBe(true);
    }
    for (const steps of [full, native]) {
      const generation = steps.findIndex(({ args }) =>
        args.includes('generate:bindings:test')
      );
      const diff = steps.findIndex(({ args }) =>
        args.includes('check:bindings')
      );
      expect(generation).toBeGreaterThanOrEqual(0);
      expect(diff).toBeGreaterThan(generation);
    }
  }
);

test('frontend runner works without Rust and retains test and source contract failures', () => {
  for (const failingScript of [
    'test:unit',
    'prebuild-check:source:after-bindings'
  ]) {
    const observed = [];
    expect(
      runVerification({
        rootDir: process.cwd(),
        mode: 'source',
        subset: 'frontend',
        platform: 'linux',
        summaryPath: null,
        log() {},
        run(command, args) {
          if (
            command !== 'npm' ||
            args.some((arg) => arg.startsWith('generate:bindings'))
          ) {
            throw new Error('Rust is unavailable');
          }
          observed.push(args);
          return { status: args.includes(failingScript) ? 23 : 0 };
        }
      })
    ).toBe(23);
    expect(observed.at(-1)).toContain(failingScript);
  }
});

test('only explicit source verification accepts subsets; complete release remains mandatory', () => {
  expect(parseCliArgs(['--source-only', '--subset', 'frontend'])).toMatchObject(
    { mode: 'source', subset: 'frontend' }
  );
  expect(parseCliArgs(['--source-only']).subset).toBe('all');
  for (const args of [['--subset'], ['--subset', 'unknown']]) {
    expect(() => parseCliArgs(args)).toThrow(/--subset/);
  }
  for (const subset of ['frontend', 'native']) {
    expect(() => verificationSteps({ mode: 'release', subset })).toThrow(
      /full set/
    );
  }
});
