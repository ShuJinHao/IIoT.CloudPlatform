APP_IMAGE_KEYS="
IIOT_HTTPAPI_IMAGE
IIOT_GATEWAY_IMAGE
IIOT_DATAWORKER_IMAGE
IIOT_MIGRATION_IMAGE
IIOT_WEB_IMAGE
"

RUNTIME_SERVICES="
iiot-httpapi
iiot-gateway
iiot-dataworker
iiot-web
nginx-gateway
"

RELEASES_DIR="$DEPLOY_DIR/releases"
RELEASE_HISTORY_DIR="$RELEASES_DIR/history"
CURRENT_RELEASE_FILE="$RELEASES_DIR/current-release.env"
PREVIOUS_RELEASE_FILE="$RELEASES_DIR/previous-release.env"
STAGED_RELEASE_FILE="$RELEASES_DIR/staged-release.env"
BACKUP_STATE_FILE="$DEPLOY_DIR/backups/postgres/latest-successful-backup.txt"

compose_env_file_path() {
  env_file=${COMPOSE_ENV_FILE:-$DEPLOY_DIR/.env}
  case "$env_file" in
    /*)
      printf '%s\n' "$env_file"
      ;;
    *)
      printf '%s\n' "$DEPLOY_DIR/$env_file"
      ;;
  esac
}

require_deploy_env_file() {
  env_file=${1:-$(compose_env_file_path)}
  if [ ! -f "$env_file" ]; then
    printf 'Missing deploy environment file: %s\n' "$env_file" >&2
    exit 66
  fi
}

load_dotenv() {
  env_file=${1:-$(compose_env_file_path)}
  require_deploy_env_file "$env_file"

  while IFS= read -r env_line || [ -n "$env_line" ]
  do
    env_line=$(printf '%s' "$env_line" | tr -d '\r')

    case "$env_line" in
      ''|'#'*)
        continue
        ;;
      *=*)
        export "$env_line"
        ;;
      *)
        printf 'Invalid env line in %s: %s\n' "$env_file" "$env_line" >&2
        exit 64
        ;;
    esac
  done < "$env_file"
}

compose() {
  docker compose --env-file "$(compose_env_file_path)" -f "$DEPLOY_DIR/docker-compose.prod.yml" "$@"
}

prepare_release_directories() {
  mkdir -p "$RELEASE_HISTORY_DIR"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'Required command not found: %s\n' "$1" >&2
    exit 127
  }
}

require_docker_compose() {
  require_command docker
  docker compose version >/dev/null 2>&1 || {
    printf 'docker compose is not available.\n' >&2
    exit 127
  }
}

ensure_release_tag() {
  release_tag=${1:-}
  case "$release_tag" in
    sha-*)
      release_suffix=${release_tag#sha-}
      case "$release_suffix" in
        ''|*[!0-9a-f]*)
          printf 'Release tag must match sha-<hex>: %s\n' "$release_tag" >&2
          exit 64
          ;;
      esac
      ;;
    *)
      printf 'Release tag must match sha-<hex>: %s\n' "$release_tag" >&2
      exit 64
      ;;
  esac
}

require_env_value() {
  key=$1
  eval "value=\${$key:-}"
  if [ -z "$value" ]; then
    printf 'Missing required value in .env: %s\n' "$key" >&2
    exit 64
  fi
}

require_app_image_values() {
  for key in $APP_IMAGE_KEYS
  do
    require_env_value "$key"
  done
}

image_repository_from_ref() {
  image_ref=$1
  last_segment=${image_ref##*/}

  if [ "${image_ref#*@}" != "$image_ref" ]; then
    printf '%s\n' "${image_ref%@*}"
    return
  fi

  if [ "${last_segment#*:}" != "$last_segment" ]; then
    printf '%s\n' "${image_ref%:*}"
    return
  fi

  printf '%s\n' "$image_ref"
}

resolve_target_image() {
  key=$1
  release_tag=$2
  eval "image_ref=\${$key:-}"

  if [ -z "$image_ref" ]; then
    printf 'Missing image variable in .env: %s\n' "$key" >&2
    exit 64
  fi

  image_repository=$(image_repository_from_ref "$image_ref")
  printf '%s:%s\n' "$image_repository" "$release_tag"
}

resolve_release_images() {
  release_tag=$1

  for key in $APP_IMAGE_KEYS
  do
    target_image=$(resolve_target_image "$key" "$release_tag")
    eval "$key=\$target_image"
  done
}

ensure_target_images_not_latest() {
  for key in $APP_IMAGE_KEYS
  do
    eval "image_ref=\${$key:-}"
    case "$image_ref" in
      *:latest)
        printf 'Application image may not use :latest during release: %s=%s\n' "$key" "$image_ref" >&2
        exit 64
        ;;
    esac
  done
}

