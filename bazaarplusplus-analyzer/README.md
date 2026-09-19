# BazaarPlusPlus Analyzer

The analyzer converts verified Bundle V5 deliveries into local hourly facts and
two independent current snapshots:

```text
analyzer-v5/heroes/latest.json
analyzer-v5/builds/latest.json
```

## Operate

Install the locked environment with `uv sync --locked`, copy `.env.example` to `.env`,
and fill in the required values. Configuration comes only from the repository
`.env`.

```bash
uv run --locked bpp run
uv run --locked bpp run --no-publish
uv run --locked bpp status --json
uv run --locked bpp verify --deep
```

Use `uv run --locked bpp --help` and `uv run --locked bpp <command> --help` for the complete
operator interface.

## Documentation

- Domain terms and boundaries: `CONTEXT.md`
- Pipeline recovery and publication invariants: `docs/architecture.md`
- Consumer calculations and cross-field semantics: `docs/specs/consumer-data-contract.md`
- JSON wire shapes: `contracts/v5/*.schema.json`
- Measured operational limits: `docs/measurements.md`

## Quality gate

```bash
just analyzer::check
just analyzer::test
```

## License

Released under the [MIT License](LICENSE).
