#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
BACKUP_DIR="$DEPLOY_DIR/backups/postgres"
BACKUP_STATE_FILE="$BACKUP_DIR/latest-successful-backup.txt"
TIMESTAMP=$(date +"%Y%m%d%H%M%S")
BACKUP_FILE="$BACKUP_DIR/iiot-db-$TIMESTAMP.dump"
CHECKSUM_FILE="$BACKUP_FILE.sha256"

. "$SCRIPT_DIR/release-common.sh"

require_docker_compose
load_dotenv

BACKUP_RETENTION_DAYS=${BACKUP_RETENTION_DAYS:-14}

cleanup_old_backups() {
  find "$BACKUP_DIR" -maxdepth 1 -type f -name 'iiot-db-*.dump' -mtime +"$BACKUP_RETENTION_DAYS" -print |
    while IFS= read -r dump_file
    do
      [ -n "$dump_file" ] || continue
      rm -f "$dump_file" "$dump_file.sha256"
    done

  find "$BACKUP_DIR" -maxdepth 1 -type f -name 'iiot-db-*.dump.sha256' -print |
    while IFS= read -r checksum_file
    do
      [ -n "$checksum_file" ] || continue
      dump_file=${checksum_file%.sha256}
      if [ ! -f "$dump_file" ]; then
        rm -f "$checksum_file"
      fi
    done
}

mkdir -p "$BACKUP_DIR"
compose up -d postgres >/dev/null
compose exec -T postgres pg_dump -Fc -U postgres -d iiot-db > "$BACKUP_FILE"
(
  cd "$BACKUP_DIR"
  sha256sum "$(basename "$BACKUP_FILE")" > "$(basename "$CHECKSUM_FILE")"
)
cleanup_old_backups
printf '%s\n' "$BACKUP_FILE" > "$BACKUP_STATE_FILE"
touch "$BACKUP_STATE_FILE"

printf 'PostgreSQL backup created: %s\n' "$BACKUP_FILE"
printf 'PostgreSQL backup checksum: %s\n' "$CHECKSUM_FILE"
