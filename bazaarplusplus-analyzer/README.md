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
uv run bpp run
uv run bpp run --no-publish
uv run bpp status --json
uv run bpp verify --deep
```

Use `uv run bpp --help` and `uv run bpp <command> --help` for the complete
operator interface.

## Documentation

- Domain terms and boundaries: `CONTEXT.md`
- Pipeline recovery and publication invariants: `docs/architecture.md`
- Consumer calculations and cross-field semantics: `docs/specs/consumer-data-contract.md`
- JSON wire shapes: `contracts/v5/*.schema.json`
- Measured operational limits: `docs/measurements.md`

## Quality gate

```bash
uv run ruff format --check .
uv run ruff check .
uv run ty check
uv run pytest
```

## License

Released under the [MIT License](LICENSE).
