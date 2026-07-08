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

service_for_image_key() {
  case "$1" in
    IIOT_HTTPAPI_IMAGE)
      printf '%s\n' iiot-httpapi
      ;;
    IIOT_GATEWAY_IMAGE)
      printf '%s\n' iiot-gateway
      ;;
    IIOT_DATAWORKER_IMAGE)
      printf '%s\n' iiot-dataworker
      ;;
    IIOT_MIGRATION_IMAGE)
      printf '%s\n' iiot-migration
      ;;
    IIOT_WEB_IMAGE)
      printf '%s\n' iiot-web
      ;;
    *)
      printf 'Unsupported image key: %s\n' "$1" >&2
      exit 64
      ;;
  esac
}

image_key_is_selected() {
  key=$1
  case " $SELECTED_IMAGE_KEYS " in
    *" $key "*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

image_ref_is_placeholder() {
  image_ref=${1:-}
  case "$image_ref" in
    ""|*:sha-0000000000000000)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

hydrate_unselected_images_from_running_containers() {
  for key in $APP_IMAGE_KEYS
  do
    if image_key_is_selected "$key"; then
      continue
    fi

    eval "image_ref=\${$key:-}"
    if ! image_ref_is_placeholder "$image_ref"; then
      continue
    fi

    service=$(service_for_image_key "$key")
    container_id=$(compose ps -q "$service" 2>/dev/null | head -n 1 || true)
    if [ -z "$container_id" ]; then
      if [ "$key" = "IIOT_MIGRATION_IMAGE" ]; then
        dotenv_image_ref=$(read_manifest_value "$DEPLOY_DIR/.env" "$key")
        if ! image_ref_is_placeholder "$dotenv_image_ref"; then
          eval "$key=\$dotenv_image_ref"
          continue
        fi

        printf 'warning: keeping placeholder image for unselected one-shot migration service: %s\n' "$key" >&2
        continue
      fi

      printf 'Current release image is not usable and no running container was found: %s=%s\n' "$key" "$image_ref" >&2
      exit 66
    fi

    running_image=$(docker inspect --format '{{.Config.Image}}' "$container_id" 2>/dev/null || true)
    if [ -z "$running_image" ]; then
      printf 'Could not inspect running image for unselected service: %s\n' "$service" >&2
      exit 66
    fi

    eval "$key=\$running_image"
  done
}

ensure_nginx_gateway_if_needed() {
  case " $RUNTIME_SELECTED_SERVICES " in
    *" iiot-web "*|*" iiot-gateway "*)
      nginx_gateway_container=$(compose ps -q nginx-gateway 2>/dev/null || true)
      if [ -n "$nginx_gateway_container" ]; then
        printf 'Restarting nginx-gateway to refresh upstream DNS...\n'
        compose restart nginx-gateway >/dev/null
      else
        printf 'Starting nginx-gateway because the selected release affects browser traffic...\n'
        compose up -d nginx-gateway >/dev/null
      fi
      ;;
  esac
}

requires_edge_installer_catalog_verification() {
  case " $RUNTIME_SELECTED_SERVICES " in
    *" iiot-httpapi "*|*" iiot-gateway "*|*" iiot-web "*)
      return 0
      ;;
    *)
      return 1
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

if [ -n "$REQUESTED_SERVICES" ]; then
  if [ ! -f "$CURRENT_RELEASE_FILE" ]; then
    printf 'Incremental deploy requires an existing current release: %s\n' "$CURRENT_RELEASE_FILE" >&2
    exit 64
  fi

  load_release_images_from_manifest "$CURRENT_RELEASE_FILE"
  hydrate_unselected_images_from_running_containers
fi

resolve_release_images_for_keys "$RELEASE_TAG" $SELECTED_IMAGE_KEYS
ensure_target_images_not_latest

