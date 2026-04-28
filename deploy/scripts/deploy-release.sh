#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

RELEASE_TAG=${1:-${RELEASE_TAG:-}}
ensure_release_tag "$RELEASE_TAG"
require_docker_compose
prepare_release_directories
load_dotenv

cleanup_temp_release_env() {
  if [ -n "${TEMP_RELEASE_ENV_FILE:-}" ] && [ -f "$TEMP_RELEASE_ENV_FILE" ]; then
    rm -f "$TEMP_RELEASE_ENV_FILE"
  fi
}

"$SCRIPT_DIR/pre-deploy-check.sh" "$RELEASE_TAG"
"$SCRIPT_DIR/postgres-backup.sh"

PRE_DEPLOY_BACKUP_FILE=$(read_state_path "$BACKUP_STATE_FILE" || true)
if [ -z "$PRE_DEPLOY_BACKUP_FILE" ]; then
  printf 'Could not resolve the latest successful backup marker: %s\n' "$BACKUP_STATE_FILE" >&2
  exit 1
fi

resolve_release_images "$RELEASE_TAG"
ensure_target_images_not_latest

DEPLOY_RELEASE_ID="$RELEASE_TAG"
DEPLOY_GIT_SHA_VALUE=${DEPLOY_GIT_SHA:-unknown}
DEPLOY_TRIGGERED_BY_VALUE=${DEPLOY_TRIGGERED_BY:-manual}
DEPLOYED_AT_UTC_VALUE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

write_release_manifest \
  "$STAGED_RELEASE_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$PRE_DEPLOY_BACKUP_FILE"

TEMP_RELEASE_ENV_FILE=$(mktemp "$DEPLOY_DIR/.release-env.XXXXXX")
trap cleanup_temp_release_env EXIT HUP INT TERM
cp "$DEPLOY_DIR/.env" "$TEMP_RELEASE_ENV_FILE"
apply_app_images_to_dotenv "$TEMP_RELEASE_ENV_FILE"
export COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE"
load_dotenv

compose pull iiot-httpapi iiot-gateway iiot-dataworker iiot-migration iiot-web
compose up -d postgres redis-cache rabbitmq seq >/dev/null
compose run --rm iiot-migration
compose up -d iiot-httpapi iiot-gateway iiot-dataworker iiot-web nginx-gateway >/dev/null
COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE" "$SCRIPT_DIR/post-deploy-check.sh"

cp "$TEMP_RELEASE_ENV_FILE" "$DEPLOY_DIR/.env"

if [ -f "$CURRENT_RELEASE_FILE" ]; then
  cp "$CURRENT_RELEASE_FILE" "$PREVIOUS_RELEASE_FILE"
fi

cp "$STAGED_RELEASE_FILE" "$CURRENT_RELEASE_FILE"
history_file=$(record_release_history "$CURRENT_RELEASE_FILE" "$DEPLOY_RELEASE_ID")
cleanup_temp_release_env
unset COMPOSE_ENV_FILE
trap - EXIT HUP INT TERM

printf 'Release deployed successfully: %s\n' "$DEPLOY_RELEASE_ID"
printf 'Current release manifest: %s\n' "$CURRENT_RELEASE_FILE"
printf 'Release history record: %s\n' "$history_file"
