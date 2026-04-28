#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

RELEASE_TAG=${1:-${RELEASE_TAG:-}}
ensure_release_tag "$RELEASE_TAG"
require_docker_compose

cd "$DEPLOY_DIR"
if [ ! -f ./.env ]; then
  printf 'Missing deploy environment file: %s/.env\n' "$DEPLOY_DIR" >&2
  exit 66
fi

load_dotenv
require_app_image_values
resolve_release_images "$RELEASE_TAG"
ensure_target_images_not_latest
compose config -q

if [ -f "$CURRENT_RELEASE_FILE" ]; then
  public_base_url="http://127.0.0.1:${GATEWAY_HTTP_PORT:-80}"
  probe_status "${public_base_url}/internal/healthz" "200" 3
  "$SCRIPT_DIR/ops-check.sh"
fi

printf 'Pre-deploy checks passed for release tag: %s\n' "$RELEASE_TAG"
