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

DEPLOY_RELEASE_LOCK_FILE=$(resolve_managed_lock_file \
  "${DEPLOY_RELEASE_LOCK_FILE:-$CLOUD_RELEASE_LOCK_FILE_DEFAULT}" \
  "$DEPLOY_DIR/.cloud-release.lock")
POST_RELEASE_CLEANUP_LOCK_FILE=$(resolve_managed_lock_file \
  "${POST_RELEASE_CLEANUP_LOCK_FILE:-$POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT}" \
  "$DEPLOY_DIR/.post-release-cleanup.lock")
export DEPLOY_RELEASE_LOCK_FILE POST_RELEASE_CLEANUP_LOCK_FILE

SUPPORT_MANIFEST="$DEPLOY_DIR/.cloud-support-manifest.sha256"
if [ ! -r "$SUPPORT_MANIFEST" ]; then
  printf 'Cloud deploy support manifest is missing or unreadable: %s\n' "$SUPPORT_MANIFEST" >&2
  printf 'Run the standard workspace deploy entry so allowlisted support files are staged, synchronized and verified before release.\n' >&2
  exit 66
fi
command -v sha256sum >/dev/null 2>&1 || {
  printf 'Required command not found for Cloud support verification: sha256sum\n' >&2
  exit 69
}
(cd "$DEPLOY_DIR" && sha256sum -c "$SUPPORT_MANIFEST")
printf 'preflight_support_manifest=verified path=%s\n' "$SUPPORT_MANIFEST"

ensure_managed_lock_available \
  "$DEPLOY_RELEASE_LOCK_FILE" \
  "${DEPLOY_RELEASE_LOCK_OWNER_PID:-}"
ensure_managed_lock_available "$POST_RELEASE_CLEANUP_LOCK_FILE"
printf 'preflight_release_lock=available-or-owned path=%s\n' "$DEPLOY_RELEASE_LOCK_FILE"
printf 'preflight_cleanup_lock=available path=%s\n' "$POST_RELEASE_CLEANUP_LOCK_FILE"

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
"$SCRIPT_DIR/check-release-state-access.sh"
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
