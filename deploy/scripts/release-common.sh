APP_IMAGE_KEYS="
IIOT_HTTPAPI_IMAGE
IIOT_GATEWAY_IMAGE
IIOT_DATAWORKER_IMAGE
IIOT_MIGRATION_IMAGE
IIOT_WEB_IMAGE
"

INFRA_IMAGE_KEYS="
IIOT_NGINX_IMAGE
IIOT_POSTGRES_IMAGE
IIOT_REDIS_IMAGE
IIOT_RABBITMQ_IMAGE
IIOT_SEQ_IMAGE
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
CURRENT_RELEASE_SUMMARY_FILE="$RELEASES_DIR/current-release.summary.md"
BACKUP_STATE_FILE="$DEPLOY_DIR/backups/postgres/latest-successful-backup.txt"
CLOUD_RELEASE_LOCK_FILE_DEFAULT="/data/iiot-platform/.locks/cloud-release.lock"
POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT="/data/iiot-platform/.locks/deploy-cleanup.lock"

resolve_managed_lock_file() {
  preferred_lock_file=$1
  fallback_lock_file=$2
  preferred_lock_parent=$(dirname "$preferred_lock_file")

  if { [ -d "$preferred_lock_parent" ] || mkdir -p "$preferred_lock_parent" 2>/dev/null; } \
    && [ -w "$preferred_lock_parent" ] \
    && [ -x "$preferred_lock_parent" ]; then
    printf '%s\n' "$preferred_lock_file"
    return
  fi

  fallback_lock_parent=$(dirname "$fallback_lock_file")
  if ! { [ -d "$fallback_lock_parent" ] || mkdir -p "$fallback_lock_parent" 2>/dev/null; } \
    || [ ! -w "$fallback_lock_parent" ] \
    || [ ! -x "$fallback_lock_parent" ]; then
    printf 'Neither managed lock parent is writable: preferred=%s fallback=%s\n' \
      "$preferred_lock_parent" \
      "$fallback_lock_parent" >&2
    return 73
  fi

  printf 'Managed lock parent is not writable; using deploy-local fallback: %s\n' "$fallback_lock_file" >&2
  printf '%s\n' "$fallback_lock_file"
}

managed_lock_read_field() {
  managed_lock_field_dir=$1
  managed_lock_field_name=$2
  if [ -r "$managed_lock_field_dir/$managed_lock_field_name" ]; then
    sed -n '1p' "$managed_lock_field_dir/$managed_lock_field_name"
  fi
}

managed_lock_process_start() {
  managed_lock_process_pid=${1:-}
  case "$managed_lock_process_pid" in
    ''|*[!0-9]*)
      return 1
      ;;
  esac

  if [ -r "/proc/$managed_lock_process_pid/stat" ]; then
    awk '{print $22}' "/proc/$managed_lock_process_pid/stat"
    return
  fi

  return 1
}

managed_lock_process_is_alive() {
  managed_lock_process_pid=${1:-}
  case "$managed_lock_process_pid" in
    ''|*[!0-9]*)
      return 1
      ;;
  esac

  kill -0 "$managed_lock_process_pid" 2>/dev/null || [ -d "/proc/$managed_lock_process_pid" ]
}

managed_lock_mtime_epoch() {
  managed_lock_mtime_path=$1
  if stat -c '%Y' "$managed_lock_mtime_path" >/dev/null 2>&1; then
    stat -c '%Y' "$managed_lock_mtime_path"
    return
  fi

  stat -f '%m' "$managed_lock_mtime_path" 2>/dev/null
}

managed_lock_inode() {
  managed_lock_inode_path=$1
  if stat -c '%i' "$managed_lock_inode_path" >/dev/null 2>&1; then
    stat -c '%i' "$managed_lock_inode_path"
    return
  fi

  stat -f '%i' "$managed_lock_inode_path" 2>/dev/null
}

