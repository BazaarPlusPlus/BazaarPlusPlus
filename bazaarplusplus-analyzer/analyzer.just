set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Refresh shared configuration, then run an explicit analyzer CLI command.
cli +args:
    bun ../scripts/workspace.ts run analyzer -- uv run --locked bazaarlens "$@"

# Check Python formatting, lint, and types using the locked environment.
check:
    uv run --locked ruff format --check .
    uv run --locked ruff check .
    uv run --locked ty check

# Run tests and coverage without running the publication pipeline.
test:
    uv run --locked pytest

# Rewrite the pipeline and projection goldens from fixture Bundles; review the diff.
golden:
    BPP_UPDATE_GOLDENS=1 uv run --locked pytest tests/test_pipeline_golden.py tests/test_bundle_goldens.py --no-cov

# Apply safe Ruff fixes and format in place.
fmt:
    uv run --locked ruff check --fix .
    uv run --locked ruff format .
