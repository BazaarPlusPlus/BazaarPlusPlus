import { createSpaLocation, type ResolvedSpaLocation } from '../src/app/router';

/** Moves jsdom's window to `href` and resolves it the way the App does. */
export function locationAt(href: string): ResolvedSpaLocation {
  window.history.replaceState(null, '', href);
  return createSpaLocation().current();
}
