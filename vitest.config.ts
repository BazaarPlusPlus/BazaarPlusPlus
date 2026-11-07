import { defineConfig } from 'vitest/config';

// Every root-tooling test. `just release::test` runs the release project and
// `just commands-check` the tooling project.
export default defineConfig({
  test: {
    environment: 'node',
    // The tests drive git, just, bash, MSBuild and Bun children; under a full
    // parallel run a cold start exceeds the 5 s default.
    testTimeout: 60_000,
    projects: [
      {
        extends: true,
        test: { name: 'release', include: ['release/**/*.test.ts'] }
      },
      {
        extends: true,
        test: {
          name: 'tooling',
          include: [
            'scripts/**/*.test.ts',
            '.github/scripts/*.test.ts',
            'design/*.test.ts',
            '.agents/skills/*/scripts/*.test.ts'
          ]
        }
      }
    ]
  }
});
