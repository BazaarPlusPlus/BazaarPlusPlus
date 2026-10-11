#!/usr/bin/env bash
# Mirrors the analyzer Data Root (BPP_DATA_ROOT) to and from its private R2
# state bucket for .github/workflows/analyzer.yml, which runs on a fresh runner
# every time. Hourly commits and day seals are immutable and carry their own
# sha256 (bazaarplusplus-analyzer/docs/architecture.md), so `bpp verify --deep`
# after a pull proves the restore. Lock and staging entries are never mirrored.
#
#   analyzer-state.sh pull       copy the bucket into an empty Data Root
#   analyzer-state.sh push       mirror the Data Root, deleting what retention
#                                pruned; only after a successful run
#   analyzer-state.sh push-new   upload new and changed files, delete nothing;
#                                after a failed run
#
# Reads BPP_DATA_ROOT, BPP_ANALYZER_STATE_BUCKET, CLOUDFLARE_ACCOUNT_ID and
# the derived AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY.
set -euo pipefail

: "${BPP_DATA_ROOT:?}" "${BPP_ANALYZER_STATE_BUCKET:?}" "${CLOUDFLARE_ACCOUNT_ID:?}"
: "${AWS_ACCESS_KEY_ID:?}" "${AWS_SECRET_ACCESS_KEY:?}"

# R2 rejects the default CRC checksums newer AWS CLI releases send.
export AWS_REQUEST_CHECKSUM_CALCULATION=when_required
export AWS_RESPONSE_CHECKSUM_VALIDATION=when_required
export AWS_DEFAULT_REGION=auto

remote="s3://${BPP_ANALYZER_STATE_BUCKET}"
common=(
  --endpoint-url "https://${CLOUDFLARE_ACCOUNT_ID,,}.r2.cloudflarestorage.com"
  --only-show-errors
  --no-progress
  # DirectoryLock, durable.atomic_replace and FactStore staging names.
  --exclude '.lock*'
  --exclude '*.tmp-*'
  --exclude 'duckdb-tmp/*'
)

case "${1:-}" in
  pull)
    mkdir -p "$BPP_DATA_ROOT"
    if [ -n "$(ls -A "$BPP_DATA_ROOT")" ]; then
      echo "::error::$BPP_DATA_ROOT must be empty before a pull"
      exit 1
    fi
    aws s3 sync "$remote" "$BPP_DATA_ROOT" "${common[@]}"
    ;;
  push)
    aws s3 sync "$BPP_DATA_ROOT" "$remote" "${common[@]}" --delete
    ;;
  push-new)
    aws s3 sync "$BPP_DATA_ROOT" "$remote" "${common[@]}"
    ;;
  *)
    echo "usage: analyzer-state.sh pull|push|push-new" >&2
    exit 2
    ;;
esac
