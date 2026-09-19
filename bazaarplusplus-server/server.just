set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Run the Worker's type, lint, and formatting checks.
check:
    npm run check

# Run the Mod compatibility probe and Worker tests in the local Cloudflare test runtime.
test: test-mod-compat
    npm test

# Verify Ghost responses with the real ModApi assembly; only the .NET SDK is required.
test-mod-compat:
    dotnet build ../bazaarplusplus-mod/tests/ModApi.Tests/ModApi.Tests.csproj
    dotnet run --project scripts/ghost-projection/mod-compat/Probe.csproj -p:ModRoot="$PWD/../bazaarplusplus-mod" -- "$PWD/../bazaarplusplus-mod" contracts/v5/ghost-summary.response.json

# Start the local Worker; requires this project's .dev.vars.
dev:
    npm run dev

# Format Worker sources in place.
fmt:
    npm run format
