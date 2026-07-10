#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

require_docker_compose
prepare_release_directories
ROLLBACK_LOCK_FILE=$(resolve_managed_lock_file \
  "$CLOUD_RELEASE_LOCK_FILE_DEFAULT" \
  "$DEPLOY_DIR/.cloud-release.lock")
readonly ROLLBACK_LOCK_FILE
ROLLBACK_LOCK_ACQUIRED=0
ROLLBACK_CONFIG_LOCK_FILE=$(resolve_managed_lock_file \
  "$CLOUD_CONFIG_LOCK_FILE_DEFAULT" \
  "$DEPLOY_DIR/.cloud-config.lock")
readonly ROLLBACK_CONFIG_LOCK_FILE
ROLLBACK_CONFIG_LOCK_ACQUIRED=0

cleanup_temp_release_env() {
  if [ -n "${TEMP_RELEASE_ENV_FILE:-}" ] && [ -f "$TEMP_RELEASE_ENV_FILE" ]; then
    rm -f "$TEMP_RELEASE_ENV_FILE"
  fi
}

cleanup_rollback() {
  rollback_status=$?
  trap - EXIT HUP INT TERM
  cleanup_temp_release_env
  if [ "$ROLLBACK_CONFIG_LOCK_ACQUIRED" -eq 1 ] \
    && [ "$(managed_lock_read_field "${ROLLBACK_CONFIG_LOCK_FILE}.d" pid)" = "$$" ]; then
    release_managed_lock "$ROLLBACK_CONFIG_LOCK_FILE" || true
  fi
  if [ "$ROLLBACK_LOCK_ACQUIRED" -eq 1 ] \
    && [ -r "${ROLLBACK_LOCK_FILE}.d/pid" ] \
    && [ "$(managed_lock_read_field "${ROLLBACK_LOCK_FILE}.d" pid)" = "$$" ]; then
    release_managed_lock "$ROLLBACK_LOCK_FILE" || true
  fi
  exit "$rollback_status"
}

trap cleanup_rollback EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

TARGET_RELEASE_FILE=$(resolve_release_file_path "${1:-}")
if [ ! -f "$TARGET_RELEASE_FILE" ]; then
  printf 'Rollback release file not found: %s\n' "$TARGET_RELEASE_FILE" >&2
  exit 66
fi

if [ ! -f "$CURRENT_RELEASE_FILE" ]; then
  printf 'Current release manifest is missing: %s\n' "$CURRENT_RELEASE_FILE" >&2
  exit 66
fi

TARGET_RELEASE_ID=$(read_manifest_value "$TARGET_RELEASE_FILE" "DEPLOY_RELEASE_ID")
TARGET_DEPLOY_GIT_SHA=$(read_manifest_value "$TARGET_RELEASE_FILE" "DEPLOY_GIT_SHA")
TARGET_PRE_DEPLOY_BACKUP_FILE=$(read_manifest_value "$TARGET_RELEASE_FILE" "PRE_DEPLOY_BACKUP_FILE")

if [ -z "$TARGET_RELEASE_ID" ]; then
  printf 'Rollback release file is missing DEPLOY_RELEASE_ID: %s\n' "$TARGET_RELEASE_FILE" >&2
  exit 66
fi

acquire_managed_lock \
  "$ROLLBACK_LOCK_FILE" \
  cloud-rollback \
  "$TARGET_RELEASE_ID" \
  rollback \
  rollback-release
ROLLBACK_LOCK_ACQUIRED=1
acquire_strict_managed_lock \
  "$ROLLBACK_CONFIG_LOCK_FILE" \
  cloud-operator-config \
  "$TARGET_RELEASE_ID" \
  rollback \
  rollback-release
ROLLBACK_CONFIG_LOCK_ACQUIRED=1
load_dotenv "$DEPLOY_DIR/.env"
DEPLOY_ORIGINAL_ENV_SHA256=$(sha256_file "$DEPLOY_DIR/.env")
DEPLOY_CONFIG_LOCK_TEST_HOOK=${DEPLOY_CONFIG_LOCK_TEST_HOOK:-}
readonly DEPLOY_ORIGINAL_ENV_SHA256 DEPLOY_CONFIG_LOCK_TEST_HOOK
load_release_images_from_manifest "$TARGET_RELEASE_FILE"
ensure_target_images_not_latest

