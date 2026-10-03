# Consumer schemas

These strict Draft 2020-12 schemas own field names, types, required fields,
enums, and scalar bounds:

- `heroes.schema.json` validates `analyzer-v5/heroes/latest.json`.
- `builds.schema.json` validates `analyzer-v5/builds/latest.json`.

Cross-field and calculation semantics live in
`docs/specs/consumer-data-contract.md`. `validate_snapshot` enforces both
authorities before publication.

## Shared goldens

`fixtures/heroes.latest.json` and `fixtures/builds.latest.json` are the exact
bytes the analyzer publishes for one Source Day of fixture Bundles served
through a stand-in Bundle Server (`tests/test_pipeline_golden.py`). Consumer
tests read them in place of hand-written snapshots. Never edit them by hand:
`just analyzer::golden` regenerates them, and `just analyzer::test` fails when
the pipeline no longer reproduces them.

## Hero aliases

`hero-aliases.json` owns the mapping from legacy hero ids to canonical ones.
The analyzer normalizes heroes with it, and consumer tests read it. It owns
only the mapping: each reader keeps its own matching rule (the analyzer matches
the trimmed id exactly; the mod and installer ignore ASCII case).
