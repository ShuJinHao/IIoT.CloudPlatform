#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

load_dotenv

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
  printf 'OIDC signing certificate already exists: %s\n' "$target_certificate"
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

chmod 600 "$target_certificate"
cleanup_temp_cert_files
trap - EXIT HUP INT TERM

printf 'OIDC signing certificate generated: %s\n' "$target_certificate"