managed_lock_status_for_dir() {
  managed_lock_status_dir=$1
  if [ ! -d "$managed_lock_status_dir" ]; then
    printf '%s\n' absent
    return
  fi

  managed_lock_status_pid=$(managed_lock_read_field "$managed_lock_status_dir" pid)
  case "$managed_lock_status_pid" in
    ''|*[!0-9]*)
      managed_lock_status_now=$(date +%s)
      managed_lock_status_mtime=$(managed_lock_mtime_epoch "$managed_lock_status_dir" || true)
      managed_lock_status_grace=${MANAGED_LOCK_INITIALIZATION_GRACE_SECONDS:-30}
      case "$managed_lock_status_grace" in
        ''|*[!0-9]*)
          managed_lock_status_grace=30
          ;;
      esac

      if [ -n "$managed_lock_status_mtime" ] \
        && [ $((managed_lock_status_now - managed_lock_status_mtime)) -ge "$managed_lock_status_grace" ]; then
        printf '%s\n' stale
      else
        printf '%s\n' initializing
      fi
      return
      ;;
  esac

  if ! managed_lock_process_is_alive "$managed_lock_status_pid"; then
    printf '%s\n' stale
    return
  fi

  managed_lock_status_recorded_start=$(managed_lock_read_field "$managed_lock_status_dir" process-start)
  managed_lock_status_actual_start=$(managed_lock_process_start "$managed_lock_status_pid" || true)
  if [ -n "$managed_lock_status_recorded_start" ] \
    && [ -n "$managed_lock_status_actual_start" ] \
    && [ "$managed_lock_status_recorded_start" != "$managed_lock_status_actual_start" ]; then
    printf '%s\n' stale
    return
  fi

  printf '%s\n' live
}

describe_managed_lock() {
  managed_lock_describe_file=$1
  managed_lock_describe_dir="${managed_lock_describe_file}.d"
  managed_lock_describe_pid=$(managed_lock_read_field "$managed_lock_describe_dir" pid)
  managed_lock_describe_release=$(managed_lock_read_field "$managed_lock_describe_dir" release)
  managed_lock_describe_phase=$(managed_lock_read_field "$managed_lock_describe_dir" phase)
  managed_lock_describe_created=$(managed_lock_read_field "$managed_lock_describe_dir" created-at)
  managed_lock_describe_script=$(managed_lock_read_field "$managed_lock_describe_dir" script)
  printf 'path=%s pid=%s release=%s phase=%s created_at=%s script=%s\n' \
    "$managed_lock_describe_dir" \
    "${managed_lock_describe_pid:-unknown}" \
    "${managed_lock_describe_release:-unknown}" \
    "${managed_lock_describe_phase:-unknown}" \
    "${managed_lock_describe_created:-unknown}" \
    "${managed_lock_describe_script:-unknown}"
}

remove_stale_managed_lock() {
  managed_lock_remove_file=$1
  managed_lock_remove_dir="${managed_lock_remove_file}.d"
  managed_lock_remove_status=$(managed_lock_status_for_dir "$managed_lock_remove_dir")
  if [ "$managed_lock_remove_status" != "stale" ]; then
    printf 'Refusing to remove a managed lock that is not stale: status=%s %s\n' \
      "$managed_lock_remove_status" \
      "$(describe_managed_lock "$managed_lock_remove_file")" >&2
    return 75
  fi

  managed_lock_remove_inode=$(managed_lock_inode "$managed_lock_remove_dir" || true)
  managed_lock_remove_claim="${managed_lock_remove_dir}.stale-claim-$$-$(date +%s)"
  if ! mv "$managed_lock_remove_dir" "$managed_lock_remove_claim" 2>/dev/null; then
    printf 'Managed lock changed while claiming stale state; retry after inspecting: %s\n' "$managed_lock_remove_dir" >&2
    return 75
  fi

  managed_lock_claim_inode=$(managed_lock_inode "$managed_lock_remove_claim" || true)
  managed_lock_claim_status=$(managed_lock_status_for_dir "$managed_lock_remove_claim")
  if { [ -n "$managed_lock_remove_inode" ] \
      && [ -n "$managed_lock_claim_inode" ] \
      && [ "$managed_lock_remove_inode" != "$managed_lock_claim_inode" ]; } \
    || [ "$managed_lock_claim_status" != "stale" ]; then
    if [ ! -e "$managed_lock_remove_dir" ]; then
      mv "$managed_lock_remove_claim" "$managed_lock_remove_dir" 2>/dev/null || true
    fi
    printf 'Managed lock changed during stale verification; it was not removed: %s\n' "$managed_lock_remove_dir" >&2
    return 75
  fi

  rm -rf "$managed_lock_remove_claim"
  printf 'Removed stale managed lock: %s\n' "$managed_lock_remove_dir"
}

