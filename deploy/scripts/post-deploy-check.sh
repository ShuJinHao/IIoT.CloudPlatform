#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

require_docker_compose
load_dotenv

for service_name in nginx-gateway iiot-gateway iiot-httpapi iiot-dataworker iiot-web
do
  require_running_service "$service_name"
done

public_base_url="http://127.0.0.1:${GATEWAY_HTTP_PORT:-80}"
probe_status "${public_base_url}/" "200"
probe_status "${public_base_url}/internal/healthz" "200"
"$SCRIPT_DIR/ops-check.sh"

printf 'Post-deploy checks passed.\n'
