#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

load_dotenv

CONTAINER_UID=${CLOUD_CONTAINER_UID:-10001}
CONTAINER_GID=${CLOUD_CONTAINER_GID:-10001}
HAS_RISK=0

fail_probe() {
  printf 'nonroot_readiness_error=%s\n' "$1" >&2
  HAS_RISK=1
}

require_non_root_numeric_identity() {
  case "$CONTAINER_UID:$CONTAINER_GID" in
    0:*|*:0)
      fail_probe "container uid/gid must not be root: $CONTAINER_UID:$CONTAINER_GID"
      ;;
    *[!0-9:]*|:*)
      fail_probe "container uid/gid must be numeric: $CONTAINER_UID:$CONTAINER_GID"
      ;;
  esac
}

mode_triplet() {
  stat_mode=$(path_mode "$1")
  printf '%s\n' "$stat_mode" | sed 's/.*\(...\)$/\1/'
}

path_mode() {
  if stat -c '%a' "$1" >/dev/null 2>&1; then
    stat -c '%a' "$1"
    return
  fi

  stat -f '%Lp' "$1"
}

mode_digit() {
  mode=$1
  position=$2

  case "$position" in
    owner)
      printf '%s\n' "${mode%??}"
      ;;
    group)
      rest=${mode#?}
      printf '%s\n' "${rest%?}"
      ;;
    other)
      printf '%s\n' "${mode##??}"
      ;;
  esac
}

digit_has_read() {
  case "$1" in
    4|5|6|7)
      return 0
      ;;
  esac
  return 1
}

digit_has_write() {
  case "$1" in
    2|3|6|7)
      return 0
      ;;
  esac
  return 1
}

digit_has_write_execute() {
  case "$1" in
    3|7)
      return 0
      ;;
  esac
  return 1
}

path_stat() {
  if stat -c '%u %g %a' "$1" >/dev/null 2>&1; then
    stat -c '%u %g %a' "$1"
    return
  fi

  stat -f '%u %g %Lp' "$1"
}

path_readable_by_container() {
  path=$1
  set -- $(path_stat "$path")
  owner_uid=$1
  owner_gid=$2
  mode=$(mode_triplet "$path")

  if [ "$CONTAINER_UID" = "$owner_uid" ]; then
    digit_has_read "$(mode_digit "$mode" owner)"
    return
  fi

  if [ "$CONTAINER_GID" = "$owner_gid" ]; then
    digit_has_read "$(mode_digit "$mode" group)"
    return
  fi

  digit_has_read "$(mode_digit "$mode" other)"
}

directory_writable_by_container() {
  path=$1
  set -- $(path_stat "$path")
  owner_uid=$1
  owner_gid=$2
  mode=$(mode_triplet "$path")

  if [ "$CONTAINER_UID" = "$owner_uid" ]; then
    digit_has_write_execute "$(mode_digit "$mode" owner)"
    return
  fi

  if [ "$CONTAINER_GID" = "$owner_gid" ]; then
    digit_has_write_execute "$(mode_digit "$mode" group)"
    return
  fi

  digit_has_write_execute "$(mode_digit "$mode" other)"
}

ensure_not_world_readable() {
  path=$1
  mode=$(mode_triplet "$path")
  if digit_has_read "$(mode_digit "$mode" other)"; then
    fail_probe "$path must not be world-readable"
  fi
}

ensure_not_world_writable_directory() {
  path=$1
  mode=$(mode_triplet "$path")
  if digit_has_write "$(mode_digit "$mode" other)"; then
    fail_probe "$path must not be world-writable"
  fi
}

volume_project_name() {
  if [ -n "${COMPOSE_PROJECT_NAME:-}" ]; then
    printf '%s\n' "$COMPOSE_PROJECT_NAME"
    return
  fi

  basename "$DEPLOY_DIR"
}