ensure_managed_lock_available() {
  managed_lock_available_file=$1
  managed_lock_allowed_owner_pid=${2:-}
  managed_lock_available_dir="${managed_lock_available_file}.d"
  managed_lock_available_status=$(managed_lock_status_for_dir "$managed_lock_available_dir")

  case "$managed_lock_available_status" in
    absent)
      return
      ;;
    live)
      managed_lock_available_owner_pid=$(managed_lock_read_field "$managed_lock_available_dir" pid)
      if [ -n "$managed_lock_allowed_owner_pid" ] \
        && [ "$managed_lock_available_owner_pid" = "$managed_lock_allowed_owner_pid" ]; then
        return
      fi
      printf 'Managed lock is active; fail-fast without waiting: %s\n' \
        "$(describe_managed_lock "$managed_lock_available_file")" >&2
      return 75
      ;;
    initializing)
      printf 'Managed lock is being initialized; fail-fast without waiting: %s\n' \
        "$(describe_managed_lock "$managed_lock_available_file")" >&2
      return 75
      ;;
    stale)
      remove_stale_managed_lock "$managed_lock_available_file"
      if [ -d "$managed_lock_available_dir" ]; then
        printf 'A managed lock was acquired while stale state was removed: %s\n' \
          "$(describe_managed_lock "$managed_lock_available_file")" >&2
        return 75
      fi
      return
      ;;
    *)
      printf 'Unknown managed lock state: %s path=%s\n' \
        "$managed_lock_available_status" \
        "$managed_lock_available_dir" >&2
      return 75
      ;;
  esac
}

acquire_managed_lock() {
  managed_lock_acquire_file=$1
  managed_lock_acquire_purpose=$2
  managed_lock_acquire_release=$3
  managed_lock_acquire_phase=$4
  managed_lock_acquire_script=$5
  managed_lock_acquire_dir="${managed_lock_acquire_file}.d"

  ensure_managed_lock_available "$managed_lock_acquire_file" || return $?
  if ! mkdir "$managed_lock_acquire_dir" 2>/dev/null; then
    printf 'Managed lock was acquired concurrently; fail-fast without waiting: %s\n' \
      "$(describe_managed_lock "$managed_lock_acquire_file")" >&2
    return 75
  fi

  umask 077
  managed_lock_acquire_process_start=$(managed_lock_process_start "$$" || true)
  if ! {
    printf '%s\n' "$$" > "$managed_lock_acquire_dir/pid"
    if [ -n "$managed_lock_acquire_process_start" ]; then
      printf '%s\n' "$managed_lock_acquire_process_start" > "$managed_lock_acquire_dir/process-start"
    fi
    printf '%s\n' "$managed_lock_acquire_purpose" > "$managed_lock_acquire_dir/purpose"
    printf '%s\n' "$managed_lock_acquire_release" > "$managed_lock_acquire_dir/release"
    printf '%s\n' "$managed_lock_acquire_phase" > "$managed_lock_acquire_dir/phase"
    printf '%s\n' "$managed_lock_acquire_script" > "$managed_lock_acquire_dir/script"
    date -u +'%Y-%m-%dT%H:%M:%SZ' > "$managed_lock_acquire_dir/created-at"
  }; then
    rm -rf "$managed_lock_acquire_dir"
    printf 'Could not write managed lock metadata: %s\n' "$managed_lock_acquire_dir" >&2
    return 73
  fi

  printf 'Managed lock acquired: %s\n' "$(describe_managed_lock "$managed_lock_acquire_file")"
}

update_managed_lock_phase() {
  managed_lock_update_file=$1
  managed_lock_update_phase=$2
  managed_lock_update_dir="${managed_lock_update_file}.d"
  managed_lock_update_owner=$(managed_lock_read_field "$managed_lock_update_dir" pid)
  if [ "$managed_lock_update_owner" != "$$" ]; then
    printf 'Refusing to update a managed lock owned by another process: %s\n' \
      "$(describe_managed_lock "$managed_lock_update_file")" >&2
    return 75
  fi

  printf '%s\n' "$managed_lock_update_phase" > "$managed_lock_update_dir/phase"
  printf 'Managed lock phase: release=%s phase=%s\n' \
    "$(managed_lock_read_field "$managed_lock_update_dir" release)" \
    "$managed_lock_update_phase"
}

