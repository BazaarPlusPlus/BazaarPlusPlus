# Card Titles From The Game's Data Cache

## Context

The History List filters runs by a Key Card (#1085), and players search for it by the name the game shows them, mostly in Simplified Chinese. The mod's battle snapshots store each item's template id and its English internal name; no file in the repository or the Data Root has Chinese card titles. The game keeps its card data and translations in its own cache, `Application.persistentDataPath/prod/cache` (`DataManifestType.GetCachePath` in the game's `TheBazaarRuntime.dll`): `GameData.db`, whose `cards` rows hold each card's title key and English title, and `translations/zh-CN.bytes`, a SQLite table of titles by key. [ADR-0008](0008-legacy-data-roots-and-v5-import.md) limits History to the current Data Root, which is inside the game directory; this cache is in the user's profile.

## Decision

- History reads card titles from that cache, and nothing else from it. On macOS it is `~/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache`; on Windows `%USERPROFILE%\AppData\LocalLow\Tempo Storm\The Bazaar\prod\cache` (Unity's location for the company and product in `app.info`). Other platforms have none.
- The files are the game's. The installer opens each read-only for one call and closes it, so it never holds a file the game replaces when it updates its data, and never writes, deletes, or copies them.
- A missing or unreadable cache, or a card it does not have, is not an error: the card keeps the internal name its snapshot carries, and the filter, which matches template ids, works the same.

## Rejected Alternatives

- Ship a card title table with the installer. It goes stale with every game update and adds a release step.
- Have the mod write localized titles into the snapshots. Runs recorded before the change would stay English, and titles would follow whatever language the game ran in.
- Derive the title key as the MD5 of the English internal name, the convention the mod's `ChineseTranslationCatalog` relies on, and read only the translations. It needs a hash dependency and misses the items whose internal name differs from their title.

## Consequences

ADR-0008's Data Root limit still holds for everything History writes and for the history it reads; this read is its one exception. A game update that changes the cache layout turns the titles back to English until History follows it. [History And Storage](../history-storage.md#key-cards) owns the current behavior.