log_volume_writability_diagnostic() {
  key=$1
  suffix=$2
  project_name=$(volume_project_name)
  volume_name="${project_name}_${suffix}"

  if ! command -v docker >/dev/null 2>&1; then
    printf 'nonroot_readiness_log_volume_%s=docker-unavailable volume=%s\n' "$key" "$volume_name"
    return
  fi

  if ! docker volume inspect "$volume_name" >/dev/null 2>&1; then
    printf 'nonroot_readiness_log_volume_%s=not-created volume=%s\n' "$key" "$volume_name"
    return
  fi

  mountpoint=$(docker volume inspect -f '{{ .Mountpoint }}' "$volume_name" 2>/dev/null || true)
  if [ -z "$mountpoint" ] || [ ! -d "$mountpoint" ]; then
    printf 'nonroot_readiness_log_volume_%s=exists mountpoint-not-accessible volume=%s\n' "$key" "$volume_name"
    return
  fi

  writable_by_container=false
  if directory_writable_by_container "$mountpoint"; then
    writable_by_container=true
  fi

  world_writable=false
  mode=$(mode_triplet "$mountpoint")
  if digit_has_write "$(mode_digit "$mode" other)"; then
    world_writable=true
  fi

  printf 'nonroot_readiness_log_volume_%s=exists volume=%s writable_by_container=%s world_writable=%s\n' \
    "$key" \
    "$volume_name" \
    "$writable_by_container" \
    "$world_writable"
}

resolve_oidc_certificate_host_path() {
  certificate_path=${OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH:-}
  if [ -z "$certificate_path" ]; then
    fail_probe "OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH is required for non-root readiness"
    return
  fi

  case "$certificate_path" in
    /app/certs/*)
      certificate_file=${certificate_path##*/}
      printf '%s/%s\n' "${OIDC_PROVIDER_CERTS_DIR:-$DEPLOY_DIR/certs}" "$certificate_file"
      ;;
    *)
      fail_probe "OIDC signing certificate path must be under /app/certs for the current compose mount: $certificate_path"
      ;;
  esac
}

check_oidc_certificate() {
  cert_path=$(resolve_oidc_certificate_host_path || true)
  if [ -z "${cert_path:-}" ]; then
    return
  fi

  if [ ! -f "$cert_path" ]; then
    fail_probe "OIDC signing certificate file is missing: $cert_path"
    return
  fi

  if ! path_readable_by_container "$cert_path"; then
    fail_probe "OIDC signing certificate is not readable by $CONTAINER_UID:$CONTAINER_GID: $cert_path"
  fi

  ensure_not_world_readable "$cert_path"
}

check_edge_updates_directory() {
  edge_updates_dir=${EDGE_UPDATES_DIR:-/data/iiot-platform/edge-client/edge-updates}
  if [ ! -d "$edge_updates_dir" ]; then
    fail_probe "EDGE_UPDATES_DIR is missing: $edge_updates_dir"
    return
  fi

  if ! directory_writable_by_container "$edge_updates_dir"; then
    fail_probe "EDGE_UPDATES_DIR is not writable by $CONTAINER_UID:$CONTAINER_GID: $edge_updates_dir"
  fi

  ensure_not_world_writable_directory "$edge_updates_dir"

  for subdir_name in installers plugins velopack; do
    check_edge_updates_subdirectory "$edge_updates_dir" "$subdir_name"
  done
}

