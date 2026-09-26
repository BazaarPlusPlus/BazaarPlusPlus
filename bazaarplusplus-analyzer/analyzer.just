set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Refresh shared configuration, then run an explicit analyzer CLI command.
cli +args:
    node ../scripts/workspace.mjs run analyzer -- uv run --locked bpp "$@"

# Check Python formatting, lint, and types using the locked environment.
check:
    uv run --locked ruff format --check .
    uv run --locked ruff check .
    uv run --locked ty check

# Run tests and coverage without running the publication pipeline.
test:
    uv run --locked pytest

# Apply safe Ruff fixes and format in place.
fmt:
    uv run --locked ruff check --fix .
    uv run --locked ruff format .