release_managed_lock() {
  managed_lock_release_file=$1
  managed_lock_release_dir="${managed_lock_release_file}.d"
  if [ ! -d "$managed_lock_release_dir" ]; then
    return
  fi

  managed_lock_release_owner=$(managed_lock_read_field "$managed_lock_release_dir" pid)
  if [ "$managed_lock_release_owner" != "$$" ]; then
    printf 'Managed lock belongs to another process and will not be released: %s\n' \
      "$(describe_managed_lock "$managed_lock_release_file")" >&2
    return 75
  fi

  managed_lock_release_recorded_start=$(managed_lock_read_field "$managed_lock_release_dir" process-start)
  managed_lock_release_actual_start=$(managed_lock_process_start "$$" || true)
  if [ -n "$managed_lock_release_recorded_start" ] \
    && [ -n "$managed_lock_release_actual_start" ] \
    && [ "$managed_lock_release_recorded_start" != "$managed_lock_release_actual_start" ]; then
    printf 'Managed lock process identity changed and will not be released: %s\n' \
      "$(describe_managed_lock "$managed_lock_release_file")" >&2
    return 75
  fi

  rm -rf "$managed_lock_release_dir"
  printf 'Managed lock released: %s\n' "$managed_lock_release_dir"
}

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

require_infra_image_values() {
  for key in $INFRA_IMAGE_KEYS
  do
    require_env_value "$key"
  done
}

ensure_image_values_not_template() {
  for key in $APP_IMAGE_KEYS $INFRA_IMAGE_KEYS
  do
    eval "image_ref=\${$key:-}"
    require_env_value "$key"

    case "$image_ref" in
      *.example*|*internal.example*)
        printf 'Image value still uses a documentation example registry: %s=%s\n' "$key" "$image_ref" >&2
        exit 64
        ;;
    esac
  done
}