read_state_path() {
  state_file=$1

  if [ ! -f "$state_file" ]; then
    return 1
  fi

  state_path=$(sed -n '1p' "$state_file")
  if [ -z "$state_path" ]; then
    return 1
  fi

  printf '%s\n' "$state_path"
}

probe_status() {
  url=$1
  expected_codes=$2
  max_attempts=${3:-12}
  attempt=1

  while [ "$attempt" -le "$max_attempts" ]
  do
    status_code=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 "$url" || true)

    for allowed_code in $expected_codes
    do
      if [ "$status_code" = "$allowed_code" ]; then
        printf 'Probe succeeded: %s -> %s\n' "$url" "$status_code"
        return 0
      fi
    done

    printf 'Probe attempt %s/%s failed: %s -> %s\n' "$attempt" "$max_attempts" "$url" "${status_code:-curl-error}" >&2
    sleep 5
    attempt=$((attempt + 1))
  done

  printf 'Probe failed after %s attempts: %s expected [%s]\n' "$max_attempts" "$url" "$expected_codes" >&2
  return 1
}

require_running_service() {
  service_name=$1
  if ! compose ps --status running --services | grep -qx "$service_name"; then
    printf 'Required service is not running: %s\n' "$service_name" >&2
    compose ps >&2
    exit 1
  fi
}

replace_env_value() {
  file_path=$1
  key=$2
  value=$3
  tmp_file=$(mktemp "$DEPLOY_DIR/.env.XXXXXX")

  awk -v key="$key" -v value="$value" '
    BEGIN { updated = 0 }
    index($0, key "=") == 1 {
      print key "=" value
      updated = 1
      next
    }
    { print }
    END {
      if (!updated) {
        print key "=" value
      }
    }' "$file_path" > "$tmp_file"

  mv "$tmp_file" "$file_path"
}

apply_app_images_to_dotenv() {
  env_file=${1:-"$DEPLOY_DIR/.env"}

  for key in $APP_IMAGE_KEYS
  do
    eval "value=\${$key:-}"
    replace_env_value "$env_file" "$key" "$value"
  done
}

write_release_manifest() {
  output_path=$1
  release_id=$2
  deploy_git_sha=$3
  deploy_triggered_by=$4
  deployed_at_utc=$5
  pre_deploy_backup_file=$6

  umask 077
  {
    printf 'DEPLOY_RELEASE_ID=%s\n' "$release_id"
    printf 'DEPLOY_GIT_SHA=%s\n' "$deploy_git_sha"
    printf 'DEPLOY_TRIGGERED_BY=%s\n' "$deploy_triggered_by"
    printf 'DEPLOYED_AT_UTC=%s\n' "$deployed_at_utc"
    printf 'PRE_DEPLOY_BACKUP_FILE=%s\n' "$pre_deploy_backup_file"

    for key in $APP_IMAGE_KEYS
    do
      eval "value=\${$key:-}"
      printf '%s=%s\n' "$key" "$value"
    done
  } > "$output_path"
}

safe_release_file_name() {
  printf '%s' "$1" | tr -c 'A-Za-z0-9._-' '-'
}

record_release_history() {
  source_file=$1
  release_id=$2
  history_timestamp=$(date -u +"%Y%m%dT%H%M%SZ")
  safe_release_id=$(safe_release_file_name "$release_id")
  history_file="$RELEASE_HISTORY_DIR/$history_timestamp-$safe_release_id.env"
  cp "$source_file" "$history_file"
  printf '%s\n' "$history_file"
}

read_manifest_value() {
  manifest_path=$1
  key=$2
  sed -n "s/^${key}=//p" "$manifest_path" | tail -n 1
}

load_release_images_from_manifest() {
  manifest_path=$1

  for key in $APP_IMAGE_KEYS
  do
    value=$(read_manifest_value "$manifest_path" "$key")
    if [ -z "$value" ]; then
      printf 'Release manifest is missing %s: %s\n' "$key" "$manifest_path" >&2
      exit 66
    fi

    eval "$key=\$value"
  done
}

resolve_release_file_path() {
  requested_path=${1:-}

  if [ -z "$requested_path" ]; then
    printf '%s\n' "$PREVIOUS_RELEASE_FILE"
    return
  fi

  case "$requested_path" in
    /*)
      printf '%s\n' "$requested_path"
      ;;
    *)
      if [ -f "$DEPLOY_DIR/$requested_path" ]; then
        printf '%s\n' "$DEPLOY_DIR/$requested_path"
        return
      fi

      if [ -f "$RELEASES_DIR/$requested_path" ]; then
        printf '%s\n' "$RELEASES_DIR/$requested_path"
        return
      fi

      printf '%s\n' "$DEPLOY_DIR/$requested_path"
      ;;
  esac
}
