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
mod release 'release/release.just'

# Root tooling owns its dependencies and formatting configuration.
root_js := "package.json package-lock.json .prettierrc.json release.mjs 'release/**/*.{mjs,ts,json}' 'scripts/**/*.mjs'"

# `just --fmt` does not descend into modules, so format each file explicitly.
just_files := "JUSTFILE bazaarplusplus-mod/mod.just bazaarplusplus-installer/installer.just bazaarplusplus-site/site.just bazaarplusplus-server/server.just bazaarplusplus-analyzer/analyzer.just release/release.just"

# List commands without building or publishing anything.
default:
    @{{ quote(just_executable()) }} --list --list-submodules

# Check all projects without signing, publishing, or deploying to the game.
[group('workspace')]
check: commands-check release::check mod::check installer::check site::check server::check analyzer::check

# Run all project tests without publishing data or deploying to the game.
[group('workspace')]
test: commands-check release::test mod::test installer::test site::test server::test analyzer::test

# Format every project in place, then re-project release files.
[group('workspace')]
fmt: && mod::fmt installer::fmt site::fmt server::fmt analyzer::fmt release::sync
    for file in {{ just_files }}; do {{ quote(just_executable()) }} --justfile "$file" --fmt; done
    npm exec -- prettier --config .prettierrc.json --write {{ root_js }}

# Install the workspace Git hooks (requires root npm dependencies).
[group('workspace')]
hooks-install:
    npm exec -- lefthook install

# Check formatting and exercise command routing with isolated tool stubs.
[group('workspace')]
commands-check:
    for file in {{ just_files }}; do {{ quote(just_executable()) }} --justfile "$file" --fmt --check; done
    npm exec -- prettier --config .prettierrc.json --check {{ root_js }}
    node --test scripts/just.test.mjs
