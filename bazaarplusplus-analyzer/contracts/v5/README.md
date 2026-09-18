# Consumer schemas

These strict Draft 2020-12 schemas own field names, types, required fields,
enums, and scalar bounds:

- `heroes.schema.json` validates `analyzer-v5/heroes/latest.json`.
- `builds.schema.json` validates `analyzer-v5/builds/latest.json`.

Cross-field and calculation semantics live in
`docs/specs/consumer-data-contract.md`. `validate_snapshot` enforces both
authorities before publication.
