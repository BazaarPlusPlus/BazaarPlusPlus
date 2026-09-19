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

Do not edit a snapshot by hand. Regenerate one from the pinned corpus with:

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
