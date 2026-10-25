import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { runGit } from './git-command.mjs';

const runner = path.join(import.meta.dirname, 'verify.mjs');
function fixture(t) {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'verify-'));
  t.after(() => fs.rmSync(cwd, { recursive: true, force: true }));
  runGit(['init', '-q'], { cwd });
  runGit(
    [
      '-c',
      'user.name=Test',
      '-c',
      'user.email=test@example.com',
      '-c',
      'core.hooksPath=/dev/null',
      'commit',
      '--allow-empty',
      '-qm',
      'initial'
    ],
    { cwd }
  );
  fs.writeFileSync(
    path.join(cwd, 'justfile'),
    `pass:\n    @echo ok\nfail:\n    @echo 'Passed!'\n    @exit 7\nafter:\n    @echo should-not-run\nwait:\n    @node wait.cjs\n`
  );
  const receipts = () =>
    fs
      .readdirSync(path.join(cwd, 'artifacts/verification'))
      .map((dir) =>
        JSON.parse(
          fs.readFileSync(
            path.join(cwd, 'artifacts/verification', dir, 'result.json'),
            'utf8'
          )
        )
      );
  return { cwd, receipts };
}

test('records the completed exit code and the repository commit', (t) => {
  const f = fixture(t);
  const run = spawnSync(process.execPath, [runner, 'pass'], {
    cwd: f.cwd,
    encoding: 'utf8'
  });
  assert.equal(run.status, 0, run.stderr);
  const [receipt] = f.receipts();
  assert.equal(
    receipt.head,
    runGit(['rev-parse', 'HEAD'], { cwd: f.cwd }).trim()
  );
  assert.equal(receipt.status, 'passed');
  assert.equal(receipt.steps[0].exitCode, 0);
  assert.match(fs.readFileSync(receipt.steps[0].logPath, 'utf8'), /ok/);
});

test('a success-looking line followed by failure stops the sequence', (t) => {
  const f = fixture(t);
  const run = spawnSync(process.execPath, [runner, 'fail', 'after'], {
    cwd: f.cwd,
    encoding: 'utf8'
  });
  assert.equal(run.status, 1);
  const [receipt] = f.receipts();
  assert.equal(receipt.status, 'failed');
  assert.equal(receipt.steps.length, 1);
  assert.notEqual(receipt.steps[0].exitCode, 0);
  assert.match(fs.readFileSync(receipt.steps[0].logPath, 'utf8'), /Passed!/);
});

test('an active process has no successful receipt until its output and process close', async (t) => {
  const f = fixture(t);
  fs.writeFileSync(
    path.join(f.cwd, 'wait.cjs'),
    `const fs=require('fs'); console.log('ready'); const timer=setInterval(()=>{if(fs.existsSync('release')){clearInterval(timer); console.log('done');}},20);`
  );
  const child = spawn(process.execPath, [runner, 'wait'], {
    cwd: f.cwd,
    stdio: ['ignore', 'pipe', 'pipe']
  });
  t.after(() => child.kill());
  const completed = new Promise((resolve) => child.on('close', resolve));
  await new Promise((resolve, reject) => {
    let output = '';
    const timer = setTimeout(
      () => reject(new Error('worker did not become ready')),
      10000
    );
    child.stdout.on('data', (data) => {
      output += data;
      if (output.includes('ready')) {
        clearTimeout(timer);
        resolve();
      }
    });
  });
  const [running] = f.receipts();
  assert.equal(running.status, 'running');
  assert.equal(running.steps[0].exitCode, null);
  fs.writeFileSync(path.join(f.cwd, 'release'), '');
  assert.equal(await completed, 0);
  assert.equal(f.receipts()[0].status, 'passed');
});