check_edge_updates_subdirectory() {
  edge_updates_root=$1
  subdir_name=$2
  subdir_path="$edge_updates_root/$subdir_name"

  if [ ! -e "$subdir_path" ]; then
    printf 'nonroot_readiness_edge_updates_subdir_%s=missing path=%s\n' "$subdir_name" "$subdir_path"
    return
  fi

  if [ ! -d "$subdir_path" ]; then
    fail_probe "EDGE_UPDATES_DIR subpath is not a directory: $subdir_path"
    return
  fi

  subdir_has_risk=0
  if ! directory_writable_by_container "$subdir_path"; then
    fail_probe "EDGE_UPDATES_DIR subdirectory is not writable by $CONTAINER_UID:$CONTAINER_GID: $subdir_path"
    subdir_has_risk=1
  fi

  mode=$(mode_triplet "$subdir_path")
  if digit_has_write "$(mode_digit "$mode" other)"; then
    fail_probe "$subdir_path must not be world-writable"
    subdir_has_risk=1
  fi

  if [ "$subdir_has_risk" -ne 0 ]; then
    printf 'nonroot_readiness_edge_updates_subdir_%s=failed path=%s\n' "$subdir_name" "$subdir_path"
    return
  fi

  printf 'nonroot_readiness_edge_updates_subdir_%s=checked path=%s\n' "$subdir_name" "$subdir_path"
}

compose_nginx_gateway_targets_port() {
  port=$1
  awk -v port="$port" '
    /^  nginx-gateway:/ {
      in_service = 1
      next
    }
    in_service && /^  [^[:space:]][^:]*:/ {
      in_service = 0
      in_ports = 0
    }
    in_service && /^    ports:/ {
      in_ports = 1
      next
    }
    in_service && in_ports && /^    [^[:space:]-][^:]*:/ {
      in_ports = 0
    }
    in_service && in_ports {
      line = $0
      sub(/[[:space:]]*#.*/, "", line)
      gsub(/"/, "", line)
      gsub(/\047/, "", line)

      if (line ~ "target:[[:space:]]*" port "[[:space:]]*$") {
        found = 1
      }

      if (line ~ /^[[:space:]]*-/) {
        sub(/^[[:space:]]*-[[:space:]]*/, "", line)
        n = split(line, parts, ":")
        if (parts[n] == port) {
          found = 1
        }
      }
    }
    END {
      exit found ? 0 : 1
    }
  ' "$DEPLOY_DIR/docker-compose.prod.yml"
}

check_nginx_low_port_blocker() {
  if grep -Eq 'listen[[:space:]]+80([[:space:];]|$)' "$DEPLOY_DIR/nginx/nginx.conf"; then
    fail_probe "nginx-gateway must not listen on container port 80"
  fi

  if grep -Eq 'proxy_pass[[:space:]]+http://iiot-web:80([/;[:space:]]|$)' "$DEPLOY_DIR/nginx/nginx.conf"; then
    fail_probe "nginx-gateway must not proxy iiot-web on container port 80"
  fi

  if compose_nginx_gateway_targets_port 80; then
    fail_probe "nginx-gateway compose port target must not be container port 80"
  fi

  if ! grep -Eq 'listen[[:space:]]+8080([[:space:];]|$)' "$DEPLOY_DIR/nginx/nginx.conf"; then
    fail_probe "nginx-gateway must listen on container port 8080"
  fi

  if ! grep -Eq 'proxy_pass[[:space:]]+http://iiot-web:8080([/;[:space:]]|$)' "$DEPLOY_DIR/nginx/nginx.conf"; then
    fail_probe "nginx-gateway must proxy iiot-web on container port 8080"
  fi

  if ! compose_nginx_gateway_targets_port 8080; then
    fail_probe "nginx-gateway compose port target must be container port 8080"
  fi

  printf 'nonroot_readiness_nginx_internal_port=8080\n'
}

check_log_volume_diagnostics() {
  log_volume_writability_diagnostic httpapi_logs httpapi-logs
  log_volume_writability_diagnostic dataworker_logs dataworker-logs
}

require_non_root_numeric_identity
check_oidc_certificate
check_edge_updates_directory
check_nginx_low_port_blocker
check_log_volume_diagnostics

printf 'nonroot_readiness_container_uid=%s container_gid=%s\n' "$CONTAINER_UID" "$CONTAINER_GID"

if [ "$HAS_RISK" -ne 0 ]; then
  exit 2
fi

printf 'nonroot_readiness=passed\n'
