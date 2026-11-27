import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import {
  defaultClientConditions,
  defaultServerConditions,
  defineConfig,
  lazyPlugins,
  searchForWorkspaceRoot
} from 'vite-plus';

export default defineConfig({
  test: { include: ['test/**/*.test.ts'] },
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

      // Goldens and fixtures the tests compare byte for byte.
      'public/card-art/**',
      'src/demo-data/*.json'
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
  resolve: {
    conditions: ['@bpp/source', ...defaultClientConditions],
    dedupe: ['react', 'react-dom']
  },
  ssr: {
    resolve: {
      conditions: ['@bpp/source', ...defaultServerConditions]
    }
  },
  server: {
    fs: {
      allow: [searchForWorkspaceRoot(process.cwd())]
    }
  }
});
