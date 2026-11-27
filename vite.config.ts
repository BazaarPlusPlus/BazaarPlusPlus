import { defineConfig } from 'vite-plus';

// Each bazaarplusplus-* project and packages/ui checks itself with its own config.
const ignorePatterns = [
  'bazaarplusplus-*/**',
  'packages/ui/**',
  // A symlink to .agents/skills, which is checked under its real path.
  '.claude/**',
  // oxfmt aligns tables by character count, which breaks Chinese tables.
  '**/*.md',
  'packages/runs/message-stream/game-message-schema.json'
];

export default defineConfig({
  fmt: {
    ignorePatterns,
    singleQuote: true,
    trailingComma: 'none',
    printWidth: 80,
    sortPackageJson: false
  },
  lint: {
    ignorePatterns,
    plugins: ['typescript'],
    categories: {
      correctness: 'off'
    },
    rules: {
      'typescript/no-explicit-any': 'error',
      'typescript/ban-ts-comment': [
        'error',
        {
          'ts-expect-error': true,
          'ts-ignore': true,
          'ts-nocheck': true,
          'ts-check': false
        }
      ],
      'typescript/no-import-type-side-effects': 'error',
      'typescript/no-non-null-assertion': 'error',
      'vite-plus/prefer-vite-plus-imports': 'error'
    },
    overrides: [
      {
        files: ['**/*.test.ts', '**/test-support/**'],
        rules: {
          'typescript/no-non-null-assertion': 'off'
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
  }
});
