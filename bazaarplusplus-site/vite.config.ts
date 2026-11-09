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
      'playwright-report/**'
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
  // The Web component library lives in ../design/ui (docs/adr/0018). Its files
  // resolve React from this project: they have no node_modules of their own.
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
