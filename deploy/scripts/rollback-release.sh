#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

require_docker_compose
prepare_release_directories
load_dotenv

cleanup_temp_release_env() {
  if [ -n "${TEMP_RELEASE_ENV_FILE:-}" ] && [ -f "$TEMP_RELEASE_ENV_FILE" ]; then
    rm -f "$TEMP_RELEASE_ENV_FILE"
  fi
}

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

load_release_images_from_manifest "$TARGET_RELEASE_FILE"
ensure_target_images_not_latest

TEMP_RELEASE_ENV_FILE=$(mktemp "$DEPLOY_DIR/.release-env.XXXXXX")
trap cleanup_temp_release_env EXIT HUP INT TERM
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

cp "$TEMP_RELEASE_ENV_FILE" "$DEPLOY_DIR/.env"

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

cp "$CURRENT_RELEASE_FILE" "$PREVIOUS_RELEASE_FILE"
cp "$STAGED_RELEASE_FILE" "$CURRENT_RELEASE_FILE"
history_file=$(record_release_history "$CURRENT_RELEASE_FILE" "$DEPLOY_RELEASE_ID")
cleanup_temp_release_env
unset COMPOSE_ENV_FILE
trap - EXIT HUP INT TERM

printf 'Application rollback completed: %s\n' "$DEPLOY_RELEASE_ID"
printf 'Current release manifest: %s\n' "$CURRENT_RELEASE_FILE"
printf 'Release history record: %s\n' "$history_file"
