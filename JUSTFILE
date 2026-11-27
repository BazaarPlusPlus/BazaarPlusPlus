set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Each project owns a module; its recipes run in that project's directory.
# Every module file repeats the shell and positional-arguments settings, which
# modules do not inherit.
mod mod 'bazaarplusplus-mod/mod.just'
mod installer 'bazaarplusplus-installer/installer.just'
mod site 'bazaarplusplus-site/site.just'
mod ui-docs 'bazaarplusplus-ui-docs/ui-docs.just'
mod server 'bazaarplusplus-server/server.just'
mod bot 'bazaarplusplus-bot/bot.just'
mod analyzer 'bazaarplusplus-analyzer/analyzer.just'
mod android 'bazaarplusplus-android/android.just'
mod release 'release/release.just'
mod design 'design/design.just'
mod runs 'packages/runs/runs.just'
mod ui 'packages/ui/ui.just'

# `just --fmt` does not descend into modules, so format each file explicitly.
just_files := "JUSTFILE bazaarplusplus-mod/mod.just bazaarplusplus-installer/installer.just bazaarplusplus-site/site.just bazaarplusplus-ui-docs/ui-docs.just bazaarplusplus-server/server.just bazaarplusplus-bot/bot.just bazaarplusplus-analyzer/analyzer.just bazaarplusplus-android/android.just release/release.just design/design.just packages/runs/runs.just packages/ui/ui.just"

# List commands without building or publishing anything.
default:
    @{{ quote(just_executable()) }} --list --list-submodules

# Check all projects without signing, publishing, or deploying to the game.
[group('workspace')]
check: commands-check design::check runs::check ui::check release::check mod::check installer::check site::check ui-docs::check server::check bot::check analyzer::check android::check

# Run all project tests without publishing data or deploying to the game.
[group('workspace')]
test: commands-check runs::test ui::test release::test mod::test installer::test site::test ui-docs::test server::test bot::test analyzer::test android::test

# Format every project in place, then re-project release files.
[group('workspace')]
fmt: && mod::fmt ui::fmt installer::fmt site::fmt ui-docs::fmt server::fmt bot::fmt analyzer::fmt release::sync
    for file in {{ just_files }}; do {{ quote(just_executable()) }} --justfile "$file" --fmt; done
    bun run vp fmt --write

# Install the workspace Git hooks (requires root Bun dependencies).
[group('workspace')]
hooks-install:
    bun scripts/install-hooks.ts

# Connect shared local configuration, install locked dependencies and Git hooks.
[group('workspace')]
setup *args:
    bun scripts/workspace.ts setup "$@"

# Inventory local prerequisites without printing secret values or publishing.
[group('workspace')]
doctor:
    bun scripts/workspace.ts doctor

# Copy root package.json vite-plus into site and installer and into every vite alias, then reinstall each.
[group('workspace')]
vite-plus-sync:
    bun scripts/vite-plus.ts

# Inventory the Android AVD matrix and its historical login checks.
[group('workspace')]
list-devices:
    bun scripts/android/setup-device.ts status

# Prepare each Android AVD and verify its game login; --help lists the options.
[group('workspace')]
setup-device *args:
    bun scripts/android/setup-device.ts init "$@"

# Open panels, capture each matrix device's screens and build the walkthrough HTML; --help lists the commands.
[group('workspace')]
walkthrough *args:
    bun scripts/android/walkthrough.ts "$@"

# Development only: copy recent cloud runs into desktop or Android history, or clear them; just mod::history-sample has the usage.
[group('workspace')]
history-sample *args:
    bun scripts/workspace.ts run analyzer -- bun scripts/history-sample.ts "$@"

# Extract Simplified Chinese hero, card, skill and enchantment names from the local game cache.
[group('workspace')]
zh-names out *args:
    bun scripts/dev/zh-names.ts "$@"

# Run explicit verification recipes serially and retain their final exit status, SHA and logs.
[group('workspace')]
verify +recipes:
    bun scripts/verify.ts "$@"

# Run an explicit command with one configuration profile, from the repo root.
[group('workspace')]
with-config profile +args:
    bun scripts/workspace.ts run "$1" -- "${@:2}"

# List, by name only, which GitHub secrets and variables exist and which have a local value.
[group('workspace')]
secrets-check *args:
    bun scripts/workspace.ts secrets check "$@"

# Copy every locally present GitHub secret and variable through gh; [--dependabot].
[group('workspace')]
secrets-sync *args:
    bun scripts/workspace.ts secrets push "$@"

# Check formatting, types and lint of the root tooling, then run its tests, which exercise command routing with isolated tool stubs.
[group('workspace')]
commands-check:
    for file in {{ just_files }}; do {{ quote(just_executable()) }} --justfile "$file" --fmt --check; done
    bun run vp fmt --check
    bun run typecheck
    bun run lint
    bun run test --project tooling
