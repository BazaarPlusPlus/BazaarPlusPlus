import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  preparePublishManifest,
  type PackageManifest
} from './publish-manifest';

const uiRoot = path.resolve(import.meta.dirname, '..');
const repoRoot = path.resolve(uiRoot, '../..');
const rootNodeModules = path.join(repoRoot, 'node_modules');

function run(command: string, args: string[], cwd: string): string {
  const result = spawnSync(command, args, {
    cwd,
    encoding: 'utf8',
    env: { ...process.env, CI: 'true' }
  });
  if (result.status !== 0) {
    throw new Error(
      `${command} ${args.join(' ')} failed in ${cwd} (exit ${String(result.status)}):\n${result.stdout}\n${result.stderr}`
    );
  }
  return result.stdout;
}

function linkNodeModule(targetNodeModules: string, name: string) {
  const source = path.join(rootNodeModules, name);
  if (!fs.existsSync(source)) {
    throw new Error(`Missing hoisted workspace dependency: ${source}`);
  }
  const dest = path.join(targetNodeModules, name);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.symlinkSync(source, dest, 'dir');
}

function setupConsumer(
  baseDir: string,
  name: string,
  tarballPath: string,
  files: Record<string, string>,
  deps: string[]
): string {
  const dir = path.join(baseDir, name);
  const nodeModules = path.join(dir, 'node_modules');
  const pkgDest = path.join(nodeModules, '@bazaarplusplus', 'ui');
  fs.mkdirSync(pkgDest, { recursive: true });
  run('tar', ['-xzf', tarballPath, '--strip-components=1', '-C', pkgDest], dir);
  for (const dep of deps) linkNodeModule(nodeModules, dep);
  const binDir = path.join(rootNodeModules, '.bin');
  if (fs.existsSync(binDir)) {
    fs.symlinkSync(binDir, path.join(nodeModules, '.bin'), 'dir');
  }
  for (const [rel, content] of Object.entries(files)) {
    const filePath = path.join(dir, rel);
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, content);
  }
  return dir;
}

