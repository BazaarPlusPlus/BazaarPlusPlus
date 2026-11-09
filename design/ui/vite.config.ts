import { defineConfig } from 'vite-plus';

// Lint for the Web component library, under the React rules the installer and
// the site use. The root `bun run lint` runs it from this directory, so the
// type-aware pass reads ./tsconfig.json; the root lint skips design/ui.
// Formatting stays with the root fmt block.
export default defineConfig({
  lint: {
    plugins: [
      'typescript',
      'oxc',
      'unicorn',
      'react',
      'jsx-a11y',
      'import',
      'promise'
    ],
    categories: {
      correctness: 'error',
      suspicious: 'error',
      perf: 'error'
    },
    ignorePatterns: ['vite.config.ts'],
    rules: {
      'react/react-in-jsx-scope': 'off',
      'jsx-a11y/prefer-tag-over-role': 'off',
      'jsx-a11y/control-has-associated-label': 'off',
      'import/no-unassigned-import': 'off',
      'typescript/no-unsafe-type-assertion': 'off',
      'typescript/consistent-return': 'off',
      'typescript/no-explicit-any': 'error',
      'typescript/no-non-null-assertion': 'error',
      'vite-plus/prefer-vite-plus-imports': 'error'
    },
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
  }
});