DEPLOY_RELEASE_ID="$RELEASE_TAG"
DEPLOY_GIT_SHA_VALUE=${DEPLOY_GIT_SHA:-unknown}
DEPLOY_TRIGGERED_BY_VALUE=${DEPLOY_TRIGGERED_BY:-manual}
DEPLOYED_AT_UTC_VALUE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
DEPLOY_RELEASE_NOTES_VALUE=${DEPLOY_RELEASE_NOTES:-}

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

compose pull $SELECTED_SERVICES
compose up -d postgres redis-cache rabbitmq seq >/dev/null
if [ "$RUN_MIGRATION" = "true" ]; then
  compose run -T --rm iiot-migration
fi
if [ -n "$RUNTIME_SELECTED_SERVICES" ]; then
  if [ -n "$REQUESTED_SERVICES" ]; then
    compose up -d --no-deps $RUNTIME_SELECTED_SERVICES >/dev/null
  else
    compose up -d $RUNTIME_SELECTED_SERVICES >/dev/null
  fi
fi
ensure_nginx_gateway_if_needed
post_deploy_verify_edge_installer_catalog=${POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG:-0}
post_deploy_require_edge_installer_catalog=${POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG:-0}
if requires_edge_installer_catalog_verification; then
  printf 'Edge installer catalog verification is required for selected Cloud download services: %s\n' "$RUNTIME_SELECTED_SERVICES"
  post_deploy_verify_edge_installer_catalog=1
  post_deploy_require_edge_installer_catalog=1
fi
COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE" \
  POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG="$post_deploy_verify_edge_installer_catalog" \
  POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG="$post_deploy_require_edge_installer_catalog" \
  "$SCRIPT_DIR/post-deploy-check.sh"

cp "$TEMP_RELEASE_ENV_FILE" "$DEPLOY_DIR/.env"

if [ -f "$CURRENT_RELEASE_FILE" ]; then
  cp "$CURRENT_RELEASE_FILE" "$PREVIOUS_RELEASE_FILE"
fi

cp "$STAGED_RELEASE_FILE" "$CURRENT_RELEASE_FILE"
history_file=$(record_release_history "$CURRENT_RELEASE_FILE" "$DEPLOY_RELEASE_ID")
write_release_summary \
  "$CURRENT_RELEASE_SUMMARY_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$SELECTED_SERVICES" \
  "$DEPLOY_RELEASE_NOTES_VALUE"

cleanup_log=$(mktemp "$DEPLOY_DIR/post-release-cleanup.XXXXXX")
cleanup_status=0
if COMPOSE_ENV_FILE="$DEPLOY_DIR/.env" "$SCRIPT_DIR/post-release-cleanup.sh" --release-tag "$DEPLOY_RELEASE_ID" > "$cleanup_log" 2>&1; then
  cleanup_status=0
else
  cleanup_status=$?
fi
cat "$cleanup_log"
{
  printf '\n'
  cat "$cleanup_log"
} >> "$CURRENT_RELEASE_SUMMARY_FILE"
rm -f "$cleanup_log"

history_summary_file=${history_file%.env}.summary.md
cp "$CURRENT_RELEASE_SUMMARY_FILE" "$history_summary_file"
cleanup_temp_release_env
unset COMPOSE_ENV_FILE
trap - EXIT HUP INT TERM

if [ "$cleanup_status" -ne 0 ]; then
  printf 'Release deployed, but post-release cleanup failed: %s\n' "$DEPLOY_RELEASE_ID" >&2
  printf 'Current release summary: %s\n' "$CURRENT_RELEASE_SUMMARY_FILE" >&2
  exit "$cleanup_status"
fi

printf 'Release deployed successfully: %s\n' "$DEPLOY_RELEASE_ID"
printf 'Current release manifest: %s\n' "$CURRENT_RELEASE_FILE"
printf 'Current release summary: %s\n' "$CURRENT_RELEASE_SUMMARY_FILE"
printf 'Release history record: %s\n' "$history_file"
printf 'Release history summary: %s\n' "$history_summary_file"
