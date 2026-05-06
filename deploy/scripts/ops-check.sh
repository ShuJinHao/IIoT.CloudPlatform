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

psql_scalar() {
  compose exec -T postgres psql -qtAX -U postgres -d iiot-db -c "$1"
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

OUTBOX_BACKLOG=$(psql_scalar "select count(*) from outbox_messages where processed_at_utc is null;")
OUTBOX_OLDEST_PENDING_AGE_SECONDS=$(psql_scalar "select coalesce(extract(epoch from (now() - min(occurred_at_utc)))::bigint::text, '0') from outbox_messages where processed_at_utc is null and abandoned_at_utc is null;")
UPLOAD_RECEIVE_REGISTRATION_COUNT=$(psql_scalar "select count(*) from upload_receive_registrations;")
TIMESCALE_EXTENSION_VERSION=$(psql_scalar "select coalesce((select extversion from pg_extension where extname = 'timescaledb'), 'missing');")
TIMESCALE_INFO_AVAILABLE=$(psql_scalar "select case when to_regclass('timescaledb_information.hypertables') is null then '0' else '1' end;")
RECORD_TABLE_STATS=$(psql_scalar "select c.relname || ' table_total=' || pg_size_pretty(pg_total_relation_size(c.oid)) || ' indexes=' || pg_size_pretty(pg_indexes_size(c.oid)) from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'public' and c.relkind in ('r','p') and c.relname in ('device_logs','hourly_capacity','pass_station_records') order by c.relname;")
QUEUE_SNAPSHOT=$(compose exec -T rabbitmq rabbitmqctl list_queues -q name messages)
NOW_EPOCH=$(date +%s)

HAS_RISK=0
if [ "$OUTBOX_BACKLOG" != "0" ]; then
  HAS_RISK=1
fi

if [ "$TIMESCALE_EXTENSION_VERSION" = "missing" ] || [ "$TIMESCALE_INFO_AVAILABLE" != "1" ]; then
  HAS_RISK=1
  HYPERTABLE_SNAPSHOT=""
  CHUNK_SNAPSHOT=""
  TIMESCALE_POLICY_SNAPSHOT=""
else
  HYPERTABLE_SNAPSHOT=$(psql_scalar "with expected(name) as (values ('device_logs'), ('hourly_capacity'), ('pass_station_records')) select expected.name || ':' || case when h.hypertable_name is null then 'missing' else 'present' end from expected left join timescaledb_information.hypertables h on h.hypertable_schema = 'public' and h.hypertable_name = expected.name order by expected.name;")
  CHUNK_SNAPSHOT=$(psql_scalar "with expected(name) as (values ('device_logs'), ('hourly_capacity'), ('pass_station_records')) select expected.name || ':' || count(c.chunk_name) from expected left join timescaledb_information.chunks c on c.hypertable_schema = 'public' and c.hypertable_name = expected.name group by expected.name order by expected.name;")
  TIMESCALE_POLICY_SNAPSHOT=$(psql_scalar "with expected(name) as (values ('device_logs'), ('hourly_capacity'), ('pass_station_records')) select expected.name || ' compression_policy=' || coalesce(max(case when j.proc_name = 'policy_compression' then 'present' end), 'missing') || ' retention_policy=' || coalesce(max(case when j.proc_name = 'policy_retention' then 'present' end), 'missing') from expected left join timescaledb_information.jobs j on j.hypertable_schema = 'public' and j.hypertable_name = expected.name group by expected.name order by expected.name;")
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

printf 'outbox_oldest_pending_age_seconds=%s upload_receive_registrations=%s timescale_extension=%s\n' \
  "$OUTBOX_OLDEST_PENDING_AGE_SECONDS" \
  "$UPLOAD_RECEIVE_REGISTRATION_COUNT" \
  "$TIMESCALE_EXTENSION_VERSION"

printf '%s\n' "$RECORD_TABLE_STATS" | while IFS= read -r table_stat
do
  if [ -n "$table_stat" ]; then
    printf 'record_table=%s\n' "$table_stat"
  fi
done

for record_table in device_logs hourly_capacity pass_station_records
do
  hypertable_state="missing"
  chunk_count="missing"

  if [ -n "$HYPERTABLE_SNAPSHOT" ]; then
    hypertable_state=$(printf '%s\n' "$HYPERTABLE_SNAPSHOT" | awk -F: -v target="$record_table" '
      $1 == target { print $2; found = 1; exit }
      END {
        if (!found) {
          print "missing"
        }
      }')
  fi

  if [ -n "$CHUNK_SNAPSHOT" ]; then
    chunk_count=$(printf '%s\n' "$CHUNK_SNAPSHOT" | awk -F: -v target="$record_table" '
      $1 == target { print $2; found = 1; exit }
      END {
        if (!found) {
          print "missing"
        }
      }')
  fi

  printf 'timescale_hypertable=%s status=%s chunk_count=%s\n' \
    "$record_table" \
    "$hypertable_state" \
    "$chunk_count"

  if [ "$hypertable_state" != "present" ]; then
    HAS_RISK=1
  fi
done

printf '%s\n' "$TIMESCALE_POLICY_SNAPSHOT" | while IFS= read -r policy_state
do
  if [ -n "$policy_state" ]; then
    printf 'timescale_policy=%s\n' "$policy_state"
  fi
done

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
