#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
EXPORT_DIR="$DEPLOY_DIR/backups/client-release-history"
TIMESTAMP=$(date +"%Y%m%d%H%M%S")
EXPORT_FILE="$EXPORT_DIR/client-release-history-$TIMESTAMP.sql"
CHECKSUM_FILE="$EXPORT_FILE.sha256"

. "$SCRIPT_DIR/release-common.sh"

require_docker_compose
load_dotenv
POSTGRES_READY_ATTEMPTS=${POSTGRES_READY_ATTEMPTS:-30}
require_decimal_range POSTGRES_READY_ATTEMPTS "$POSTGRES_READY_ATTEMPTS" 1 300 || exit $?

mkdir -p "$EXPORT_DIR"
compose up -d postgres >/dev/null

attempt=1
while [ "$attempt" -le "$POSTGRES_READY_ATTEMPTS" ]
do
  if compose exec -T postgres pg_isready -h 127.0.0.1 -U postgres -d iiot-db >/dev/null 2>&1; then
    break
  fi

  if [ "$attempt" -eq "$POSTGRES_READY_ATTEMPTS" ]; then
    printf 'PostgreSQL was not ready for client release history export after %s attempts.\n' "$POSTGRES_READY_ATTEMPTS" >&2
    exit 1
  fi

  sleep 2
  attempt=$((attempt + 1))
done

compose exec -T postgres pg_dump \
  -h 127.0.0.1 \
  -U postgres \
  -d iiot-db \
  --data-only \
  --column-inserts \
  --no-owner \
  --no-privileges \
  -t public.edge_client_release_components \
  -t public.edge_client_release_versions \
  -t public.edge_client_release_artifacts \
  -t public.edge_client_release_retention_policies \
  > "$EXPORT_FILE"

(
  cd "$EXPORT_DIR"
  sha256sum "$(basename "$EXPORT_FILE")" > "$(basename "$CHECKSUM_FILE")"
)

printf 'Client release history export created: %s\n' "$EXPORT_FILE"
printf 'Client release history checksum: %s\n' "$CHECKSUM_FILE"
printf 'Scope: edge_client_release_components, edge_client_release_versions, edge_client_release_artifacts, edge_client_release_retention_policies.\n'
