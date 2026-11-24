import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';
import { defineConfig, lazyPlugins, searchForWorkspaceRoot } from 'vite-plus';

export default defineConfig({
  fmt: {
    singleQuote: true,
    trailingComma: 'none',
    printWidth: 80,
    sortPackageJson: false,
    ignorePatterns: [
      'dist/**',
      '.wrangler/**',
      '**/*.md',
      'e2e/__snapshots__/**',
      'test-results/**',
      'playwright-report/**',
      // Written by tools/message-schema/generate.ts, whose check compares the exact bytes.
      'src/features/runs/message-stream/game-message-schema.json',
      // Goldens and fixtures the tests compare byte for byte.
      'test/fixtures/run-stream.*'
    ]
  },
  lint: {
    plugins: [
      'typescript',
      'oxc',
      'unicorn',
      'react',
      'jsx-a11y',
      'import',
      'promise',
      'vitest'
    ],
    categories: {
      correctness: 'error',
      suspicious: 'error',
      perf: 'error'
    },
    ignorePatterns: [
      'dist/**',
      '.wrangler/**',
      'test-results/**',
      'playwright-report/**'
    ],
    rules: {
      'react/react-in-jsx-scope': 'off',
      'unicorn/no-array-sort': 'off',
      'unicorn/no-array-reverse': 'off',
      'react/no-array-index-key': 'off',
      'jsx-a11y/prefer-tag-over-role': 'off',
      'jsx-a11y/control-has-associated-label': 'off',
      'import/no-unassigned-import': 'off',
      'typescript/no-unsafe-type-assertion': 'off',
      'typescript/consistent-return': 'off',
      'typescript/unbound-method': 'off',
      'vite-plus/prefer-vite-plus-imports': 'error'
    },
    overrides: [
      {
        files: ['test/**/*.{ts,tsx}'],
        rules: {
          'vitest/require-mock-type-parameters': 'off'
        }
      }
    ],
    options: {
      typeAware: true,
      typeCheck: true
    },
    jsPlugins: [
      {
        name: 'vite-plus',
        specifier: 'vite-plus/oxlint-plugin'
      }
    ]
  },
  plugins: lazyPlugins(() => [react(), tailwindcss()]),
  // Shared @bpp/ui files resolve React from the site because design/ui has no node_modules.
  resolve: {
    alias: {
      '@bpp/ui': fileURLToPath(
        new URL('../design/ui/index.ts', import.meta.url)
      )
    },
    dedupe: ['react', 'react-dom']
  },
  server: {
    fs: {
      allow: [searchForWorkspaceRoot(process.cwd()), '../design']
    }
  }
});
