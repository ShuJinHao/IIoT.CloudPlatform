#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

load_dotenv

CONTAINER_UID=${CLOUD_CONTAINER_UID:-10001}
CONTAINER_GID=${CLOUD_CONTAINER_GID:-10001}

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

path_world_readable() {
  mode=$(mode_triplet "$1")
  digit_has_read "$(mode_digit "$mode" other)"
}

ensure_certificate_readable_by_container() {
  certificate=$1

  if [ "$(id -u)" = "$CONTAINER_UID" ]; then
    chmod 600 "$certificate"
    return
  fi

  if chgrp "$CONTAINER_GID" "$certificate" 2>/dev/null; then
    chmod 640 "$certificate"
    return
  fi

  if path_readable_by_container "$certificate" && ! path_world_readable "$certificate"; then
    return
  fi

  printf 'OIDC signing certificate is not readable by container uid/gid %s:%s: %s\n' "$CONTAINER_UID" "$CONTAINER_GID" "$certificate" >&2
  printf 'Fix ownership/mode before deploy, for example: chgrp %s %s && chmod 640 %s\n' "$CONTAINER_GID" "$certificate" "$certificate" >&2
  return 73
}

certificate_path=${OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH:-}
if [ -z "$certificate_path" ]; then
  printf 'OIDC signing certificate path is not configured; skipping certificate bootstrap.\n'
  exit 0
fi

certs_dir=${OIDC_PROVIDER_CERTS_DIR:-$DEPLOY_DIR/certs}
case "$certs_dir" in
  /*)
    ;;
  *)
    certs_dir="$DEPLOY_DIR/$certs_dir"
    ;;
esac

certificate_file_name=$(basename "$certificate_path")
target_certificate="$certs_dir/$certificate_file_name"

if [ -f "$target_certificate" ]; then
  ensure_certificate_readable_by_container "$target_certificate"
  printf 'OIDC signing certificate already exists and is container-readable: %s\n' "$target_certificate"
  exit 0
fi

require_command openssl
mkdir -p "$certs_dir"

if [ ! -w "$certs_dir" ]; then
  printf 'OIDC certificate directory is not writable by the runner: %s\n' "$certs_dir" >&2
  printf 'Fix ownership, for example: chown -R github-runner:github-runner %s\n' "$certs_dir" >&2
  exit 73
fi

tmp_key=$(mktemp "$certs_dir/.oidc-signing.XXXXXX.key")
tmp_crt=$(mktemp "$certs_dir/.oidc-signing.XXXXXX.crt")
cleanup_temp_cert_files() {
  rm -f "$tmp_key" "$tmp_crt"
}
trap cleanup_temp_cert_files EXIT HUP INT TERM

openssl req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes \
  -subj "/CN=IIoT Cloud OIDC Signing" \
  -keyout "$tmp_key" \
  -out "$tmp_crt" >/dev/null 2>&1

openssl pkcs12 -export \
  -inkey "$tmp_key" \
  -in "$tmp_crt" \
  -out "$target_certificate" \
  -name "iiot-cloud-oidc-signing" \
  -passout "pass:${OIDC_PROVIDER_SIGNING_CERTIFICATE_PASSWORD:-}" >/dev/null 2>&1

if ! ensure_certificate_readable_by_container "$target_certificate"; then
  rm -f "$target_certificate"
  exit 73
fi
cleanup_temp_cert_files
trap - EXIT HUP INT TERM

printf 'OIDC signing certificate generated: %s\n' "$target_certificate"
