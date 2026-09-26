# BazaarPlusPlus Analyzer

The analyzer converts verified Bundle V5 deliveries into local hourly facts and
two independent current snapshots:

```text
analyzer-v5/heroes/latest.json
analyzer-v5/builds/latest.json
```

## Operate

Run workspace `just setup` and fill in the shared analyzer configuration described
in the [development guide](../docs/development.md). `just analyzer::cli <command>`
refreshes the managed repository `.env` before invoking the CLI. The analyzer itself
still reads only that file; direct uv commands below require a current projection.

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
