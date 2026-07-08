#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

require_post_deploy_value() {
  name=$1
  value=$2
  if [ -z "$value" ]; then
    printf '%s is required when POST_DEPLOY_VERIFY_OIDC_TOKEN=1.\n' "$name" >&2
    exit 64
  fi
}

path_mode() {
  if stat -c '%a' "$1" >/dev/null 2>&1; then
    stat -c '%a' "$1"
    return
  fi

  stat -f '%Lp' "$1"
}

mode_triplet() {
  stat_mode=$(path_mode "$1")
  printf '%s\n' "$stat_mode" | sed 's/.*\(...\)$/\1/'
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

ensure_post_deploy_secret_file_private() {
  name=$1
  path=$2
  mode=$(mode_triplet "$path")

  if [ "$(mode_digit "$mode" group)" != "0" ] ||
     [ "$(mode_digit "$mode" other)" != "0" ]; then
    printf '%s must not grant group or other permissions; use chmod 600 %s before running post-deploy token verification.\n' "$name" "$path" >&2
    exit 64
  fi
}

require_post_deploy_file() {
  name=$1
  path=$2
  require_post_deploy_value "$name" "$path"

  if [ ! -f "$path" ]; then
    printf '%s must point to a readable regular file when POST_DEPLOY_VERIFY_OIDC_TOKEN=1.\n' "$name" >&2
    exit 64
  fi

  if [ ! -r "$path" ]; then
    printf '%s must point to a readable regular file when POST_DEPLOY_VERIFY_OIDC_TOKEN=1.\n' "$name" >&2
    exit 64
  fi

  if [ ! -s "$path" ]; then
    printf '%s must not be empty when POST_DEPLOY_VERIFY_OIDC_TOKEN=1.\n' "$name" >&2
    exit 64
  fi

  ensure_post_deploy_secret_file_private "$name" "$path"
}

prepare_post_deploy_secret_file() {
  name=$1
  source_file=$2
  target_file=$3

  require_post_deploy_file "$name" "$source_file"
  if ! tr -d '\r\n' < "$source_file" > "$target_file"; then
    printf '%s could not be read for OIDC token verification.\n' "$name" >&2
    return 1
  fi

  chmod 600 "$target_file"
  if [ ! -s "$target_file" ]; then
    printf '%s contained no usable value after newline trimming.\n' "$name" >&2
    return 1
  fi
}

post_deploy_enabled() {
  case "${1:-0}" in
    1|true|TRUE|yes|YES)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

verify_oidc_token_exchange() {
  if ! post_deploy_enabled "${POST_DEPLOY_VERIFY_OIDC_TOKEN:-0}"; then
    printf 'post_deploy_oidc_token=skipped set POST_DEPLOY_VERIFY_OIDC_TOKEN=1 with a real authorization code and PKCE verifier to verify token issuance\n'
    return 0
  fi

  command -v python3 >/dev/null 2>&1 || {
    printf 'python3 is required when POST_DEPLOY_VERIFY_OIDC_TOKEN=1.\n' >&2
    exit 1
  }

  client_id=${POST_DEPLOY_OIDC_CLIENT_ID:-aicopilot}
  redirect_uri=${POST_DEPLOY_OIDC_REDIRECT_URI:-${AICOPILOT_OIDC_REDIRECT_URI:-}}
  authorization_code_source_file=${POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE:-}
  code_verifier_source_file=${POST_DEPLOY_OIDC_CODE_VERIFIER_FILE:-}

  if [ -n "${POST_DEPLOY_OIDC_AUTHORIZATION_CODE:-}" ] ||
     [ -n "${POST_DEPLOY_OIDC_CODE_VERIFIER:-}" ]; then
    printf 'OIDC code/verifier must be passed as files; use POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE and POST_DEPLOY_OIDC_CODE_VERIFIER_FILE.\n' >&2
    exit 64
  fi

  require_post_deploy_value POST_DEPLOY_OIDC_REDIRECT_URI "$redirect_uri"
  require_post_deploy_file POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE "$authorization_code_source_file"
  require_post_deploy_file POST_DEPLOY_OIDC_CODE_VERIFIER_FILE "$code_verifier_source_file"

  token_response_file=$(mktemp)
  authorization_code_file=$(mktemp)
  code_verifier_file=$(mktemp)
  chmod 600 "$token_response_file" "$authorization_code_file" "$code_verifier_file"
  if ! prepare_post_deploy_secret_file \
    POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE \
    "$authorization_code_source_file" \
    "$authorization_code_file"; then
    rm -f "$token_response_file" "$authorization_code_file" "$code_verifier_file"
    exit 1
  fi

  if ! prepare_post_deploy_secret_file \
    POST_DEPLOY_OIDC_CODE_VERIFIER_FILE \
    "$code_verifier_source_file" \
    "$code_verifier_file"; then
    rm -f "$token_response_file" "$authorization_code_file" "$code_verifier_file"
    exit 1
  fi

  if ! curl -fsS --request POST \
    --header 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=authorization_code' \
    --data-urlencode "client_id=$client_id" \
    --data-urlencode "redirect_uri=$redirect_uri" \
    --data-urlencode "code@$authorization_code_file" \
    --data-urlencode "code_verifier@$code_verifier_file" \
    --output "$token_response_file" \
    "${public_base_url}/connect/token"; then
    rm -f "$token_response_file" "$authorization_code_file" "$code_verifier_file"
    printf 'post_deploy_oidc_token=failed token endpoint did not return a successful response\n' >&2
    exit 1
  fi

  if ! python3 - "$token_response_file" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as response_file:
    payload = json.load(response_file)

access_token = payload.get("access_token")
token_type = payload.get("token_type")

if not isinstance(access_token, str) or not access_token:
    raise SystemExit("OIDC token response did not contain access_token.")

if not isinstance(token_type, str) or token_type.lower() != "bearer":
    raise SystemExit("OIDC token response did not contain bearer token_type.")
PY
  then
    rm -f "$token_response_file" "$authorization_code_file" "$code_verifier_file"
    printf 'post_deploy_oidc_token=failed token response did not contain a bearer access token\n' >&2
    exit 1
  fi

  rm -f "$token_response_file" "$authorization_code_file" "$code_verifier_file"
  printf 'post_deploy_oidc_token=verified client_id=%s\n' "$client_id"
}

verify_edge_installer_catalog() {
  if post_deploy_enabled "${POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG:-0}"; then
    edge_channel=${POST_DEPLOY_EDGE_CHANNEL:-stable}
    edge_target_runtime=${POST_DEPLOY_EDGE_TARGET_RUNTIME:-win-x64}
    BASE_URL="$public_base_url" \
      CHANNEL="$edge_channel" \
      TARGET_RUNTIME="$edge_target_runtime" \
      EXPECTED_VERSION="${POST_DEPLOY_EDGE_EXPECTED_VERSION:-}" \
      EXPECTED_PLUGIN_MODULE_ID="${POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID:-}" \
      EXPECTED_PLUGIN_VERSION="${POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION:-}" \
      "$SCRIPT_DIR/verify-edge-installer-catalog.sh"
    printf 'post_deploy_edge_installer_catalog=verified channel=%s target_runtime=%s\n' \
      "$edge_channel" \
      "$edge_target_runtime"
    return 0
  fi

  if post_deploy_enabled "${POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG:-0}"; then
    printf 'post_deploy_edge_installer_catalog=skipped required=1; Edge catalog verification is required for this deployment and cannot be skipped.\n' >&2
    exit 1
  fi

  printf 'post_deploy_edge_installer_catalog=skipped set POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG=1 to verify public catalog and static downloads\n'
}

require_docker_compose
load_dotenv

for service_name in nginx-gateway iiot-gateway iiot-httpapi iiot-dataworker iiot-web
do
  require_running_service "$service_name"
done
require_healthy_service "iiot-dataworker"

public_base_url="http://127.0.0.1:${GATEWAY_HTTP_PORT:-80}"
probe_status "${public_base_url}/" "200"
probe_status "${public_base_url}/internal/healthz" "200"
probe_status "${public_base_url}/.well-known/openid-configuration" "200"
probe_status "${public_base_url}/.well-known/jwks" "200"
verify_oidc_token_exchange
verify_edge_installer_catalog

"$SCRIPT_DIR/ops-check.sh"

printf 'Post-deploy checks passed.\n'
