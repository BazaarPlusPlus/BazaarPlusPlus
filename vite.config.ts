import { defineConfig } from 'vite-plus';

// Each bazaarplusplus-* project checks itself with its own config.
const ignorePatterns = [
  'bazaarplusplus-*/**',
  // A symlink to .agents/skills, which is checked under its real path.
  '.claude/**',
  // oxfmt aligns tables by character count, which breaks Chinese tables.
  '**/*.md'
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
    ignorePatterns: [
      ...ignorePatterns,
      // The Web component library lints itself under the React rules
      // (design/ui/vite.config.ts).
      'design/ui/**'
    ],
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
      // Type-only imports must not introduce runtime module side effects.
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
