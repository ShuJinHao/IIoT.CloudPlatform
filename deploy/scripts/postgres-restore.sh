#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
DUMP_FILE=${1:-}

. "$SCRIPT_DIR/release-common.sh"

if [ -z "$DUMP_FILE" ]; then
  printf 'Usage: %s <dump-file>\n' "$0" >&2
  exit 64
fi

if [ ! -f "$DUMP_FILE" ]; then
  printf 'Dump file not found: %s\n' "$DUMP_FILE" >&2
  exit 66
fi

DUMP_FILE=$(CDPATH= cd -- "$(dirname -- "$DUMP_FILE")" && pwd)/$(basename "$DUMP_FILE")
CHECKSUM_FILE="$DUMP_FILE.sha256"

if [ ! -f "$CHECKSUM_FILE" ]; then
  printf 'Checksum file not found: %s\n' "$CHECKSUM_FILE" >&2
  exit 66
fi

require_docker_compose
load_dotenv

(
  cd "$(dirname "$DUMP_FILE")"
  sha256sum -c "$(basename "$CHECKSUM_FILE")" >/dev/null
)

compose up -d postgres redis-cache rabbitmq seq >/dev/null
compose stop nginx-gateway iiot-web iiot-gateway iiot-httpapi iiot-dataworker >/dev/null
cat "$DUMP_FILE" | compose exec -T postgres pg_restore --clean --if-exists --no-owner --no-privileges -U postgres -d iiot-db
compose run --rm iiot-migration
compose up -d iiot-httpapi iiot-gateway iiot-dataworker iiot-web nginx-gateway >/dev/null
"$SCRIPT_DIR/ops-check.sh"

printf 'PostgreSQL restore completed from: %s\n' "$DUMP_FILE"
