#!/bin/bash
# Cloud sessions: make the macos-online Game Assembly Snapshot available so the
# mod builds on Linux (build/ManagedPath.props defaults Linux to the macos lock
# entry). The environment's setup script installs the toolchain; this step runs
# here because only the session can read CLOUDFLARE_API_TOKEN. A verified local
# snapshot makes the fetch a no-op, and a lock update fetches the new one.
# Stdout becomes session context, so the fetch log goes to stderr; a failure
# warns and never blocks the session.
set -uo pipefail

[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0
cd "${CLAUDE_PROJECT_DIR:-.}"

skip() {
  echo "Game-libs: $1; the mod will not build until 'just mod::fetch macos online' succeeds."
  exit 0
}

command -v just >/dev/null 2>&1 || skip "just is not installed (environment setup script)"
[ -n "${CLOUDFLARE_API_TOKEN:-}" ] && [ -n "${CLOUDFLARE_ACCOUNT_ID:-}" ] ||
  skip "CLOUDFLARE_API_TOKEN or CLOUDFLARE_ACCOUNT_ID is not set in the cloud environment"

if managed="$(just mod::fetch macos online | tail -n 1)" && [ -d "$managed" ]; then
  echo "Game-libs: macos-online snapshot ready at $managed."
else
  skip "fetching the macos-online snapshot failed (see the hook's stderr)"
fi