ensure_app_images_have_explicit_registry() {
  for key in $APP_IMAGE_KEYS
  do
    eval "image_ref=\${$key:-}"
    require_env_value "$key"
    image_registry=${image_ref%%/*}

    case "$image_ref" in
      docker.io/*|registry-1.docker.io/*)
        printf 'Application image must be pushed to Harbor, not pulled from Docker Hub: %s=%s\n' "$key" "$image_ref" >&2
        exit 64
        ;;
    esac

    if [ "$image_registry" = "$image_ref" ]; then
      printf 'Application image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
      exit 64
    fi

    case "$image_registry" in
      *.*|*:*|localhost)
        ;;
      *)
        printf 'Application image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
        exit 64
        ;;
    esac
  done
}

require_changed_secret_value() {
  key=$1
  eval "value=\${$key:-}"

  require_env_value "$key"

  shift
  for forbidden_value in "$@"
  do
    if [ "$value" = "$forbidden_value" ]; then
      printf 'Required secret still uses a template or known weak value: %s\n' "$key" >&2
      exit 64
    fi
  done
}

require_min_secret_length() {
  key=$1
  min_length=$2
  eval "value=\${$key:-}"

  require_env_value "$key"

  value_length=${#value}
  if [ "$value_length" -lt "$min_length" ]; then
    printf 'Required secret is too short: %s must be at least %s characters\n' "$key" "$min_length" >&2
    exit 64
  fi
}

require_positive_integer_at_most() {
  key=$1
  max_value=$2
  eval "value=\${$key:-}"

  require_env_value "$key"

  case "$value" in
    ''|*[!0-9]*)
      printf 'Deployment numeric value must be a positive integer: %s=%s\n' "$key" "$value" >&2
      exit 64
      ;;
  esac

  if [ "$value" -le 0 ]; then
    printf 'Deployment numeric value must be greater than 0: %s=%s\n' "$key" "$value" >&2
    exit 64
  fi

  if [ "$value" -gt "$max_value" ]; then
    printf 'Deployment numeric value exceeds the allowed maximum: %s=%s max=%s\n' \
      "$key" \
      "$value" \
      "$max_value" >&2
    exit 64
  fi
}

require_template_value_replaced() {
  key=$1
  eval "value=\${$key:-}"

  require_env_value "$key"

  case "$value" in
    __REPLACE_*__|change-me-*|*.example*|*internal.example*)
      printf 'Required deployment value still uses a template marker: %s\n' "$key" >&2
      exit 64
      ;;
  esac
}

lower_value() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

ensure_deploy_operator_not_root() {
  if [ "$(id -u)" = "0" ] && [ "${ALLOW_ROOT_DEPLOY_PREFLIGHT:-}" != "emergency" ]; then
    printf 'Pre-deploy checks refuse root execution by default. Use a dedicated deploy user, or set ALLOW_ROOT_DEPLOY_PREFLIGHT=emergency for an approved break-glass path.\n' >&2
    exit 64
  fi
}

ensure_bootstrap_secret_not_disabled() {
  for key in BOOTSTRAP_AUTH_REQUIRE_SECRET BootstrapAuth__RequireSecret
  do
    eval "value=\${$key:-}"
    if [ -z "$value" ]; then
      continue
    fi

    case "$(lower_value "$value")" in
      false|0|no)
        printf 'Bootstrap secret cannot be disabled in deployment env: %s=%s\n' "$key" "$value" >&2
        exit 64
        ;;
    esac
  done
}

url_host() {
  value=$1
  value=${value#*://}
  value=${value%%/*}
  value=${value%%\?*}
  value=${value##*@}

  case "$value" in
    \[*\]*)
      host=${value#\[}
      host=${host%%\]*}
      ;;
    *)
      host=${value%%:*}
      ;;
  esac

  printf '%s\n' "$host"
}

is_loopback_or_rfc1918_ipv4_host() {
  host=$1

  case "$host" in
    localhost|127.*|::1)
      return 0
      ;;
  esac

  case "$host" in
    ''|*[!0-9.]*)
      return 1
      ;;
  esac

  old_ifs=$IFS
  IFS=.
  set -- $host
  IFS=$old_ifs

  if [ "$#" -ne 4 ]; then
    return 1
  fi

  for octet in "$@"
  do
    case "$octet" in
      ''|*[!0-9]*)
        return 1
        ;;
    esac

    if [ "$octet" -gt 255 ]; then
      return 1
    fi
  done

  first=$1
  second=$2
  if [ "$first" -eq 10 ]; then
    return 0
  fi
  if [ "$first" -eq 192 ] && [ "$second" -eq 168 ]; then
    return 0
  fi
  if [ "$first" -eq 172 ] && [ "$second" -ge 16 ] && [ "$second" -le 31 ]; then
    return 0
  fi

  return 1
}

ensure_oidc_http_uri_boundary() {
  key=$1
  eval "value=\${$key:-}"
  require_env_value "$key"

  case "$value" in
    https://*)
      return
      ;;
    http://*)
      host=$(url_host "$value")
      if is_loopback_or_rfc1918_ipv4_host "$host"; then
        return
      fi

      printf 'HTTP OIDC value must use loopback or RFC1918 IPv4 host when ALLOW_INTRANET_HTTP_OIDC=true: %s=%s\n' "$key" "$value" >&2
      exit 64
      ;;
    *)
      printf 'OIDC value must be an absolute http/https URI: %s=%s\n' "$key" "$value" >&2
      exit 64
      ;;
  esac
}

ensure_oidc_http_boundary() {
  allow_oidc_http=$(lower_value "${ALLOW_INTRANET_HTTP_OIDC:-false}")

  case "$allow_oidc_http" in
    true)
      ensure_oidc_http_uri_boundary OIDC_PROVIDER_ISSUER
      ensure_oidc_http_uri_boundary AICOPILOT_OIDC_REDIRECT_URI
      ensure_oidc_http_uri_boundary AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI
      ;;
    false|'')
      for key in OIDC_PROVIDER_ISSUER AICOPILOT_OIDC_REDIRECT_URI AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI
      do
        eval "value=\${$key:-}"
        require_env_value "$key"
        case "$value" in
          http://*)
            printf 'HTTP OIDC value requires ALLOW_INTRANET_HTTP_OIDC=true and loopback/RFC1918 IPv4 host: %s=%s\n' "$key" "$value" >&2
            exit 64
            ;;
        esac
      done
      ;;
    *)
      printf 'ALLOW_INTRANET_HTTP_OIDC must be true or false: %s\n' "$ALLOW_INTRANET_HTTP_OIDC" >&2
      exit 64
      ;;
  esac
}

ensure_deploy_disk_headroom() {
  disk_path=${PRE_DEPLOY_DISK_PATH:-$DEPLOY_DIR}
  warn_percent=${PRE_DEPLOY_DISK_WARN_PERCENT:-80}
  block_percent=${PRE_DEPLOY_DISK_BLOCK_PERCENT:-85}
  disk_usage_percent=$(df -P "$disk_path" | awk 'NR == 2 { gsub("%", "", $5); print $5 }')

  if [ -z "$disk_usage_percent" ]; then
    printf 'Could not determine disk usage for pre-deploy path: %s\n' "$disk_path" >&2
    exit 65
  fi

  printf 'preflight_disk_usage_percent=%s warn_threshold=%s block_threshold=%s path=%s\n' \
    "$disk_usage_percent" \
    "$warn_percent" \
    "$block_percent" \
    "$disk_path"

  if [ "$disk_usage_percent" -ge "$block_percent" ]; then
    printf 'Disk usage is at or above the routine release block threshold: %s%% >= %s%%\n' "$disk_usage_percent" "$block_percent" >&2
    exit 65
  fi

  if [ "$disk_usage_percent" -ge "$warn_percent" ]; then
    printf 'warning: disk usage is at or above the operator warning threshold: %s%% >= %s%%\n' "$disk_usage_percent" "$warn_percent" >&2
  fi
}

print_http_only_preflight_summary() {
  runtime_controls=${1:-runtime-check-not-declared}
  printf 'preflight_transport_baseline=http-only controlled-intranet no-tls no-hsts no-https-redirection\n'
  printf 'preflight_compensation_controls=secrets-checked templates-checked bootstrap-secret-required oidc-http-boundary-checked image-registry-checked docker-hub-blocked compose-checked disk-checked %s\n' "$runtime_controls"
}

ensure_required_secret_values_changed() {
  require_changed_secret_value \
    PG_PASSWORD \
    __REPLACE_POSTGRES_PASSWORD__ \
    change-me-postgres-password \
    postgres \
    123456
  require_min_secret_length PG_PASSWORD 12

  require_changed_secret_value \
    RABBITMQ_DEFAULT_PASS \
    __REPLACE_RABBITMQ_PASSWORD__ \
    change-me-rabbitmq-password \
    guest \
    123456
  require_min_secret_length RABBITMQ_DEFAULT_PASS 12

  require_changed_secret_value \
    JWTSETTINGS__SECRET \
    __REPLACE_JWT_SECRET__ \
    change-me-jwt-secret \
    iiot-cloud-jwt-secret-2026-04-22
  require_min_secret_length JWTSETTINGS__SECRET 32

  require_changed_secret_value \
    SEQ_ADMIN_PASSWORD \
    __REPLACE_SEQ_PASSWORD__ \
    change-me-seq-password \
    123456
  require_min_secret_length SEQ_ADMIN_PASSWORD 12

  require_changed_secret_value \
    SEED_ADMIN_PASSWORD \
    __REPLACE_ADMIN_PASSWORD__ \
    change-me-admin-password \
    Ljh123456!
  require_min_secret_length SEED_ADMIN_PASSWORD 12
}

ensure_required_public_values_changed() {
  require_template_value_replaced PUBLIC_BASE_URL
  require_template_value_replaced CORS_ALLOWED_ORIGIN_0
  require_template_value_replaced OIDC_PROVIDER_ISSUER
  require_template_value_replaced AICOPILOT_OIDC_REDIRECT_URI
  require_template_value_replaced AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI
  require_template_value_replaced VITE_AICOPILOT_CHALLENGE_URL
}

ensure_rate_limit_values_bounded() {
  max_edge_upload_rate_per_minute=12000

  require_positive_integer_at_most RATE_LIMIT_CAPACITY_UPLOAD_TOKEN_LIMIT "$max_edge_upload_rate_per_minute"
  require_positive_integer_at_most RATE_LIMIT_CAPACITY_UPLOAD_TOKENS_PER_PERIOD "$max_edge_upload_rate_per_minute"
  require_positive_integer_at_most RATE_LIMIT_DEVICE_LOG_UPLOAD_TOKEN_LIMIT "$max_edge_upload_rate_per_minute"
  require_positive_integer_at_most RATE_LIMIT_DEVICE_LOG_UPLOAD_TOKENS_PER_PERIOD "$max_edge_upload_rate_per_minute"
  require_positive_integer_at_most RATE_LIMIT_PASS_STATION_UPLOAD_TOKEN_LIMIT "$max_edge_upload_rate_per_minute"
  require_positive_integer_at_most RATE_LIMIT_PASS_STATION_UPLOAD_TOKENS_PER_PERIOD "$max_edge_upload_rate_per_minute"
}

ensure_infra_images_not_docker_hub() {
  for key in $INFRA_IMAGE_KEYS
  do
    eval "image_ref=\${$key:-}"
    image_registry=${image_ref%%/*}

    case "$image_ref" in
      docker.io/*|registry-1.docker.io/*)
        printf 'Infrastructure image must be mirrored to Harbor, not pulled from Docker Hub: %s=%s\n' "$key" "$image_ref" >&2
        exit 64
        ;;
    esac

    if [ "$image_registry" = "$image_ref" ]; then
      printf 'Infrastructure image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
      exit 64
    fi

    case "$image_registry" in
      *.*|*:*|localhost)
        ;;
      *)
        printf 'Infrastructure image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
        exit 64
        ;;
    esac
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

require_healthy_service() {
  service_name=$1
  max_attempts=${2:-12}
  attempt=1

  container_id=$(compose ps -q "$service_name" 2>/dev/null | head -n 1 || true)
  if [ -z "$container_id" ]; then
    printf 'Required service container was not found: %s\n' "$service_name" >&2
    compose ps >&2
    exit 1
  fi

  while [ "$attempt" -le "$max_attempts" ]
  do
    health_status=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container_id" 2>/dev/null || true)

    case "$health_status" in
      healthy)
        printf 'Service health check passed: %s -> healthy\n' "$service_name"
        return 0
        ;;
      none)
        printf 'Service does not define a Docker health check: %s\n' "$service_name" >&2
        exit 1
        ;;
      starting|unhealthy)
        printf 'Service health attempt %s/%s: %s -> %s\n' "$attempt" "$max_attempts" "$service_name" "$health_status" >&2
        sleep 5
        ;;
      *)
        printf 'Service health check status is unavailable: %s -> %s\n' "$service_name" "${health_status:-unknown}" >&2
        sleep 5
        ;;
    esac

    attempt=$((attempt + 1))
  done

  printf 'Service did not become healthy after %s attempts: %s\n' "$max_attempts" "$service_name" >&2
  docker inspect --format '{{json .State.Health}}' "$container_id" >&2 || true
  exit 1
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

resolve_release_images_for_keys() {
  release_tag=$1
  shift

  for key in "$@"
  do
    target_image=$(resolve_target_image "$key" "$release_tag")
    eval "$key=\$target_image"
  done
}

apply_app_images_to_dotenv_for_keys() {
  env_file=$1
  shift

  for key in "$@"
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

write_release_summary() {
  output_path=$1
  release_id=$2
  deploy_git_sha=$3
  deploy_triggered_by=$4
  deployed_at_utc=$5
  deployed_services=$6
  release_notes=${7:-}

  umask 077
  {
    printf '### Cloud deploy\n\n'
    printf -- '- Release tag: `%s`\n' "$release_id"
    printf -- '- Git SHA: `%s`\n' "$deploy_git_sha"
    printf -- '- Triggered by: `%s`\n' "$deploy_triggered_by"
    printf -- '- Deployed at UTC: `%s`\n' "$deployed_at_utc"
    printf -- '- Services: `%s`\n' "${deployed_services:-all}"
    printf '\n#### Changes\n'

    if [ -n "$release_notes" ]; then
      printf '%s\n' "$release_notes" | sed '/^[[:space:]]*$/d; s/^/- /'
    else
      printf -- '- No git summary available.\n'
    fi
  } > "$output_path"
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
