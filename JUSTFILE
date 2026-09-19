set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Root release and command scripts, formatted with the installer's Prettier.
root_js := "release.mjs 'release/**/*.{mjs,json}' 'scripts/**/*.mjs'"

# List commands without building or publishing anything.
default:
    @{{ quote(just_executable()) }} --list

# Check all projects without signing, publishing, or deploying to the game.
[group('workspace')]
check: commands-check release-check mod-format-check mod-build installer-check site-check server-check analyzer-check

# Run all project tests without publishing data or deploying to the game.
[group('workspace')]
test: commands-check mod-test installer-test site-test server-test analyzer-test

# Format every project in place, then re-project release files.
[group('workspace')]
fmt: && release-sync
    {{ quote(just_executable()) }} --fmt
    npm --prefix bazaarplusplus-installer exec -- prettier --config bazaarplusplus-installer/.prettierrc.json --write {{ root_js }}
    cd bazaarplusplus-mod && bash ./run.sh format
    cd bazaarplusplus-installer && npm run format
    cd bazaarplusplus-site && npm run format
    cd bazaarplusplus-server && npm run format
    cd bazaarplusplus-analyzer && uv run --locked ruff check --fix . && uv run --locked ruff format .

# Install the workspace Git hooks (requires installer npm dependencies).
[group('workspace')]
hooks-install:
    bazaarplusplus-installer/node_modules/.bin/lefthook install

# Check formatting and exercise command routing with isolated tool stubs.
[group('workspace')]
commands-check:
    {{ quote(just_executable()) }} --fmt --check
    npm --prefix bazaarplusplus-installer exec -- prettier --config bazaarplusplus-installer/.prettierrc.json --check {{ root_js }}
    node --test scripts/just.test.mjs

# Compile the mod without changing the installed game; forward build options.
[group('mod')]
[working-directory('bazaarplusplus-mod')]
mod-build *args:
    bash ./run.sh build --no-deploy "$@"

# Fail on C# files that the repo-pinned CSharpier would reformat.
[group('mod')]
[working-directory('bazaarplusplus-mod')]
mod-format-check:
    bash ./run.sh format-check

# Run the offline mod suite; forward ManagedPath or other test properties.
[group('mod')]
[working-directory('bazaarplusplus-mod')]
mod-test *args:
    bash ./run.sh test "$@"

# Run the existing source-only gate, including Rust/JS tests and frontend build.
[group('installer')]
[working-directory('bazaarplusplus-installer')]
installer-check:
    npm run verify -- --source-only
    npm run docs:check

# Generate bindings and run the Rust and JavaScript tests.
[group('installer')]
[working-directory('bazaarplusplus-installer')]
installer-test:
    npm test

# Start the installer frontend development server (not the desktop app).
[group('installer')]
[working-directory('bazaarplusplus-installer')]
installer-dev:
    npm run dev

# Check types, lint, formatting, and the production site build.
[group('site')]
[working-directory('bazaarplusplus-site')]
site-check:
    npm run typecheck
    npm run lint
    npm run format:check
    npm run build

# Run site tests once.
[group('site')]
[working-directory('bazaarplusplus-site')]
site-test:
    npm test

# Start the site development server.
[group('site')]
[working-directory('bazaarplusplus-site')]
site-dev:
    npm run dev

# Build the site without deploying it.
[group('site')]
[working-directory('bazaarplusplus-site')]
site-build:
    npm run build

# Run the Worker's type, lint, and formatting checks.
[group('server')]
[working-directory('bazaarplusplus-server')]
server-check:
    npm run check

# Run Worker tests in the local Cloudflare test runtime.
[group('server')]
[working-directory('bazaarplusplus-server')]
server-test:
    npm test

# Start the local Worker; requires its project-specific .dev.vars.
[group('server')]
[working-directory('bazaarplusplus-server')]
server-dev:
    npm run dev

# Check Python formatting, lint, and types using the locked environment.
[group('analyzer')]
[working-directory('bazaarplusplus-analyzer')]
analyzer-check:
    uv run --locked ruff format --check .
    uv run --locked ruff check .
    uv run --locked ty check

# Run analyzer tests and coverage without running its publication pipeline.
[group('analyzer')]
[working-directory('bazaarplusplus-analyzer')]
analyzer-test:
    uv run --locked pytest

# Project the authored VERSION and Payload Inventory into toolchain files.
[group('release')]
release-sync:
    node release.mjs sync

# Check product version and Payload Inventory alignment without changing them.
[group('release')]
release-check:
    node release.mjs check

# Prepare one platform Payload; optional arguments are MSBuild properties.
[group('release')]
release-prepare platform *args:
    node release.mjs prepare --platform "$1" -- "${@:2}"

# Prepare, verify, sign, and bundle on the native host; never upload.
[group('release')]
release-build platform *args:
    node release.mjs build --platform "$1" -- "${@:2}"

# Upload immutable platform artifacts without advancing latest.
[group('release')]
release-upload platform:
    node release.mjs upload --platform "$1"

# Advance latest only when both platforms have the same version and commit.
[group('release')]
release-promote:
    node release.mjs promote
