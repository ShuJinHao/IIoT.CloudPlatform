#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
BACKUP_DIR="$DEPLOY_DIR/backups/postgres"
VERIFY_STATE_FILE="$BACKUP_DIR/latest-successful-verify.txt"
DUMP_FILE=${1:-}
TIMESTAMP=$(date +"%Y%m%d%H%M%S")
VERIFY_DATABASE="iiot-restore-verify-$TIMESTAMP"

. "$SCRIPT_DIR/release-common.sh"

locate_latest_dump() {
  find "$BACKUP_DIR" -maxdepth 1 -type f -name 'iiot-db-*.dump' | sort | tail -n 1
}

if [ -z "$DUMP_FILE" ]; then
  DUMP_FILE=$(locate_latest_dump)
fi

if [ -z "$DUMP_FILE" ]; then
  printf 'No PostgreSQL backup dump found under %s\n' "$BACKUP_DIR" >&2
  exit 66
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

cleanup_temp_database() {
  compose exec -T postgres dropdb --if-exists -U postgres "$VERIFY_DATABASE" >/dev/null 2>&1 || true
}

drop_temp_database_strict() {
  compose exec -T postgres dropdb --if-exists -U postgres "$VERIFY_DATABASE" >/dev/null
}

compose up -d postgres >/dev/null
(
  cd "$(dirname "$DUMP_FILE")"
  sha256sum -c "$(basename "$CHECKSUM_FILE")" >/dev/null
)

trap cleanup_temp_database EXIT INT TERM
compose exec -T postgres createdb -U postgres "$VERIFY_DATABASE"
cat "$DUMP_FILE" | compose exec -T postgres pg_restore --clean --if-exists --no-owner --no-privileges -U postgres -d "$VERIFY_DATABASE"
compose exec -T postgres psql -v ON_ERROR_STOP=1 -U postgres -d "$VERIFY_DATABASE" <<'SQL'
select count(*) from devices;
select count(*) from employees;
select count(*) from recipes;
select count(*) from outbox_messages;
select count(*) from "__EFMigrationsHistory";
SQL
drop_temp_database_strict
trap - EXIT INT TERM
mkdir -p "$BACKUP_DIR"
printf '%s\n' "$DUMP_FILE" > "$VERIFY_STATE_FILE"
touch "$VERIFY_STATE_FILE"

printf 'PostgreSQL backup verify completed: %s\n' "$DUMP_FILE"
