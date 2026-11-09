/** Runs a player must have in History before an install offers the GitHub
 *  star, so only someone who has used the plugin sees it. */
export const GITHUB_STAR_MIN_RUNS = 10;

export const GITHUB_STAR_OFFERED_KEY = 'github-star-offered';

type OfferStorage = Pick<Storage, 'getItem' | 'setItem'>;

/** Decides once per computer whether a finished install offers the GitHub
 *  star. The offer is spent when it is shown, whatever the player does next. */
export async function takeGithubStarOffer({
  storage,
  countRuns
}: {
  storage: OfferStorage;
  countRuns: () => Promise<number>;
}): Promise<boolean> {
  try {
    if (storage.getItem(GITHUB_STAR_OFFERED_KEY) !== null) return false;
    if ((await countRuns()) < GITHUB_STAR_MIN_RUNS) return false;
    storage.setItem(GITHUB_STAR_OFFERED_KEY, '1');
    return true;
  } catch {
    // An unreadable History or storage only means no offer.
    return false;
  }
}
