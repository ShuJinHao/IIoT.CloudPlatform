#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

current_user=$(id -un 2>/dev/null || id -u)
current_group=$(id -gn 2>/dev/null || id -g)

fail_release_state() {
  path=$1
  reason=$2

  printf 'Cloud release state is not accessible to standard non-root deploy user %s:%s: %s (%s)\n' \
    "$current_user" \
    "$current_group" \
    "$path" \
    "$reason" >&2
  printf 'Fix ownership, for example: chown -R %s:%s %s\n' \
    "$current_user" \
    "$current_group" \
    "$RELEASES_DIR" >&2
  printf 'Fix modes, for example: find %s -type d -exec chmod 755 {} + && find %s -type f -exec chmod 600 {} +\n' \
    "$RELEASES_DIR" \
    "$RELEASES_DIR" >&2
  printf 'If a root emergency path was used, restore owner/mode first, then rerun the standard non-root preflight.\n' >&2
  exit 73
}

ensure_directory_access() {
  path=$1

  if [ ! -d "$path" ]; then
    fail_release_state "$path" "directory missing"
  fi

  if [ ! -r "$path" ] || [ ! -w "$path" ] || [ ! -x "$path" ]; then
    fail_release_state "$path" "directory must be readable, writable and searchable"
  fi
}

ensure_file_access_if_exists() {
  path=$1

  if [ -e "$path" ] && { [ ! -r "$path" ] || [ ! -w "$path" ]; }; then
    fail_release_state "$path" "file must be readable and writable"
  fi
}

if ! mkdir -p "$RELEASE_HISTORY_DIR" 2>/dev/null; then
  fail_release_state "$RELEASE_HISTORY_DIR" "cannot create or update release history directory"
fi

ensure_directory_access "$RELEASES_DIR"
ensure_directory_access "$RELEASE_HISTORY_DIR"
ensure_file_access_if_exists "$CURRENT_RELEASE_FILE"
ensure_file_access_if_exists "$PREVIOUS_RELEASE_FILE"
ensure_file_access_if_exists "$STAGED_RELEASE_FILE"
ensure_file_access_if_exists "$CURRENT_RELEASE_SUMMARY_FILE"
ensure_file_access_if_exists "$RELEASE_IMAGE_ENV_FILE"
ensure_file_access_if_exists "$STAGED_RELEASE_IMAGE_ENV_FILE"

printf 'Cloud release state access check passed: %s\n' "$RELEASES_DIR"
