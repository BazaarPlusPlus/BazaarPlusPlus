import { describe, expect, it } from 'vitest';
import { GITHUB_STAR_MIN_RUNS, takeGithubStarOffer } from './githubStarPrompt';

function memoryStorage() {
  const values = new Map<string, string>();
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => void values.set(key, value)
  };
}

const enoughRuns = async () => GITHUB_STAR_MIN_RUNS;
const tooFewRuns = async () => GITHUB_STAR_MIN_RUNS - 1;
const unreadableHistory = async (): Promise<number> => {
  throw new Error('database is locked');
};

describe('takeGithubStarOffer', () => {
  it('offers once when History has enough runs', async () => {
    const storage = memoryStorage();

    expect(await takeGithubStarOffer({ storage, countRuns: enoughRuns })).toBe(
      true
    );
    expect(await takeGithubStarOffer({ storage, countRuns: enoughRuns })).toBe(
      false
    );
  });

  it('keeps the offer while History has too few runs', async () => {
    const storage = memoryStorage();

    expect(
      await takeGithubStarOffer({
        storage,
        countRuns: tooFewRuns
      })
    ).toBe(false);
    expect(
      await takeGithubStarOffer({
        storage,
        countRuns: enoughRuns
      })
    ).toBe(true);
  });

  it('makes no offer when History cannot be read', async () => {
    const storage = memoryStorage();

    expect(
      await takeGithubStarOffer({
        storage,
        countRuns: unreadableHistory
      })
    ).toBe(false);
    expect(
      await takeGithubStarOffer({
        storage,
        countRuns: enoughRuns
      })
    ).toBe(true);
  });
});
