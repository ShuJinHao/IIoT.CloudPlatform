#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

RELEASE_TAG=${RELEASE_TAG:-}
REQUESTED_SERVICES=${DEPLOY_SERVICES:-}

while [ "$#" -gt 0 ]
do
  case "$1" in
    --services)
      shift
      REQUESTED_SERVICES=${1:-}
      ;;
    --services=*)
      REQUESTED_SERVICES=${1#--services=}
      ;;
    -*)
      printf 'Unknown deploy-release option: %s\n' "$1" >&2
      exit 64
      ;;
    *)
      if [ -n "$RELEASE_TAG" ]; then
        if [ "$RELEASE_TAG" != "$1" ]; then
          printf 'Unexpected deploy-release argument: %s\n' "$1" >&2
          exit 64
        fi
      else
        RELEASE_TAG=$1
      fi
      ;;
  esac
  shift
done

ensure_release_tag "$RELEASE_TAG"
require_docker_compose
prepare_release_directories
load_dotenv

normalize_services() {
  services_input=${1:-}
  if [ -z "$services_input" ]; then
    printf '%s\n' "iiot-httpapi iiot-gateway iiot-dataworker iiot-migration iiot-web"
    return
  fi

  normalized_services=""
  for service in $(printf '%s' "$services_input" | tr ',' ' ')
  do
    case "$service" in
      httpapi|iiot-httpapi)
        normalized=iiot-httpapi
        ;;
      gateway|iiot-gateway)
        normalized=iiot-gateway
        ;;
      dataworker|iiot-dataworker)
        normalized=iiot-dataworker
        ;;
      migration|iiot-migration)
        normalized=iiot-migration
        ;;
      web|iiot-web)
        normalized=iiot-web
        ;;
      *)
        printf 'Unsupported deploy service: %s\n' "$service" >&2
        exit 64
        ;;
    esac

    case " $normalized_services " in
      *" $normalized "*)
        ;;
      *)
        normalized_services="$normalized_services $normalized"
        ;;
    esac
  done

  printf '%s\n' "$(printf '%s' "$normalized_services" | awk '{$1=$1; print}')"
}

image_key_for_service() {
  case "$1" in
    iiot-httpapi)
      printf '%s\n' IIOT_HTTPAPI_IMAGE
      ;;
    iiot-gateway)
      printf '%s\n' IIOT_GATEWAY_IMAGE
      ;;
    iiot-dataworker)
      printf '%s\n' IIOT_DATAWORKER_IMAGE
      ;;
    iiot-migration)
      printf '%s\n' IIOT_MIGRATION_IMAGE
      ;;
    iiot-web)
      printf '%s\n' IIOT_WEB_IMAGE
      ;;
    *)
      printf 'Unsupported deploy service: %s\n' "$1" >&2
      exit 64
      ;;
  esac
}

SELECTED_SERVICES=$(normalize_services "$REQUESTED_SERVICES")
SELECTED_IMAGE_KEYS=""
RUNTIME_SELECTED_SERVICES=""
RUN_MIGRATION=false
for service in $SELECTED_SERVICES
do
  image_key=$(image_key_for_service "$service")
  SELECTED_IMAGE_KEYS="$SELECTED_IMAGE_KEYS $image_key"
  if [ "$service" = "iiot-migration" ]; then
    RUN_MIGRATION=true
  else
    RUNTIME_SELECTED_SERVICES="$RUNTIME_SELECTED_SERVICES $service"
  fi
done
SELECTED_IMAGE_KEYS=$(printf '%s' "$SELECTED_IMAGE_KEYS" | awk '{$1=$1; print}')
RUNTIME_SELECTED_SERVICES=$(printf '%s' "$RUNTIME_SELECTED_SERVICES" | awk '{$1=$1; print}')

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

resolve_release_images_for_keys "$RELEASE_TAG" $SELECTED_IMAGE_KEYS
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
apply_app_images_to_dotenv_for_keys "$TEMP_RELEASE_ENV_FILE" $SELECTED_IMAGE_KEYS
export COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE"
load_dotenv

compose pull $SELECTED_SERVICES
compose up -d postgres redis-cache rabbitmq seq >/dev/null
if [ "$RUN_MIGRATION" = "true" ]; then
  compose run -T --rm iiot-migration
fi
if [ -n "$RUNTIME_SELECTED_SERVICES" ]; then
  compose up -d $RUNTIME_SELECTED_SERVICES >/dev/null
fi
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
