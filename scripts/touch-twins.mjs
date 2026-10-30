// Warns, never fails, when a commit changes an Android touch surface or its Mono port without
// the other. ADR-0024 keeps the two by hand under twin names: Android<Name>.cs in the Android
// host, Touch<Name>.cs in the Mono host. Some changes belong to one side only, so this asks a
// look rather than blocking.
import fs from 'node:fs';
import path from 'node:path';

const MOD = 'bazaarplusplus-mod/src';
const SIDES = [
  { root: `${MOD}/BazaarPlusPlus.Android`, prefix: 'Android' },
  { root: `${MOD}/BazaarPlusPlus`, prefix: 'Touch' }
];

function sources(cwd, root) {
  const found = new Map();
  const walk = (directory) => {
    for (const entry of fs.readdirSync(path.join(cwd, directory), {
      withFileTypes: true
    })) {
      const file = `${directory}/${entry.name}`;
      if (entry.isDirectory()) {
        if (entry.name !== 'bin' && entry.name !== 'obj') walk(file);
      } else if (entry.name.endsWith('.cs')) found.set(entry.name, file);
    }
  };
  walk(root);
  return found;
}

export function unmirroredTwins(files, cwd) {
  const changed = new Set(files);
  const indexes = SIDES.map((side) => sources(cwd, side.root));
  const missing = [];
  for (const file of changed) {
    SIDES.forEach((side, index) => {
      const name = path.basename(file);
      if (!file.startsWith(`${side.root}/`) || !name.startsWith(side.prefix))
        return;
      const other = 1 - index;
      const twin = indexes[other].get(
        SIDES[other].prefix + name.slice(side.prefix.length)
      );
      if (twin && !changed.has(twin)) missing.push([file, twin]);
    });
  }
  return missing;
}

if (import.meta.main) {
  const cwd = path.resolve(import.meta.dirname, '..');
  const missing = unmirroredTwins(process.argv.slice(2), cwd);
  if (missing.length) {
    console.log(
      'Touch twins changed on one side only (ADR-0024); mirror them or confirm the change is host-specific:'
    );
    for (const [file, twin] of missing)
      console.log(`  ${file}\n    -> ${twin}`);
  }
}
