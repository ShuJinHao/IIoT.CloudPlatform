#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
IMPORT_FILE=${1:-}
ALLOW_OVERWRITE=${ALLOW_CLIENT_RELEASE_HISTORY_IMPORT_OVERWRITE:-false}

. "$SCRIPT_DIR/release-common.sh"

if [ -z "$IMPORT_FILE" ]; then
  printf 'Usage: %s <client-release-history.sql>\n' "$0" >&2
  exit 64
fi

if [ ! -f "$IMPORT_FILE" ]; then
  printf 'Client release history file not found: %s\n' "$IMPORT_FILE" >&2
  exit 66
fi

IMPORT_FILE=$(CDPATH= cd -- "$(dirname -- "$IMPORT_FILE")" && pwd)/$(basename "$IMPORT_FILE")
CHECKSUM_FILE="$IMPORT_FILE.sha256"

if [ ! -f "$CHECKSUM_FILE" ]; then
  printf 'Checksum file not found: %s\n' "$CHECKSUM_FILE" >&2
  exit 66
fi

require_docker_compose
load_dotenv

(
  cd "$(dirname "$IMPORT_FILE")"
  sha256sum -c "$(basename "$CHECKSUM_FILE")" >/dev/null
)

compose up -d postgres >/dev/null

existing_count=$(compose exec -T postgres psql \
  -h 127.0.0.1 \
  -U postgres \
  -d iiot-db \
  -At \
  -c "select (select count(*) from public.edge_client_release_components) + (select count(*) from public.edge_client_release_versions) + (select count(*) from public.edge_client_release_artifacts);" \
  | tr -d '\r\n ')

if [ "$existing_count" != "0" ]; then
  if [ "$ALLOW_OVERWRITE" != "true" ]; then
    printf 'Client release history import target is not empty (%s rows). Set ALLOW_CLIENT_RELEASE_HISTORY_IMPORT_OVERWRITE=true only when replacing a failed import.\n' "$existing_count" >&2
    exit 65
  fi

  compose exec -T postgres psql \
    -h 127.0.0.1 \
    -U postgres \
    -d iiot-db \
    -v ON_ERROR_STOP=1 \
    -c "truncate table public.edge_client_release_artifacts, public.edge_client_release_versions, public.edge_client_release_components, public.edge_client_release_retention_policies cascade;" \
    >/dev/null
fi

cat "$IMPORT_FILE" | compose exec -T postgres psql \
  -h 127.0.0.1 \
  -U postgres \
  -d iiot-db \
  -v ON_ERROR_STOP=1 \
  >/dev/null

component_count=$(compose exec -T postgres psql \
  -h 127.0.0.1 \
  -U postgres \
  -d iiot-db \
  -At \
  -c "select count(*) from public.edge_client_release_components;" \
  | tr -d '\r\n ')
version_count=$(compose exec -T postgres psql \
  -h 127.0.0.1 \
  -U postgres \
  -d iiot-db \
  -At \
  -c "select count(*) from public.edge_client_release_versions;" \
  | tr -d '\r\n ')
artifact_count=$(compose exec -T postgres psql \
  -h 127.0.0.1 \
  -U postgres \
  -d iiot-db \
  -At \
  -c "select count(*) from public.edge_client_release_artifacts;" \
  | tr -d '\r\n ')

printf 'Client release history imported from: %s\n' "$IMPORT_FILE"
printf 'Imported rows: components=%s versions=%s artifacts=%s\n' "$component_count" "$version_count" "$artifact_count"
