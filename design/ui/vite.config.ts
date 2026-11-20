import { defineConfig } from 'vite-plus';

// Run from design/ui so type-aware lint uses its tsconfig. Formatting uses the root config.
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