TEMP_RELEASE_ENV_FILE=$(mktemp "$DEPLOY_DIR/.release-env.XXXXXX")
cp "$DEPLOY_DIR/.env" "$TEMP_RELEASE_ENV_FILE"
apply_app_images_to_dotenv "$TEMP_RELEASE_ENV_FILE"
export COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE"
load_dotenv

compose pull iiot-httpapi iiot-gateway iiot-dataworker iiot-web
compose up -d iiot-httpapi iiot-gateway iiot-dataworker iiot-web nginx-gateway >/dev/null

if ! COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE" "$SCRIPT_DIR/post-deploy-check.sh"; then
  printf 'Application rollback to %s did not recover the stack. Transfer to the existing database recovery flow or clear and rebuild the database in the current pre-launch environment.\n' "$TARGET_RELEASE_ID" >&2
  exit 1
fi

current_env_sha256=$(sha256_file "$DEPLOY_DIR/.env")
if [ "$current_env_sha256" != "$DEPLOY_ORIGINAL_ENV_SHA256" ]; then
  printf 'Cloud rollback stopped because .env changed concurrently; operator changes were preserved.\n' >&2
  exit 75
fi
if [ -n "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ]; then
  if [ "${DEPLOY_CONFIG_LOCK_TEST_MODE:-}" != enabled ] \
    || [ ! -f "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ] \
    || [ -L "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ] \
    || [ ! -x "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ]; then
    printf 'Cloud config-lock test hook was rejected.\n' >&2
    exit 64
  fi
  "$DEPLOY_CONFIG_LOCK_TEST_HOOK" "$DEPLOY_DIR" "$DEPLOY_ORIGINAL_ENV_SHA256"
fi
[ "$(sha256_file "$DEPLOY_DIR/.env")" = "$DEPLOY_ORIGINAL_ENV_SHA256" ] || {
  printf 'Unsupported direct .env write detected after final hash; operator content was not overwritten.\n' >&2
  exit 75
}

ROLLBACK_BACKUP_FILE=$(read_state_path "$BACKUP_STATE_FILE" || true)
if [ -z "$ROLLBACK_BACKUP_FILE" ]; then
  ROLLBACK_BACKUP_FILE=${TARGET_PRE_DEPLOY_BACKUP_FILE:-unavailable}
fi

DEPLOY_RELEASE_ID="$TARGET_RELEASE_ID"
DEPLOY_GIT_SHA_VALUE=${TARGET_DEPLOY_GIT_SHA:-unknown}
DEPLOY_TRIGGERED_BY_VALUE=${DEPLOY_TRIGGERED_BY:-manual}
DEPLOYED_AT_UTC_VALUE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

write_release_manifest \
  "$STAGED_RELEASE_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$ROLLBACK_BACKUP_FILE"
write_release_image_env "$STAGED_RELEASE_IMAGE_ENV_FILE"

atomic_copy_file "$CURRENT_RELEASE_FILE" "$PREVIOUS_RELEASE_FILE" 600
atomic_copy_file "$STAGED_RELEASE_IMAGE_ENV_FILE" "$RELEASE_IMAGE_ENV_FILE" 600
atomic_copy_file "$STAGED_RELEASE_FILE" "$CURRENT_RELEASE_FILE" 600
history_file=$(record_release_history "$CURRENT_RELEASE_FILE" "$DEPLOY_RELEASE_ID")
cleanup_temp_release_env
unset COMPOSE_ENV_FILE
release_managed_lock "$ROLLBACK_CONFIG_LOCK_FILE"
ROLLBACK_CONFIG_LOCK_ACQUIRED=0
release_managed_lock "$ROLLBACK_LOCK_FILE"
ROLLBACK_LOCK_ACQUIRED=0
trap - EXIT HUP INT TERM

printf 'Application rollback completed: %s\n' "$DEPLOY_RELEASE_ID"
printf 'Current release manifest: %s\n' "$CURRENT_RELEASE_FILE"
printf 'Release history record: %s\n' "$history_file"
