# Combat Impact corpus evidence

The JSON files in this directory are deterministic, generated evidence snapshots. They retain the
minimum information needed to compare a future attribution model with an earlier one without
committing the full per-frame report:

- model and corpus schema versions;
- exact source-artifact names and SHA-256 hashes;
- the `GameData.db` and interpreting game-assembly names, versions, and SHA-256 hashes;
- sorted battle IDs and the complete-report SHA-256;
- raw adjustment inventories and rule diagnostics;
- terminal raw/effective/overkill and order-sensitivity measurements;
- measured, attributed, proof-class, and Unknown totals.

Do not edit a snapshot by hand.

## The pinned corpus

`pinned-corpus.json` is the golden for the committed corpus in `../corpus/`. `just mod::test`
replays that corpus through `ScenarioRunner.Tests`
(`Combat_impact_corpus_matches_committed_evidence`), writes `artifacts/combat-impact-corpus/`
(`report.json`, `evidence.json`), and requires the evidence to match the golden. The hashes of the
`game_types` and `json_runtime` inputs are the exception: both come from the installed game's
Managed directory, so a game update changes them, the test prints both values, and
`fullReportSha256` decides. Regenerate the golden from the mod root, then review the diff:

```bash
BPP_UPDATE_GOLDENS=1 dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj \
  --filter Combat_impact_corpus_matches_committed_evidence
git diff tests/CombatImpact.Corpus/evidence/
```

The corpus holds the Ghost concert-hall battle from `tests/PostCombatImpact.Tests/Fixtures/` and
eleven local PvP replay payloads, chosen so the set covers every non-zero rule, terminal, and
attribution diagnostic of the 41 replays they were drawn from. `GameData.cards.db` is the `cards`
table of `GameData.db` cut down to the templates the corpus spawns or transforms into, plus every
PlayerEffect template, because the implicit PlayerEffect reader only resolves a unique match
among all of them. The trimmed database and the full `GameData.db` give the same
`fullReportSha256`. Rebuild the corpus from the mod root with the source `GameData.db` (found
automatically or given by `BPP_GAMEDATA_DB`) and the replay store holding the listed battles:

```bash
dotnet run --project tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj -p:BppDeployToGame=false \
  -- prepare tests/CombatImpact.Corpus/corpus "<BazaarPlusPlusV5>/CombatReplays" \
  $(ls tests/CombatImpact.Corpus/corpus | sed -n 's/\.payload\.mpack\.gz$//p' | grep -v '^ghost-')
```

`prepare` rewrites the directory: it wraps the concert-hall spawn and combat fixtures into a
replay payload, re-serializes each named battle with fixed message ids and an empty despawn
message, and clears the opponent block of the spawn state (name, titles, rank, rating, account
and collection ids) because the repository is public. The current payloads carry no opponent
block. Then regenerate `pinned-corpus.json` as above.

## External corpora

The other snapshots record large corpora that stay outside Git. Regenerate one with:

```bash
BPP_BUNDLE_CORPUS_LIMIT=100 \
BPP_COMBAT_IMPACT_EVIDENCE_PATH="tests/CombatImpact.Corpus/evidence/<snapshot>.json" \
just mod::test-corpus <corpus-root> <full-report-path>
```

`<corpus-root>` may be a replay-payload store, a historical `run-bundles/*.mpack.gz` cache, or the
current analyzer raw V5 tree containing `*.bundle` files.

The full report is intentionally kept outside Git because it contains every periodic frame and is
roughly 70 MB for the production sample. The evidence snapshot identifies that report and its exact
input corpus cryptographically.