function main() {
  // 1. Build publishable dist/ output.
  run('bun', ['run', 'build'], uiRoot);

  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-ui-pack-check-'));
  try {
    // 2. Stage a publish copy with dev-only @bpp/source condition stripped.
    const stageDir = path.join(scratch, 'stage');
    fs.mkdirSync(stageDir, { recursive: true });
    const rawManifest = JSON.parse(
      fs.readFileSync(path.join(uiRoot, 'package.json'), 'utf8')
    ) as PackageManifest;
    const publishManifest = preparePublishManifest(rawManifest);
    fs.writeFileSync(
      path.join(stageDir, 'package.json'),
      `${JSON.stringify(publishManifest, null, 2)}\n`
    );
    fs.copyFileSync(
      path.join(uiRoot, 'README.md'),
      path.join(stageDir, 'README.md')
    );
    fs.cpSync(path.join(uiRoot, 'dist'), path.join(stageDir, 'dist'), {
      recursive: true
    });
    fs.cpSync(path.join(uiRoot, 'styles'), path.join(stageDir, 'styles'), {
      recursive: true
    });

    // 3. Pack tarball and inspect its exact entries.
    run('bun', ['pm', 'pack', '--destination', scratch], stageDir);
    const tarballs = fs
      .readdirSync(scratch)
      .filter((entry) => entry.endsWith('.tgz'));
    assert.equal(tarballs.length, 1, 'Expected exactly one .tgz tarball');
    const tarball = path.join(scratch, tarballs[0]);

    const tarEntries = run('tar', ['-tzf', tarball], scratch)
      .trim()
      .split('\n')
      .map((entry) => entry.replace(/^package\//, ''));

    for (const required of [
      'package.json',
      'README.md',
      'dist/index.js',
      'dist/index.d.ts',
      'dist/game/index.js',
      'dist/game/index.d.ts',
      'styles/tokens.css',
      'styles/theme.css',
      'styles/primitives.css',
      'styles/game.css',
      'styles/all.css'
    ]) {
      assert.ok(
        tarEntries.includes(required),
        `Tarball missing required entry: ${required}`
      );
    }

    const shaderChunks = tarEntries.filter(
      (entry) =>
        entry.startsWith('dist/game/enchantment-fx/programs/') &&
        entry.endsWith('.js')
    );
    assert.ok(
      shaderChunks.length >= 14,
      `Expected >= 14 split shader chunks, got ${shaderChunks.length}`
    );

    for (const forbidden of ['src/', 'test/', 'tools/']) {
      assert.equal(
        tarEntries.some((entry) => entry.startsWith(forbidden)),
        false,
        `Tarball must not include ${forbidden}`
      );
    }

    const packedManifest = JSON.parse(
      run('tar', ['-xOzf', tarball, 'package/package.json'], scratch)
    ) as PackageManifest;
    assert.equal(
      JSON.stringify(packedManifest).includes('@bpp/source'),
      false,
      'Packed manifest must not retain @bpp/source dev condition'
    );
    assert.equal(
      JSON.stringify(packedManifest).includes('workspace:'),
      false,
      'Packed manifest must not retain workspace: protocol'
    );

    const sharedDeps = [
      'react',
      'react-dom',
      '@types/react',
      '@types/react-dom',
      'typescript',
      'vite',
      'vite-plus',
      '@vitejs/plugin-react'
    ];

    const consumerTsconfig = JSON.stringify(
      {
        compilerOptions: {
          target: 'ES2023',
          lib: ['ES2023', 'DOM', 'DOM.Iterable'],
          module: 'ESNext',
          moduleResolution: 'Bundler',
          types: ['vite/client'],
          jsx: 'react-jsx',
          strict: true,
          skipLibCheck: true,
          noEmit: true
        },
        include: ['src']
      },
      null,
      2
    );

    // 4. Consumer A: External React 19 + Tailwind CSS v4 + Vite app using both entrypoints.
    const tailwindConsumer = setupConsumer(
      scratch,
      'consumer-tailwind',
      tarball,
      {
        'package.json': JSON.stringify({
          name: 'consumer-tailwind',
          private: true,
          type: 'module'
        }),
        'tsconfig.json': consumerTsconfig,
        'vite.config.ts': `import { defineConfig } from 'vite-plus';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [tailwindcss(), react()]
});
`,
        'index.html': `<!doctype html>
<html>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
`,
        'src/app.css': `@import 'tailwindcss';
@import '@bazaarplusplus/ui/tokens.css';
@import '@bazaarplusplus/ui/theme.css';
@import '@bazaarplusplus/ui/styles.css';
`,
        'src/main.tsx': `import './app.css';
import { createRoot } from 'react-dom/client';
import { Badge, Button, PageHeader } from '@bazaarplusplus/ui';
import {
  CardCatalog,
  CardDetail,
  CardTile,
  createCardCatalogClient,
  useCardCatalog
} from '@bazaarplusplus/ui/game';

const client = createCardCatalogClient();

function Cards() {
  const { card } = useCardCatalog();
  const sample = card({ templateId: 'demo', tier: 'Gold', enchant: 'Shiny' });
  return (
    <div className="bg-panel text-accent-text rounded-panel p-4">
      <CardTile card={sample} />
      <CardDetail
        card={sample}
        tagLabels={{}}
        tierLabels={{}}
        enchantLabels={{
          Deadly: 'Deadly',
          Ethereal: 'Ethereal',
          Fiery: 'Fiery',
          Golden: 'Golden',
          Heavy: 'Heavy',
          Icy: 'Icy',
          Mossy: 'Mossy',
          Mystical: 'Mystical',
          Obsidian: 'Obsidian',
          Radiant: 'Radiant',
          Restorative: 'Restorative',
          Shielded: 'Shielded',
          Shiny: 'Shiny',
          Tiny: 'Tiny',
          Toxic: 'Toxic',
          Turbo: 'Turbo'
        }}
        labels={{
          sizes: {},
          enchantments: 'Enchantments',
          allEnchantments: 'All',
          cooldown: 'CD',
          ammo: 'Ammo',
          seconds: 's',
          unavailable: 'N/A'
        }}
        showAllEnchantments={false}
      />
    </div>
  );
}

createRoot(document.getElementById('root')!).render(
  <main>
    <PageHeader
      title="External Tailwind App"
      meta={<Badge tone="accent">v1</Badge>}
      action={<Button variant="primary">Action</Button>}
    />
    <CardCatalog source={{ base: '/card-art/' }} locale="en" client={client}>
      <Cards />
    </CardCatalog>
  </main>
);
`
      },
      [...sharedDeps, 'tailwindcss', '@tailwindcss/vite']
    );

    run(
      path.join(rootNodeModules, '.bin', 'tsc'),
      ['--noEmit'],
      tailwindConsumer
    );
    run(path.join(rootNodeModules, '.bin', 'vp'), ['build'], tailwindConsumer);

    const tailwindAssets = fs.readdirSync(
      path.join(tailwindConsumer, 'dist', 'assets')
    );
    const tailwindCssFile = tailwindAssets.find((file) =>
      file.endsWith('.css')
    );
    assert.ok(tailwindCssFile, 'Tailwind consumer should emit a CSS asset');
    const tailwindCss = fs.readFileSync(
      path.join(tailwindConsumer, 'dist', 'assets', tailwindCssFile),
      'utf8'
    );
    assert.ok(
      tailwindCss.includes('--bg-panel'),
      'Tailwind consumer CSS should include design tokens'
    );
    assert.ok(
      tailwindCss.includes('.bpp-btn'),
      'Tailwind consumer CSS should include primitive classes'
    );
    assert.ok(
      tailwindCss.includes('.bpp-card'),
      'Tailwind consumer CSS should include game classes'
    );
    const tailwindJsAssets = tailwindAssets.filter((file) =>
      file.endsWith('.js')
    );
    assert.ok(
      tailwindJsAssets.length > 5,
      `Expected split shader chunks in Tailwind consumer build, got ${tailwindJsAssets.length} JS files`
    );

    // 5. Consumer B: External React 19 + plain CSS (no Tailwind, primitives only).
    const plainConsumer = setupConsumer(
      scratch,
      'consumer-plain',
      tarball,
      {
        'package.json': JSON.stringify({
          name: 'consumer-plain',
          private: true,
          type: 'module'
        }),
        'tsconfig.json': consumerTsconfig,
        'vite.config.ts': `import { defineConfig } from 'vite-plus';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()]
});
`,
        'index.html': `<!doctype html>
<html>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
`,
        'src/app.css': `@import '@bazaarplusplus/ui/tokens.css';
@import '@bazaarplusplus/ui/primitives.css';
`,
        'src/main.tsx': `import './app.css';
import { createRoot } from 'react-dom/client';
import { Badge, Button, StatusBanner } from '@bazaarplusplus/ui';

createRoot(document.getElementById('root')!).render(
  <main>
    <Button variant="primary">Install</Button>
    <Badge tone="ok">OK</Badge>
    <StatusBanner tone="info" title="Ready" message="Installed from tarball" />
  </main>
);
`
      },
      sharedDeps
    );

    run(path.join(rootNodeModules, '.bin', 'tsc'), ['--noEmit'], plainConsumer);
    run(path.join(rootNodeModules, '.bin', 'vp'), ['build'], plainConsumer);

    const plainAssets = fs.readdirSync(
      path.join(plainConsumer, 'dist', 'assets')
    );
    const plainCssFile = plainAssets.find((file) => file.endsWith('.css'));
    assert.ok(plainCssFile, 'Plain CSS consumer should emit a CSS asset');
    const plainCss = fs.readFileSync(
      path.join(plainConsumer, 'dist', 'assets', plainCssFile),
      'utf8'
    );
    assert.ok(
      plainCss.includes('.bpp-btn'),
      'Plain CSS consumer should include primitive classes'
    );
    assert.equal(
      plainCss.includes('.bpp-card-detail'),
      false,
      'Primitives-only consumer should not pull in game CSS'
    );
    const plainJsAssets = plainAssets.filter((file) => file.endsWith('.js'));
    assert.equal(
      plainJsAssets.length,
      1,
      `Primitives-only consumer should not emit game shader chunks (got ${plainJsAssets.join(', ')})`
    );

    console.log(
      `pack-check passed (${tarEntries.length} tarball entries, ${shaderChunks.length} shader chunks, Tailwind 4 + plain CSS consumers verified)`
    );
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }
}

main();
