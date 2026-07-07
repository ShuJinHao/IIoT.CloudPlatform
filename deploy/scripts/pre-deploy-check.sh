#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

RELEASE_TAG=${1:-${RELEASE_TAG:-}}
ensure_release_tag "$RELEASE_TAG"
require_docker_compose
ensure_deploy_operator_not_root

cd "$DEPLOY_DIR"
if [ ! -f ./.env ]; then
  printf 'Missing deploy environment file: %s/.env\n' "$DEPLOY_DIR" >&2
  exit 66
fi

load_dotenv
ensure_required_secret_values_changed
ensure_required_public_values_changed
ensure_bootstrap_secret_not_disabled
ensure_oidc_http_boundary
ensure_rate_limit_values_bounded
require_app_image_values
require_infra_image_values
ensure_image_values_not_template
ensure_app_images_have_explicit_registry
ensure_infra_images_not_docker_hub
resolve_release_images "$RELEASE_TAG"
ensure_target_images_not_latest
compose config -q
"$SCRIPT_DIR/ensure-oidc-signing-cert.sh"
sh "$SCRIPT_DIR/check-container-nonroot-readiness.sh"
ensure_deploy_disk_headroom

runtime_preflight_controls=runtime-check-skipped-no-current-release
if [ -f "$CURRENT_RELEASE_FILE" ]; then
  public_base_url="http://127.0.0.1:${GATEWAY_HTTP_PORT:-80}"
  probe_status "${public_base_url}/internal/healthz" "200" 3
  REQUIRE_BACKUP=0 \
    REQUIRE_DATAWORKER_HEALTHCHECK=${PRE_DEPLOY_REQUIRE_DATAWORKER_HEALTHCHECK:-0} \
    BACKUP_MAX_AGE_HOURS=${PRE_DEPLOY_BACKUP_MAX_AGE_HOURS:-999999} \
    "$SCRIPT_DIR/ops-check.sh"
  runtime_preflight_controls="healthz-http-local ops-check-runtime"
fi

print_http_only_preflight_summary "$runtime_preflight_controls"
printf 'Pre-deploy checks passed for release tag: %s\n' "$RELEASE_TAG"
