# ADR-0026: One in-game kit per UI system, on shared colour tokens

Status: Accepted

## Context

The mod draws two UI systems inside the game: the Desktop UI in the Mono host's `Game/` panels and the Mobile UI in `src/BazaarPlusPlus.Mobile/`, which only the Android host compiles (ADR-0025). Both read the `ingame-*` colour tokens through a generated `InGameTheme` (`design/README.md`, In game). Before this record the same primitives were written more than once:

- The native `Btn_Rectangle` sprite addresses were listed by History, Ten-Win Builds and the combat status bar, each with its own load, retry and sliced-layer helper. History and Ten-Win Builds each decoded BPP's header art.
- The button state tints (rest, hover, pressed, disabled) were the same colour literals in the desktop and Mobile History buttons. The supporter tier colours were literals on the desktop, hex strings in Core's `BPPSupporterAttribution`, and parsed back from that hex by desktop History.
- The Mobile UI made its invisible tap and scroll surfaces from a literal near-zero alpha black at each site.
- The desktop Collection panel and its UI Toolkit supporter row draw a cool palette of their own from `Infrastructure/UiTokens/Colors.cs`.

Nothing kept a new literal out, so each panel drifted a little from the tokens.

## Decision

1. **Share values, not state rules.** Every colour a panel draws is an `ingame-*` token, and a fraction of one goes through `InGameTheme.WithAlpha`. A value used by both UI systems is one token, such as the `ingame-tint-*` button state tints. Which state takes which tint stays in each host: the desktop dims a button that still takes input and lifts a hovered one above its sprite's colours, the Mobile UI does neither. A shared state rule would change one host's look for no shared behaviour (ADR-0003).
2. **Core stays colour-free.** No generated Core type carries a colour; each host maps its Core enums (status, severity, outcome) to tokens in its own kit, as before. The one exception is Core's rich text for the touch supporter line, which needs hex: `BPPSupporterAttribution.MutedHex` and `TierHex` repeat their tokens, and `just design::check` fails when they disagree.
3. **One owner per primitive per UI system.**
   - Desktop: a `GameInterop/NativeSkin/` adapter owns the `Btn_Rectangle` addresses, their one process-wide Global-scope load and its retry, BPP's header art (from the `HistoryHeaderArtwork` constants), and the sliced-layer helper. A caller passes its own pixels-per-unit and inset, because the combat status bar draws a thinner bevel than the panels. Panels keep their own geometry and state.
   - Mobile: `UiKit` owns the fills, buttons and `HitArea`, the one invisible raycast surface; `NativeSkin` owns the native art the Android host borrows.
4. **Rules over compiled IL (ADR-0009).**
   - (a) The literal `Btn_Rectangle` appears only in the desktop NativeSkin adapter, and it must appear there.
   - (b) In the covered namespaces, no method constructs a `Color` or `Color32` or calls `ColorUtility.TryParseHtmlString`, except the generated `InGameTheme` and a short list of named members. A `static readonly` colour counts, since it is a construction in the static constructor. `Architecture.Tests/InGameColourRule` owns the rule; `InGameUiColourTests` applies it to the Desktop UI and `AndroidHost.Tests/MobileUiColourTests` to the whole Mobile UI.
5. **The scope grows with the migration.** The rule covers History, Ten-Win Builds, the combat status bar, supporters and hero badges first; the NativeSkin adapter joins when it lands; the desktop Collection panel and `Infrastructure/UiTokens` join when `Colors.cs` is deleted. Native Adjustments in `Patches/`, Settings, Combat Replay and Screenshots draw their own colours and are outside the kit until a change moves them in.

## Relationship to ADR-0003 and ADR-0025

ADR-0003 rejects a unification that only looks tidier. The shared part here is a value or a runtime resource with one owner: the same sprite loaded three times, the same tint typed twice, and colours that had already drifted. Each panel keeps its state, layout and lifecycle. ADR-0025 stands: the Mono host does not compile the Mobile tree, so each UI system keeps its own kit, and only the tokens cross between them.

## Consequences

- A colour changes in `design/tokens.json` and reaches both UI systems through `just design::build`; a literal in a covered panel fails `just mod::test` or `just mod::test-core`.
- A value that a token cannot hold, such as the desktop's over-bright hover (channels above 1), is a named member the rule test lists.
- Token bytes round a float literal to the nearest 1/255, so a migrated colour may move by up to half a step.

## Evidence

- Tokens: `design/tokens.json` (`ingame-*`), `design/build.ts` (`buildInGameTheme`, `supporterMismatches`)
- Desktop: `src/BazaarPlusPlus/Infrastructure/UiTokens/InGameTheme.g.cs`, `Game/Supporters/Ui/BPPSupporterTierColors.cs`, `GameInterop/Heroes/HeroVisual.cs`
- Mobile: `src/BazaarPlusPlus.Mobile/Ui/InGameTheme.g.cs`, `Ui/UiKit.cs` (`HitArea`)
- Rules: `tests/Architecture.Tests/InGameColourRule.cs`, `InGameUiColourTests.cs`, `tests/AndroidHost.Tests/MobileUiColourTests.cs`
