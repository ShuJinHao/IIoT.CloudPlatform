#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
CANDIDATE_ENV=${1:-}
EXPECTED_SHA256=${2:-}

. "$SCRIPT_DIR/release-common.sh"

if [ -z "$CANDIDATE_ENV" ] || [ ! -f "$CANDIDATE_ENV" ] || [ -L "$CANDIDATE_ENV" ]; then
  printf 'Usage: update-deploy-env.sh <candidate-env-file> <expected-current-sha256>\n' >&2
  exit 64
fi
case "$EXPECTED_SHA256" in
  ''|*[!0-9a-f]*)
    printf 'Expected current .env SHA-256 must be lowercase hexadecimal.\n' >&2
    exit 64
    ;;
esac
[ "${#EXPECTED_SHA256}" -eq 64 ] || {
  printf 'Expected current .env SHA-256 must contain 64 characters.\n' >&2
  exit 64
}

CONFIG_LOCK_FILE=$(resolve_managed_lock_file \
  "$CLOUD_CONFIG_LOCK_FILE_DEFAULT" \
  "$DEPLOY_DIR/.cloud-config.lock")
CONFIG_LOCK_ACQUIRED=0
cleanup_config_update() {
  update_status=$?
  trap - EXIT HUP INT TERM
  if [ "$CONFIG_LOCK_ACQUIRED" -eq 1 ] \
    && [ "$(managed_lock_read_field "${CONFIG_LOCK_FILE}.d" pid)" = "$$" ]; then
    release_managed_lock "$CONFIG_LOCK_FILE" || true
  fi
  exit "$update_status"
}
trap cleanup_config_update EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

acquire_strict_managed_lock \
  "$CONFIG_LOCK_FILE" \
  cloud-config-update \
  operator-config \
  validate \
  update-deploy-env
CONFIG_LOCK_ACQUIRED=1

current_sha256=$(sha256_file "$DEPLOY_DIR/.env")
if [ "$current_sha256" != "$EXPECTED_SHA256" ]; then
  printf 'Operator configuration changed before the supported update acquired its lock.\n' >&2
  exit 75
fi

# Parse in an isolated shell. Errors contain only file/line/category.
DEPLOY_DIR="$DEPLOY_DIR" sh -c '. "$1"; load_dotenv "$2"' \
  sh "$SCRIPT_DIR/release-common.sh" "$CANDIDATE_ENV"
update_managed_lock_phase "$CONFIG_LOCK_FILE" replace
atomic_copy_file "$CANDIDATE_ENV" "$DEPLOY_DIR/.env" 600
release_managed_lock "$CONFIG_LOCK_FILE"
CONFIG_LOCK_ACQUIRED=0
trap - EXIT HUP INT TERM
printf 'Cloud operator configuration updated through the strict config lock.\n'
