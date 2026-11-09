import assert from 'node:assert/strict';
import { test } from 'vite-plus/test';
import { desktopAspectProblem } from './walkthrough.ts';

test('a 16:9 desktop window passes, with or without its title bar', () => {
  assert.equal(desktopAspectProblem(800, 478), null);
  assert.equal(desktopAspectProblem(1920, 1080), null);
});

test('any other desktop window shape is rejected with its size', () => {
  assert.match(desktopAspectProblem(1920, 480)!, /1920x480 points, not 16:9/);
  assert.match(desktopAspectProblem(1512, 600)!, /not 16:9/);
});
