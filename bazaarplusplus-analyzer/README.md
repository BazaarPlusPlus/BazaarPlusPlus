# BazaarPlusPlus Analyzer

The analyzer converts verified Bundle V6 deliveries into local hourly facts and
two independent current snapshots:

```text
heroes/latest.json
builds/latest.json
```

Both publish to the V6 metrics bucket, served at
`https://bpp-metrics-v6.bazaarplusplus.com`; see
[Publication target](docs/architecture.md#publication-target).

## Operate

Run workspace `just setup` and fill in the shared analyzer configuration described
in the [development guide](../docs/development.md). `just analyzer::cli <command>`
refreshes the managed repository `.env` before invoking the CLI. The analyzer itself
still reads only that file; the direct run/status/verify recipes below require a current projection.

```bash
just analyzer::run
just analyzer::run --no-publish
just analyzer::status --json
just analyzer::verify --deep
```

Use `uv run --locked bpp --help` and `uv run --locked bpp <command> --help` for the complete
operator interface.

## Documentation

- Domain terms and boundaries: `CONTEXT.md`
- Pipeline recovery and publication invariants: `docs/architecture.md`
- Consumer calculations and cross-field semantics: `docs/specs/consumer-data-contract.md`
- JSON wire shapes: `contracts/v6/*.schema.json`
- Measured operational limits: `docs/measurements.md`

## Quality gate

```bash
just analyzer::check
just analyzer::test
```

## License

Released under the [MIT License](LICENSE).
