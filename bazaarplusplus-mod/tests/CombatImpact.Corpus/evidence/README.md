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

The pinned corpus has two layers and two goldens (ADR-0019 item 5):

- `../../CombatImpact.CorpusExport/corpus/` holds the game replay payloads and the trimmed
  `GameData.cards.db`. The host-lane exporter `CombatImpact.CorpusExport` turns them into the
  Combat Impact Corpus `../corpus/pinned-corpus.json.gz`, BPP's own format
  (`CombatImpactCorpusCodec` in `../Format/`). `ScenarioRunner.Tests`
  (`Combat_impact_corpus_export_matches_committed_corpus`, in `just mod::test`) exports again and
  requires the JSON to match the committed corpus. The `game_types` and `json_runtime` hashes are
  the exception: both come from the Managed snapshot the exporter ran against, so a game update
  changes them and the test prints both values. A rewrite keeps the committed file when only those
  hashes differ.
- `pinned-corpus.json` is the golden of the Core-lane runner `CombatImpact.Corpus`, which reads
  only the Combat Impact Corpus. Run with no arguments (in `just mod::test-core`, and as a
  scenario capsule in `just mod::test`), it writes `artifacts/combat-impact-corpus/`
  (`report.json`, `evidence.json`) and requires the evidence to match the golden byte for byte.
  Its `verificationInputs` are copied from the corpus, so they name the inputs the exporter read.

Regenerate both from the mod root, the corpus first, then review the diff:

```bash
BPP_UPDATE_GOLDENS=1 dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj \
  --filter Combat_impact_corpus_export_matches_committed_corpus
BPP_UPDATE_GOLDENS=1 dotnet run --project tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj \
  -p:BppSnapshotPlatform=none
git diff tests/CombatImpact.Corpus/
```

The game payloads hold the Ghost concert-hall battle from `tests/CombatImpactAdapter.Tests/Fixtures/`
and eleven local PvP replay payloads, chosen so the set covers every non-zero rule, terminal, and
attribution diagnostic of the 41 replays they were drawn from. `GameData.cards.db` is the `cards`
table of `GameData.db` cut down to the templates the corpus spawns or transforms into, plus every
PlayerEffect template, because the implicit PlayerEffect reader only resolves a unique match
among all of them. The trimmed database and the full `GameData.db` give the same
`fullReportSha256`. Rebuild the game payloads from the mod root with the source `GameData.db`
(found automatically or given by `BPP_GAMEDATA_DB`) and the replay store holding the listed
battles:

```bash
dotnet run --project tests/CombatImpact.CorpusExport/CombatImpact.CorpusExport.csproj -p:BppDeployToGame=false \
  -- prepare tests/CombatImpact.CorpusExport/corpus "<BazaarPlusPlusV6>/CombatReplays" \
  $(ls tests/CombatImpact.CorpusExport/corpus | sed -n 's/\.payload\.mpack\.gz$//p' | grep -v '^ghost-')
```

`prepare` rewrites the directory: it wraps the concert-hall spawn and combat fixtures into a
replay payload, re-serializes each named battle with fixed message ids and an empty despawn
message, and clears the opponent block of the spawn state (name, titles, rank, rating, account
and collection ids) because the repository is public. The current payloads carry no opponent
block. Then regenerate the corpus and `pinned-corpus.json` as above.

## External corpora

The other snapshots record large corpora that stay outside Git. Regenerate one with:

```bash
BPP_BUNDLE_CORPUS_LIMIT=100 \
BPP_COMBAT_IMPACT_EVIDENCE_PATH="tests/CombatImpact.Corpus/evidence/<snapshot>.json" \
just mod::test-corpus <corpus-root> <full-report-path>
```

`<corpus-root>` may be a replay-payload store, a historical `run-bundles/*.mpack.gz` cache, or the
current analyzer raw V5 tree containing `*.bundle` files. The recipe exports it to
`<full-report-path minus .json>.corpus.json.gz` in the host lane, then projects that file in the
Core lane.

The full report is intentionally kept outside Git because it contains every periodic frame and is
roughly 70 MB for the production sample. The evidence snapshot identifies that report and its exact
input corpus cryptographically.
