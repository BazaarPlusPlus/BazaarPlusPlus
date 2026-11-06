# Walkthrough scenarios

Walk them in this order on every device; the report's rows follow the order
they are first recorded. A scenario marked mobile is skipped on the desktop
with the reason `desktop has no such screen`.

## Lobby

| Scenario | Reach | Look at |
|---|---|---|
| `launcher` (mobile) | The App's own launcher, before the game starts. Android: `BPP_ANDROID_SERIAL=SERIAL just mod::android-launcher`, `capture … --portrait`, then `just mod::android-launch` and look until the main menu shows | Portrait, nothing clipped |
| `main-menu` | The game's main menu after load | The HUD shortcuts by the settings gear, History in the MENU grid |
| `collection` | `panel collection` | Grid, filters, close button |
| `collection-inspect` | In Collection, tap one card | The inspect sheet |
| `history-runs` | `panel history`; the Runs section | Run list, boards, pager |
| `history-inspect` | In a run's board, tap one card | The inspect sheet with saved values |
| `history-ghost` | The Ghost tab | List or its empty message |
| `history-favorites` | The Favorites tab | List or its empty message |
| `settings` | The game's settings, scrolled to the BazaarPlusPlus rows (desktop: the settings dock) | Every BPP row readable and tappable |

## In a run

Close any open panel. Start a run from the main menu, or continue the one the
account already has, and stop at the first shop. Reuse that run on the next
device instead of starting another.

| Scenario | Reach | Look at |
|---|---|---|
| `run-shop` | The shop screen | HUD shortcuts, Ten-Win Builds entry, Screen Fill and its day clock |
| `tooltip` | Tap a shop item (desktop: hover it) | The tooltip's preview switches and upgrade or enchant preview |
| `ten-win` | `panel live_build` | Recommendation, shop, stash and board sections |
| `collection-run` | `panel collection` | Merchant preselection and the day filter (desktop; Android preselects no merchant) |

Dismiss an inspect sheet (back key, its X, or Escape) before the next `panel`
command: an open modal swallows the request, and `panel` cannot tell.

Open `live_build` only in a run: outside one it opens on an empty snapshot no
player can reach.
