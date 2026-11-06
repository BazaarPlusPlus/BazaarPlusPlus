set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Each project owns a module; its recipes run in that project's directory.
# Every module file repeats the shell and positional-arguments settings, which
# modules do not inherit.
mod mod 'bazaarplusplus-mod/mod.just'
mod installer 'bazaarplusplus-installer/installer.just'
mod site 'bazaarplusplus-site/site.just'
mod server 'bazaarplusplus-server/server.just'
mod analyzer 'bazaarplusplus-analyzer/analyzer.just'
mod android 'bazaarplusplus-android/android.just'
mod release 'release/release.just'
mod design 'design/design.just'

# Root tooling owns its dependencies and its TypeScript, lint, test and formatting configuration.
root_tooling := "package.json package-lock.json .prettierrc.json .oxlintrc.json tsconfig.json vitest.config.ts release.ts 'release/**/*.{ts,json}' 'scripts/**/*.ts' '.github/scripts/*.ts' 'design/*.ts' '.agents/skills/*/scripts/*.ts'"

# `just --fmt` does not descend into modules, so format each file explicitly.
just_files := "JUSTFILE bazaarplusplus-mod/mod.just bazaarplusplus-installer/installer.just bazaarplusplus-site/site.just bazaarplusplus-server/server.just bazaarplusplus-analyzer/analyzer.just bazaarplusplus-android/android.just release/release.just design/design.just"

# List commands without building or publishing anything.
default:
    @{{ quote(just_executable()) }} --list --list-submodules

# Check all projects without signing, publishing, or deploying to the game.
[group('workspace')]
check: commands-check design::check release::check mod::check installer::check site::check server::check analyzer::check android::check

# Run all project tests without publishing data or deploying to the game.
[group('workspace')]
test: commands-check release::test mod::test installer::test site::test server::test analyzer::test android::test

# Format every project in place, then re-project release files.
[group('workspace')]
fmt: && mod::fmt installer::fmt site::fmt server::fmt analyzer::fmt release::sync
    for file in {{ just_files }}; do {{ quote(just_executable()) }} --justfile "$file" --fmt; done
    npm exec -- prettier --config .prettierrc.json --write {{ root_tooling }}

# Install the workspace Git hooks (requires root npm dependencies).
[group('workspace')]
hooks-install:
    node scripts/install-hooks.ts

# Connect shared local configuration, install locked dependencies and Git hooks.
[group('workspace')]
setup *args:
    node scripts/workspace.ts setup "$@"

# Inventory local prerequisites without printing secret values or publishing.
[group('workspace')]
doctor:
    node scripts/workspace.ts doctor

# Inventory the Android AVD matrix and its historical login checks.
[group('workspace')]
list-devices:
    node scripts/setup-device.ts status

# Prepare each Android AVD and verify its game login; --help lists the options.
[group('workspace')]
setup-device *args:
    node scripts/setup-device.ts init "$@"

# Open panels, capture each matrix device's screens and build the walkthrough HTML; --help lists the commands.
[group('workspace')]
walkthrough *args:
    node scripts/walkthrough.ts "$@"

# Run explicit verification recipes serially and retain their final exit status, SHA and logs.
[group('workspace')]
verify +recipes:
    node scripts/verify.ts "$@"

# Run an explicit command with one configuration profile, from the repo root.
[group('workspace')]
with-config profile +args:
    node scripts/workspace.ts run "$1" -- "${@:2}"

# List, by name only, which GitHub secrets and variables exist and which have a local value.
[group('workspace')]
secrets-check *args:
    node scripts/workspace.ts secrets check "$@"

# Copy every locally present GitHub secret and variable through gh; [--dependabot].
[group('workspace')]
secrets-sync *args:
    node scripts/workspace.ts secrets push "$@"

# Check formatting, types and lint of the root tooling, then run its tests, which exercise command routing with isolated tool stubs.
[group('workspace')]
commands-check:
    for file in {{ just_files }}; do {{ quote(just_executable()) }} --justfile "$file" --fmt --check; done
    npm exec -- prettier --config .prettierrc.json --check {{ root_tooling }}
    npm run typecheck
    npm run lint
    npm test -- --project tooling
