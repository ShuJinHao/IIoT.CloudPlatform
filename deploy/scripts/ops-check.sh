#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
BACKUP_DIR="$DEPLOY_DIR/backups/postgres"
BACKUP_STATE_FILE="$BACKUP_DIR/latest-successful-backup.txt"
VERIFY_STATE_FILE="$BACKUP_DIR/latest-successful-verify.txt"

. "$SCRIPT_DIR/release-common.sh"

require_docker_compose
load_dotenv

BACKUP_MAX_AGE_HOURS=${BACKUP_MAX_AGE_HOURS:-24}
BACKUP_VERIFY_MAX_AGE_DAYS=${BACKUP_VERIFY_MAX_AGE_DAYS:-7}

require_running_service() {
  service_name=$1
  if ! compose ps --status running --services | grep -qx "$service_name"; then
    printf 'Required service is not running: %s\n' "$service_name" >&2
    compose ps >&2
    exit 1
  fi
}

lookup_queue_depth() {
  queue_name=$1
  printf '%s\n' "$QUEUE_SNAPSHOT" | awk -v target="$queue_name" '
    $1 == target { print $2; found = 1; exit }
    END {
      if (!found) {
        print 0
      }
    }'
}

resolve_queue_name() {
  semantic_name=$1
  endpoint_prefix=${Infrastructure__EventBus__EndpointPrefix:-}

  if [ -n "$endpoint_prefix" ]; then
    printf '%s-%s\n' "$endpoint_prefix" "$semantic_name"
    return
  fi

  printf '%s\n' "$semantic_name"
}

read_state_path() {
  state_file=$1
  if [ ! -f "$state_file" ]; then
    return 1
  fi

  state_path=$(sed -n '1p' "$state_file")
  if [ -z "$state_path" ]; then
    return 1
  fi

  printf '%s\n' "$state_path"
}

PUBLIC_BASE_URL="http://127.0.0.1:${GATEWAY_HTTP_PORT:-80}"
for service_name in postgres redis-cache rabbitmq seq iiot-httpapi iiot-gateway iiot-dataworker iiot-web nginx-gateway
do
  require_running_service "$service_name"
done

health_status=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 "${PUBLIC_BASE_URL}/internal/healthz" || true)
if [ "$health_status" != "200" ]; then
  printf 'Internal health probe failed: %s/internal/healthz -> %s\n' "$PUBLIC_BASE_URL" "${health_status:-curl-error}" >&2
  exit 1
fi

OUTBOX_BACKLOG=$(compose exec -T postgres psql -qtAX -U postgres -d iiot-db -c "select count(*) from outbox_messages where processed_at_utc is null;")
QUEUE_SNAPSHOT=$(compose exec -T rabbitmq rabbitmqctl list_queues -q name messages)
NOW_EPOCH=$(date +%s)

HAS_RISK=0
if [ "$OUTBOX_BACKLOG" != "0" ]; then
  HAS_RISK=1
fi

LATEST_BACKUP_FILE="missing"
LATEST_BACKUP_AGE_HOURS="missing"
if latest_backup_path=$(read_state_path "$BACKUP_STATE_FILE"); then
  LATEST_BACKUP_FILE="$latest_backup_path"
  backup_marker_epoch=$(stat -c %Y "$BACKUP_STATE_FILE")
  LATEST_BACKUP_AGE_HOURS=$(((NOW_EPOCH - backup_marker_epoch) / 3600))
  if [ ! -f "$latest_backup_path" ] || [ ! -f "$latest_backup_path.sha256" ]; then
    HAS_RISK=1
  fi
  if [ "$LATEST_BACKUP_AGE_HOURS" -gt "$BACKUP_MAX_AGE_HOURS" ]; then
    HAS_RISK=1
  fi
else
  HAS_RISK=1
fi

LATEST_BACKUP_VERIFIED_AGE_DAYS="missing"
if latest_verified_path=$(read_state_path "$VERIFY_STATE_FILE"); then
  verify_marker_epoch=$(stat -c %Y "$VERIFY_STATE_FILE")
  LATEST_BACKUP_VERIFIED_AGE_DAYS=$(((NOW_EPOCH - verify_marker_epoch) / 86400))
  if [ ! -f "$latest_verified_path" ] || [ ! -f "$latest_verified_path.sha256" ]; then
    HAS_RISK=1
  fi
  if [ "$LATEST_BACKUP_VERIFIED_AGE_DAYS" -gt "$BACKUP_VERIFY_MAX_AGE_DAYS" ]; then
    HAS_RISK=1
  fi
else
  HAS_RISK=1
fi

printf 'internal_healthz=200 outbox_backlog=%s latest_backup_age_hours=%s latest_backup_verified_age_days=%s latest_backup_file="%s"\n' \
  "$OUTBOX_BACKLOG" \
  "$LATEST_BACKUP_AGE_HOURS" \
  "$LATEST_BACKUP_VERIFIED_AGE_DAYS" \
  "$LATEST_BACKUP_FILE"

for semantic_name in \
  iiot-pass-station-batches \
  iiot-device-logs \
  iiot-hourly-capacities
do
  queue_name=$(resolve_queue_name "$semantic_name")
  error_queue="${queue_name}_error"
  skipped_queue="${queue_name}_skipped"
  queue_depth=$(lookup_queue_depth "$queue_name")
  error_depth=$(lookup_queue_depth "$error_queue")
  skipped_depth=$(lookup_queue_depth "$skipped_queue")

  printf 'queue=%s depth=%s error_depth=%s skipped_depth=%s\n' \
    "$queue_name" \
    "$queue_depth" \
    "$error_depth" \
    "$skipped_depth"

  if [ "$error_depth" != "0" ] || [ "$skipped_depth" != "0" ]; then
    HAS_RISK=1
  fi
done

if [ "$HAS_RISK" -ne 0 ]; then
  exit 2
fi

exit 0
