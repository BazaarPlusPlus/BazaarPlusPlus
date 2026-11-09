# BazaarLens

BazaarLens converts verified Bundle V6 deliveries into local hourly facts and
two independent current snapshots:

```text
heroes/latest.json
builds/latest.json
```

Both publish to `metrics/v6/` in the public bucket, served at
`https://cdn.bazaarplusplus.com/metrics/v6`; see
[Publication target](docs/architecture.md#publication-target).

## Operate

Run workspace `just setup` with the shared server and Cloudflare credentials described
in the [development guide](../docs/development.md). The `analyzer` table in
`config.toml` is optional; state location and resource limits have defaults. `just analyzer::cli <command>`
refreshes the managed repository `.env` before invoking the CLI. Use this entry point
for local operations; the analyzer itself reads only that file.

```bash
just analyzer::cli run --no-publish
just analyzer::cli run
just analyzer::cli status --json
just analyzer::cli verify --deep
```

`run` publishes to the production destination by default. Use `--no-publish` for
local collection and analysis without publication.

Use `just analyzer::cli --help` and `just analyzer::cli <command> --help` for the
complete operator interface. Automation that prepares its own `.env` invokes
`uv run --locked bazaarlens <command>` directly from this project directory.

## Documentation

- Domain terms and boundaries: `GLOSSARY.md`
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
