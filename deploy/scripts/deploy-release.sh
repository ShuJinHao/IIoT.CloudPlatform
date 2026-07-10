#!/bin/sh
set -eu

if [ -n "${DEPLOY_RELEASE_HANDOFF_MARKER:-}" ]; then
  [ -n "${DEPLOY_SUPPORT_STAGING_DIR:-}" ] \
    && [ "$DEPLOY_RELEASE_HANDOFF_MARKER" = "$DEPLOY_SUPPORT_STAGING_DIR/.deploy-release-started" ] \
    || {
      printf 'Cloud deploy-release handoff marker escaped the invocation staging directory.\n' >&2
      exit 64
    }
  printf 'started\n' >"$DEPLOY_RELEASE_HANDOFF_MARKER"
fi

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DEPLOY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

. "$SCRIPT_DIR/release-common.sh"

DEPLOY_CONFIG_LOCK_FILE=$(resolve_managed_lock_file \
  "$CLOUD_CONFIG_LOCK_FILE_DEFAULT" \
  "$DEPLOY_DIR/.cloud-config.lock")
DEPLOY_CONFIG_LOCK_ACQUIRED=0

lock_is_owned_by_workspace_invocation() {
  ownership_lock_file=${DEPLOY_RELEASE_LOCK_FILE:-}
  ownership_invocation=${WORKSPACE_INVOCATION_ID:-${IIOT_WORKSPACE_DEPLOY_INVOCATION_ID:-}}
  ownership_plan=${WORKSPACE_PLAN_DIGEST:-${IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST:-}}
  ownership_pid=${DEPLOY_RELEASE_LOCK_OWNER_PID:-$$}
  [ -n "$ownership_lock_file" ] \
    && [ "$(managed_lock_read_field "${ownership_lock_file}.d" pid)" = "$ownership_pid" ] \
    && [ "$(managed_lock_read_field "${ownership_lock_file}.d" invocation-id)" = "$ownership_invocation" ] \
    && [ "$(managed_lock_read_field "${ownership_lock_file}.d" plan-digest)" = "$ownership_plan" ]
}

write_deploy_blocked_evidence() {
  blocked_phase=$1
  blocked_exit_code=$2
  blocked_restore_status=$3
  mkdir -p "$RELEASES_DIR/history"
  blocked_file="$RELEASES_DIR/deploy-blocked.env"
  blocked_history="$RELEASES_DIR/history/$(date -u +%Y%m%dT%H%M%SZ)-blocked-$(safe_release_file_name "${WORKSPACE_INVOCATION_ID:-unknown}").env"
  {
    printf 'DEPLOY_INVOCATION_ID=%s\n' "${WORKSPACE_INVOCATION_ID:-unknown}"
    printf 'DEPLOY_EXPECTED_SHA=%s\n' "${WORKSPACE_EXPECTED_SHA:-unknown}"
    printf 'DEPLOY_PLAN_DIGEST=%s\n' "${WORKSPACE_PLAN_DIGEST:-unknown}"
    printf 'DEPLOY_PHASE=%s\n' "$blocked_phase"
    printf 'DEPLOY_EXIT_CODE=%s\n' "$blocked_exit_code"
    printf 'DEPLOY_SUPPORT_RESTORE_STATUS=%s\n' "$blocked_restore_status"
    printf 'DEPLOY_SUPPORT_BACKUP_DIR=%s\n' "${DEPLOY_SUPPORT_BACKUP_DIR:-missing}"
    printf 'RECORDED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } | atomic_write_file "$blocked_file" 600
  atomic_copy_file "$blocked_file" "$blocked_history" 600
  printf 'Cloud durable blocked evidence: %s\n' "$blocked_file" >&2
}

restore_support_backup() {
  restore_backup_dir=${DEPLOY_SUPPORT_BACKUP_DIR:-}
  [ -n "$restore_backup_dir" ] || return 0
  [ -x "$restore_backup_dir/restore-support.sh" ] || return 86
  sh "$restore_backup_dir/restore-support.sh" "$DEPLOY_DIR" "$restore_backup_dir"
}

cleanup_early_release_contract() {
  early_status=$?
  trap - EXIT HUP INT TERM
  if [ -n "${DEPLOY_SUPPORT_BACKUP_DIR:-}" ] && [ -d "$DEPLOY_SUPPORT_BACKUP_DIR" ]; then
    if restore_support_backup; then
      rm -rf "$DEPLOY_SUPPORT_BACKUP_DIR"
    else
      write_deploy_blocked_evidence blocked-early-support-restore "$early_status" failed
      lock_is_owned_by_workspace_invocation \
        && printf '%s\n' blocked-early-support-restore | atomic_write_file "${DEPLOY_RELEASE_LOCK_FILE}.d/phase" 600
      exit 86
    fi
  fi
  if [ "${DEPLOY_RELEASE_LOCK_PREACQUIRED:-0}" = 1 ] \
    && [ "${DEPLOY_RELEASE_LOCK_PARENT_OWNED:-0}" != 1 ] \
    && lock_is_owned_by_workspace_invocation; then
    release_managed_lock "$DEPLOY_RELEASE_LOCK_FILE" 2>/dev/null || true
  fi
  if [ "$DEPLOY_CONFIG_LOCK_ACQUIRED" -eq 1 ] \
    && [ "$(managed_lock_read_field "${DEPLOY_CONFIG_LOCK_FILE}.d" pid)" = "$$" ]; then
    release_managed_lock "$DEPLOY_CONFIG_LOCK_FILE" 2>/dev/null || true
    DEPLOY_CONFIG_LOCK_ACQUIRED=0
  fi
  if [ -n "${DEPLOY_SUPPORT_STAGING_DIR:-}" ]; then
    rm -rf "$DEPLOY_SUPPORT_STAGING_DIR"
  fi
  exit "$early_status"
}
trap cleanup_early_release_contract EXIT HUP INT TERM

RELEASE_TAG=${RELEASE_TAG:-}
REQUESTED_SERVICES=${DEPLOY_SERVICES:-}

while [ "$#" -gt 0 ]
do
  case "$1" in
    --services)
      shift
      REQUESTED_SERVICES=${1:-}
      ;;
    --services=*)
      REQUESTED_SERVICES=${1#--services=}
      ;;
    -*)
      printf 'Unknown deploy-release option: %s\n' "$1" >&2
      exit 64
      ;;
    *)
      if [ -n "$RELEASE_TAG" ]; then
        if [ "$RELEASE_TAG" != "$1" ]; then
          printf 'Unexpected deploy-release argument: %s\n' "$1" >&2
          exit 64
        fi
      else
        RELEASE_TAG=$1
      fi
      ;;
  esac
  shift
done

WORKSPACE_ENTRYPOINT=${IIOT_WORKSPACE_DEPLOY_ENTRYPOINT:-}
WORKSPACE_INVOCATION_ID=${IIOT_WORKSPACE_DEPLOY_INVOCATION_ID:-}
WORKSPACE_EXPECTED_SHA=${IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA:-}
WORKSPACE_PLAN_DIGEST=${IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST:-}
DEPLOY_GIT_SHA_VALUE=${DEPLOY_GIT_SHA:-}
DEPLOY_IMAGE_MANIFEST_PATH=${DEPLOY_IMAGE_MANIFEST:-}
DEPLOY_IMAGE_MANIFEST_DIGEST=${DEPLOY_IMAGE_MANIFEST_SHA256:-}
DEPLOY_IMAGE_CONTENT_DIGEST=${DEPLOY_IMAGE_CONTENT_SHA256:-}
DEPLOY_SUPPORT_BACKUP_DIR=${DEPLOY_SUPPORT_BACKUP_DIR:-}
DEPLOY_TRANSACTION_MARKER_PATH=${DEPLOY_TRANSACTION_MARKER:-}

fail_contract() {
  printf 'Cloud release contract rejected: %s\n' "$*" >&2
  exit 64
}

[ "$WORKSPACE_ENTRYPOINT" = 1 ] \
  || fail_contract "missing IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1"
case "$WORKSPACE_INVOCATION_ID" in
  ''|*[!A-Za-z0-9._:-]*) fail_contract "invalid IIOT_WORKSPACE_DEPLOY_INVOCATION_ID" ;;
esac
case "$WORKSPACE_EXPECTED_SHA" in
  *[!0-9a-f]*) fail_contract "IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA must be lowercase hex" ;;
esac
[ "${#WORKSPACE_EXPECTED_SHA}" -eq 40 ] \
  || fail_contract "IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA must contain 40 hex characters"
case "$WORKSPACE_PLAN_DIGEST" in
  *[!0-9a-f]*) fail_contract "IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST must be lowercase hex" ;;
esac
[ "${#WORKSPACE_PLAN_DIGEST}" -eq 64 ] \
  || fail_contract "IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST must contain 64 hex characters"
[ "$RELEASE_TAG" = "sha-$WORKSPACE_EXPECTED_SHA" ] \
  || fail_contract "release tag, expected SHA and approved plan do not match: tag=$RELEASE_TAG expected=sha-$WORKSPACE_EXPECTED_SHA"
[ "$DEPLOY_GIT_SHA_VALUE" = "$WORKSPACE_EXPECTED_SHA" ] \
  || fail_contract "DEPLOY_GIT_SHA, release tag and expected SHA do not match: deploy_git_sha=${DEPLOY_GIT_SHA_VALUE:-missing} expected=$WORKSPACE_EXPECTED_SHA"
case "$DEPLOY_IMAGE_MANIFEST_DIGEST" in
  *[!0-9a-f]*) fail_contract "DEPLOY_IMAGE_MANIFEST_SHA256 must be lowercase hex" ;;
esac
[ "${#DEPLOY_IMAGE_MANIFEST_DIGEST}" -eq 64 ] \
  || fail_contract "DEPLOY_IMAGE_MANIFEST_SHA256 must contain 64 hex characters"
case "$DEPLOY_IMAGE_CONTENT_DIGEST" in
  *[!0-9a-f]*) fail_contract "DEPLOY_IMAGE_CONTENT_SHA256 must be lowercase hex" ;;
esac
[ "${#DEPLOY_IMAGE_CONTENT_DIGEST}" -eq 64 ] \
  || fail_contract "DEPLOY_IMAGE_CONTENT_SHA256 must contain 64 hex characters"
[ -f "$DEPLOY_IMAGE_MANIFEST_PATH" ] && [ -r "$DEPLOY_IMAGE_MANIFEST_PATH" ] && [ ! -L "$DEPLOY_IMAGE_MANIFEST_PATH" ] \
  || fail_contract "run-bound image manifest is missing or unreadable: ${DEPLOY_IMAGE_MANIFEST_PATH:-missing}"
case "$(readlink -f "$DEPLOY_IMAGE_MANIFEST_PATH")" in
  "$(readlink -f "${DEPLOY_SUPPORT_STAGING_DIR:-/missing}")"/*) ;;
  *) fail_contract "run-bound image manifest escaped its invocation staging directory" ;;
esac
[ "$DEPLOY_SUPPORT_BACKUP_DIR" = "$DEPLOY_DIR/releases/support-recovery/$WORKSPACE_INVOCATION_ID" ] \
  || fail_contract "support backup directory is not bound to this invocation"
[ -d "$DEPLOY_SUPPORT_BACKUP_DIR" ] && [ ! -L "$DEPLOY_SUPPORT_BACKUP_DIR" ] \
  || fail_contract "support backup directory is missing or is a symlink"
[ "$(readlink -f "$DEPLOY_SUPPORT_BACKUP_DIR")" = "$DEPLOY_SUPPORT_BACKUP_DIR" ] \
  || fail_contract "support backup directory is not canonical"
[ "$DEPLOY_TRANSACTION_MARKER_PATH" = "$DEPLOY_DIR/releases/transactions/$WORKSPACE_INVOCATION_ID.env" ] \
  || fail_contract "durable transaction marker is not bound to this invocation"
[ -f "$DEPLOY_TRANSACTION_MARKER_PATH" ] && [ ! -L "$DEPLOY_TRANSACTION_MARKER_PATH" ] \
  || fail_contract "durable transaction marker is missing or unsafe"
[ "$(read_manifest_value "$DEPLOY_TRANSACTION_MARKER_PATH" DEPLOY_INVOCATION_ID)" = "$WORKSPACE_INVOCATION_ID" ] \
  || fail_contract "durable transaction marker invocation mismatch"
[ "$(read_manifest_value "$DEPLOY_TRANSACTION_MARKER_PATH" DEPLOY_PLAN_DIGEST)" = "$WORKSPACE_PLAN_DIGEST" ] \
  || fail_contract "durable transaction marker plan mismatch"

ensure_release_tag "$RELEASE_TAG"
require_docker_compose
prepare_release_directories
acquire_strict_managed_lock \
  "$DEPLOY_CONFIG_LOCK_FILE" \
  cloud-operator-config \
  "$RELEASE_TAG" \
  snapshot \
  deploy-release
DEPLOY_CONFIG_LOCK_ACQUIRED=1
load_dotenv
DEPLOY_ORIGINAL_ENV_SHA256=$(sha256_file "$DEPLOY_DIR/.env")
DEPLOY_CONFIG_LOCK_TEST_HOOK=${DEPLOY_CONFIG_LOCK_TEST_HOOK:-}
readonly DEPLOY_ORIGINAL_ENV_SHA256 DEPLOY_CONFIG_LOCK_TEST_HOOK
DEPLOY_CONFIG_DIGEST=$(canonical_cloud_config_sha256 "$DEPLOY_DIR/.env")
case "$DEPLOY_CONFIG_DIGEST" in
  *[!0-9a-f]*|'') fail_contract "canonical Cloud configuration digest is invalid" ;;
esac
[ "${#DEPLOY_CONFIG_DIGEST}" -eq 64 ] \
  || fail_contract "canonical Cloud configuration digest must contain 64 hex characters"

actual_image_manifest_digest=$(sha256sum "$DEPLOY_IMAGE_MANIFEST_PATH" | awk '{print $1}')
[ "$actual_image_manifest_digest" = "$DEPLOY_IMAGE_MANIFEST_DIGEST" ] \
  || fail_contract "image manifest digest changed before rollout: expected=$DEPLOY_IMAGE_MANIFEST_DIGEST actual=$actual_image_manifest_digest"
image_content_file=$(mktemp "$DEPLOY_DIR/.image-content.XXXXXX")
grep -E '^(CLOUD_DEPLOY_(EXPECTED_SHA|PLAN_DIGEST|RELEASE_TAG|SERVICES)|IIOT_(HTTPAPI|GATEWAY|DATAWORKER|MIGRATION|WEB)_IMAGE(_DIGEST)?)=' "$DEPLOY_IMAGE_MANIFEST_PATH" \
  | LC_ALL=C sort > "$image_content_file"
actual_image_content_digest=$(sha256sum "$image_content_file" | awk '{print $1}')
rm -f "$image_content_file"
[ "$actual_image_content_digest" = "$DEPLOY_IMAGE_CONTENT_DIGEST" ] \
  || fail_contract "stable image candidate content digest mismatch: expected=$DEPLOY_IMAGE_CONTENT_DIGEST actual=$actual_image_content_digest"
[ "$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" CLOUD_DEPLOY_INVOCATION_ID)" = "$WORKSPACE_INVOCATION_ID" ] \
  || fail_contract "image manifest invocation does not match the remote invocation"
[ "$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" CLOUD_DEPLOY_EXPECTED_SHA)" = "$WORKSPACE_EXPECTED_SHA" ] \
  || fail_contract "image manifest SHA does not match the remote invocation"
[ "$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" CLOUD_DEPLOY_PLAN_DIGEST)" = "$WORKSPACE_PLAN_DIGEST" ] \
  || fail_contract "image manifest plan digest does not match the remote invocation"
[ "$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" CLOUD_DEPLOY_RELEASE_TAG)" = "$RELEASE_TAG" ] \
  || fail_contract "image manifest release tag does not match the remote invocation"

normalize_services() {
  services_input=${1:-}
  if [ -z "$services_input" ]; then
    printf '%s\n' "iiot-httpapi iiot-gateway iiot-dataworker iiot-migration iiot-web"
    return
  fi

  normalized_services=""
  for service in $(printf '%s' "$services_input" | tr ',' ' ')
  do
    case "$service" in
      httpapi|iiot-httpapi)
        normalized=iiot-httpapi
        ;;
      gateway|iiot-gateway)
        normalized=iiot-gateway
        ;;
      dataworker|iiot-dataworker)
        normalized=iiot-dataworker
        ;;
      migration|iiot-migration)
        normalized=iiot-migration
        ;;
      web|iiot-web)
        normalized=iiot-web
        ;;
      *)
        printf 'Unsupported deploy service: %s\n' "$service" >&2
        exit 64
        ;;
    esac

    case " $normalized_services " in
      *" $normalized "*)
        ;;
      *)
        normalized_services="$normalized_services $normalized"
        ;;
    esac
  done

  printf '%s\n' "$(printf '%s' "$normalized_services" | awk '{$1=$1; print}')"
}

image_key_for_service() {
  case "$1" in
    iiot-httpapi)
      printf '%s\n' IIOT_HTTPAPI_IMAGE
      ;;
    iiot-gateway)
      printf '%s\n' IIOT_GATEWAY_IMAGE
      ;;
    iiot-dataworker)
      printf '%s\n' IIOT_DATAWORKER_IMAGE
      ;;
    iiot-migration)
      printf '%s\n' IIOT_MIGRATION_IMAGE
      ;;
    iiot-web)
      printf '%s\n' IIOT_WEB_IMAGE
      ;;
    *)
      printf 'Unsupported deploy service: %s\n' "$1" >&2
      exit 64
      ;;
  esac
}

service_for_image_key() {
  case "$1" in
    IIOT_HTTPAPI_IMAGE)
      printf '%s\n' iiot-httpapi
      ;;
    IIOT_GATEWAY_IMAGE)
      printf '%s\n' iiot-gateway
      ;;
    IIOT_DATAWORKER_IMAGE)
      printf '%s\n' iiot-dataworker
      ;;
    IIOT_MIGRATION_IMAGE)
      printf '%s\n' iiot-migration
      ;;
    IIOT_WEB_IMAGE)
      printf '%s\n' iiot-web
      ;;
    *)
      printf 'Unsupported image key: %s\n' "$1" >&2
      exit 64
      ;;
  esac
}

image_key_is_selected() {
  key=$1
  case " $SELECTED_IMAGE_KEYS " in
    *" $key "*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

load_selected_images_from_invocation_manifest() {
  for selected_key in $SELECTED_IMAGE_KEYS
  do
    selected_image_ref=$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" "$selected_key")
    selected_image_digest=$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" "${selected_key}_DIGEST")
    case "$selected_image_ref" in
      *:"$RELEASE_TAG") ;;
      *) fail_contract "selected image is not bound to the approved release tag: $selected_key=$selected_image_ref" ;;
    esac
    case "$selected_image_digest" in
      sha256:*) ;;
      *) fail_contract "selected image digest is missing: ${selected_key}_DIGEST=$selected_image_digest" ;;
    esac
    digest_hex=${selected_image_digest#sha256:}
    case "$digest_hex" in
      *[!0-9a-f]*) fail_contract "selected image digest must be lowercase hex: ${selected_key}_DIGEST=$selected_image_digest" ;;
    esac
    [ "${#digest_hex}" -eq 64 ] \
      || fail_contract "selected image digest must contain 64 hex characters: ${selected_key}_DIGEST=$selected_image_digest"
    eval "$selected_key=\$selected_image_ref"
    eval "${selected_key}_DIGEST=\$selected_image_digest"
  done
}

verify_selected_image_digests() {
  for selected_key in $SELECTED_IMAGE_KEYS
  do
    eval "selected_image_ref=\${$selected_key}"
    eval "selected_image_digest=\${${selected_key}_DIGEST}"
    repo_digests=$(docker image inspect --format '{{json .RepoDigests}}' "$selected_image_ref" 2>/dev/null || true)
    case "$repo_digests" in
      *"@$selected_image_digest"*)
        printf 'Cloud pulled image digest verified: image=%s digest=%s\n' "$selected_image_ref" "$selected_image_digest"
        ;;
      *)
        printf 'Pulled Cloud image does not match the run-bound OCI digest: image=%s expected=%s repo_digests=%s\n' \
          "$selected_image_ref" "$selected_image_digest" "${repo_digests:-missing}" >&2
        exit 66
        ;;
    esac
  done
}

running_selected_images_match_digests() {
  for running_key in $SELECTED_IMAGE_KEYS
  do
    running_service=$(service_for_image_key "$running_key")
    if [ "$running_service" = iiot-migration ]; then
      continue
    fi
    eval "running_expected_digest=\${${running_key}_DIGEST}"
    running_container_id=$(compose ps -q "$running_service" 2>/dev/null | head -n 1 || true)
    [ -n "$running_container_id" ] || return 1
    running_image_id=$(docker inspect --format '{{.Image}}' "$running_container_id" 2>/dev/null || true)
    [ -n "$running_image_id" ] || return 1
    running_repo_digests=$(docker image inspect --format '{{json .RepoDigests}}' "$running_image_id" 2>/dev/null || true)
    case "$running_repo_digests" in
      *"@$running_expected_digest"*) ;;
      *)
        printf 'Cloud runtime image drift detected: service=%s expected=%s repo_digests=%s\n' \
          "$running_service" "$running_expected_digest" "${running_repo_digests:-missing}" >&2
        return 1
        ;;
    esac
  done
  return 0
}

image_ref_is_placeholder() {
  image_ref=${1:-}
  case "$image_ref" in
    ""|*:sha-0000000000000000)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

hydrate_unselected_images_from_running_containers() {
  for key in $APP_IMAGE_KEYS
  do
    if image_key_is_selected "$key"; then
      continue
    fi

    eval "image_ref=\${$key:-}"
    if ! image_ref_is_placeholder "$image_ref"; then
      continue
    fi

    service=$(service_for_image_key "$key")
    container_id=$(compose ps -q "$service" 2>/dev/null | head -n 1 || true)
    if [ -z "$container_id" ]; then
      if [ "$key" = "IIOT_MIGRATION_IMAGE" ]; then
        dotenv_image_ref=$(read_manifest_value "$DEPLOY_DIR/.env" "$key")
        if ! image_ref_is_placeholder "$dotenv_image_ref"; then
          eval "$key=\$dotenv_image_ref"
          continue
        fi

        printf 'warning: keeping placeholder image for unselected one-shot migration service: %s\n' "$key" >&2
        continue
      fi

      printf 'Current release image is not usable and no running container was found: %s=%s\n' "$key" "$image_ref" >&2
      exit 66
    fi

    running_image=$(docker inspect --format '{{.Config.Image}}' "$container_id" 2>/dev/null || true)
    if [ -z "$running_image" ]; then
      printf 'Could not inspect running image for unselected service: %s\n' "$service" >&2
      exit 66
    fi

    eval "$key=\$running_image"
  done
}

ensure_nginx_gateway_if_needed() {
  case " $RUNTIME_SELECTED_SERVICES " in
    *" iiot-web "*|*" iiot-gateway "*)
      nginx_gateway_container=$(compose ps -q nginx-gateway 2>/dev/null || true)
      if [ -n "$nginx_gateway_container" ]; then
        printf 'Restarting nginx-gateway to refresh upstream DNS...\n'
        compose restart nginx-gateway >/dev/null
      else
        printf 'Starting nginx-gateway because the selected release affects browser traffic...\n'
        compose up -d nginx-gateway >/dev/null
      fi
      ;;
  esac
}

requires_edge_installer_catalog_verification() {
  case " $RUNTIME_SELECTED_SERVICES " in
    *" iiot-httpapi "*|*" iiot-gateway "*|*" iiot-web "*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

SELECTED_SERVICES=$(normalize_services "$REQUESTED_SERVICES")
MANIFEST_SERVICES=$(read_manifest_value "$DEPLOY_IMAGE_MANIFEST_PATH" CLOUD_DEPLOY_SERVICES)
NORMALIZED_MANIFEST_SERVICES=$(normalize_services "$MANIFEST_SERVICES")
[ "$NORMALIZED_MANIFEST_SERVICES" = "$SELECTED_SERVICES" ] \
  || fail_contract "requested services do not match the run-bound image manifest: requested=$SELECTED_SERVICES manifest=$NORMALIZED_MANIFEST_SERVICES"
SELECTED_IMAGE_KEYS=""
RUNTIME_SELECTED_SERVICES=""
RUN_MIGRATION=false
for service in $SELECTED_SERVICES
do
  image_key=$(image_key_for_service "$service")
  SELECTED_IMAGE_KEYS="$SELECTED_IMAGE_KEYS $image_key"
  if [ "$service" = "iiot-migration" ]; then
    RUN_MIGRATION=true
  else
    RUNTIME_SELECTED_SERVICES="$RUNTIME_SELECTED_SERVICES $service"
  fi
done
SELECTED_IMAGE_KEYS=$(printf '%s' "$SELECTED_IMAGE_KEYS" | awk '{$1=$1; print}')
RUNTIME_SELECTED_SERVICES=$(printf '%s' "$RUNTIME_SELECTED_SERVICES" | awk '{$1=$1; print}')

cleanup_temp_release_env() {
  if [ -n "${TEMP_RELEASE_ENV_FILE:-}" ] && [ -f "$TEMP_RELEASE_ENV_FILE" ]; then
    rm -f "$TEMP_RELEASE_ENV_FILE"
  fi
}

DEPLOY_RELEASE_LOCK_FILE=$(resolve_managed_lock_file \
  "${DEPLOY_RELEASE_LOCK_FILE:-$CLOUD_RELEASE_LOCK_FILE_DEFAULT}" \
  "$DEPLOY_DIR/.cloud-release.lock")
DEPLOY_RELEASE_LOCK_ACQUIRED=0
RUNTIME_PROMOTED=0
ROLLOUT_STARTED=0
history_file=

trap - EXIT HUP INT TERM

cleanup_deploy_process() {
  deploy_exit_status=$?
  trap - EXIT HUP INT TERM
  cleanup_temp_release_env
  if [ "$RUNTIME_PROMOTED" -eq 0 ] && [ -d "${DEPLOY_SUPPORT_BACKUP_DIR:-/missing}" ]; then
    if restore_support_backup; then
      if [ "$ROLLOUT_STARTED" -eq 1 ]; then
        write_deploy_blocked_evidence blocked-partial-rollout-support-restored "$deploy_exit_status" restored
      else
        rm -rf "$DEPLOY_SUPPORT_BACKUP_DIR"
      fi
    else
      write_deploy_blocked_evidence blocked-support-restore "$deploy_exit_status" failed
      if lock_is_owned_by_workspace_invocation; then
        printf '%s\n' blocked-support-restore | atomic_write_file "${DEPLOY_RELEASE_LOCK_FILE}.d/phase" 600
      fi
      printf 'Cloud support restore failed; lock and durable backup are preserved for operator recovery.\n' >&2
      exit 86
    fi
  elif [ "$RUNTIME_PROMOTED" -eq 1 ]; then
    if [ "$deploy_exit_status" -ne 0 ] && [ -f "$CURRENT_RELEASE_FILE" ]; then
      replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_CLEANUP_STATUS partial
      replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_PHASE runtime-healthy-cleanup-partial
      {
        printf '\nCleanup interrupted or failed after runtime promotion: invocation=%s exit=%s\n' "$WORKSPACE_INVOCATION_ID" "$deploy_exit_status"
      } | atomic_append_file "$CURRENT_RELEASE_SUMMARY_FILE" 600
      if [ -n "$history_file" ] && [ -f "$history_file" ]; then
        atomic_copy_file "$CURRENT_RELEASE_FILE" "$history_file" 600
        atomic_copy_file "$CURRENT_RELEASE_SUMMARY_FILE" "${history_file%.env}.summary.md" 600
      fi
    fi
    rm -rf "${DEPLOY_SUPPORT_BACKUP_DIR:-/missing-never}"
  fi
  if [ -n "${DEPLOY_SUPPORT_STAGING_DIR:-}" ]; then
    rm -rf "$DEPLOY_SUPPORT_STAGING_DIR"
  fi
  if [ "$DEPLOY_CONFIG_LOCK_ACQUIRED" -eq 1 ] \
    && [ "$(managed_lock_read_field "${DEPLOY_CONFIG_LOCK_FILE}.d" pid)" = "$$" ]; then
    release_managed_lock "$DEPLOY_CONFIG_LOCK_FILE" || true
    DEPLOY_CONFIG_LOCK_ACQUIRED=0
  fi
  if [ "$DEPLOY_RELEASE_LOCK_ACQUIRED" -eq 1 ] \
    && [ "${DEPLOY_RELEASE_LOCK_PARENT_OWNED:-0}" != 1 ] \
    && lock_is_owned_by_workspace_invocation; then
    release_managed_lock "$DEPLOY_RELEASE_LOCK_FILE" || true
    DEPLOY_RELEASE_LOCK_ACQUIRED=0
  fi
  exit "$deploy_exit_status"
}

handle_deploy_signal() {
  signal_name=$1
  signal_exit_code=$2
  printf 'Cloud release interrupted by signal %s; releasing the deployment lock and exiting.\n' "$signal_name" >&2
  exit "$signal_exit_code"
}

trap cleanup_deploy_process EXIT
trap 'handle_deploy_signal HUP 129' HUP
trap 'handle_deploy_signal INT 130' INT
trap 'handle_deploy_signal TERM 143' TERM

if [ "${DEPLOY_RELEASE_LOCK_PREACQUIRED:-0}" = 1 ]; then
  DEPLOY_RELEASE_LOCK_ACQUIRED=1
  lock_owner=$(managed_lock_read_field "${DEPLOY_RELEASE_LOCK_FILE}.d" pid)
  lock_expected_owner=${DEPLOY_RELEASE_LOCK_OWNER_PID:-$$}
  lock_release=$(managed_lock_read_field "${DEPLOY_RELEASE_LOCK_FILE}.d" release)
  lock_invocation=$(managed_lock_read_field "${DEPLOY_RELEASE_LOCK_FILE}.d" invocation-id)
  lock_plan_digest=$(managed_lock_read_field "${DEPLOY_RELEASE_LOCK_FILE}.d" plan-digest)
  case "$lock_expected_owner" in
    ''|*[!0-9]*) fail_contract "pre-acquired release lock delegated owner PID is invalid" ;;
  esac
  [ "$lock_owner" = "$lock_expected_owner" ] \
    || fail_contract "pre-acquired release lock is not owned by the remote transaction process"
  [ "$lock_release" = "$RELEASE_TAG" ] \
    || fail_contract "pre-acquired release lock belongs to a different release: $lock_release"
  [ "$lock_invocation" = "$WORKSPACE_INVOCATION_ID" ] \
    || fail_contract "pre-acquired release lock belongs to a different invocation: $lock_invocation"
  [ "$lock_plan_digest" = "$WORKSPACE_PLAN_DIGEST" ] \
    || fail_contract "pre-acquired release lock belongs to a different plan"
  DEPLOY_RELEASE_LOCK_ACQUIRED=1
  printf 'Cloud release continues under the support-install transaction lock: invocation=%s\n' "$WORKSPACE_INVOCATION_ID"
else
  fail_contract "remote release requires the transaction lock acquired by the workspace support staging step"
fi
export DEPLOY_RELEASE_LOCK_FILE
DEPLOY_RELEASE_LOCK_OWNER_PID=${DEPLOY_RELEASE_LOCK_OWNER_PID:-$$}
export DEPLOY_RELEASE_LOCK_OWNER_PID

"$SCRIPT_DIR/pre-deploy-check.sh" "$RELEASE_TAG"

if [ -n "$REQUESTED_SERVICES" ]; then
  if [ ! -f "$CURRENT_RELEASE_FILE" ]; then
    printf 'Incremental deploy requires an existing current release: %s\n' "$CURRENT_RELEASE_FILE" >&2
    exit 64
  fi

  load_release_images_from_manifest "$CURRENT_RELEASE_FILE"
  hydrate_unselected_images_from_running_containers
fi

load_selected_images_from_invocation_manifest
ensure_target_images_not_latest

current_release_matches_plan() {
  [ -f "$CURRENT_RELEASE_FILE" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_RELEASE_ID)" = "$RELEASE_TAG" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_GIT_SHA)" = "$WORKSPACE_EXPECTED_SHA" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_PLAN_DIGEST)" = "$WORKSPACE_PLAN_DIGEST" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_SUPPORT_MANIFEST_SHA256)" = "${EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256:-}" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_IMAGE_CONTENT_SHA256)" = "$DEPLOY_IMAGE_CONTENT_DIGEST" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_CONFIG_SHA256)" = "$DEPLOY_CONFIG_DIGEST" ] || return 1
  [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_SERVICES)" = "$SELECTED_SERVICES" ] || return 1

  for current_key in $SELECTED_IMAGE_KEYS
  do
    eval "desired_image=\${$current_key}"
    [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" "$current_key")" = "$desired_image" ] || return 1
  done
  release_image_env_matches_manifest "$RELEASE_IMAGE_ENV_FILE" "$CURRENT_RELEASE_FILE" || return 1
  running_selected_images_match_digests || return 1
  return 0
}

sync_current_release_history_after_cleanup() {
  current_invocation=$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INVOCATION_ID)
  for candidate_history in "$RELEASE_HISTORY_DIR"/*.env
  do
    [ -f "$candidate_history" ] || continue
    if [ "$(read_manifest_value "$candidate_history" DEPLOY_INVOCATION_ID)" = "$current_invocation" ]; then
      atomic_copy_file "$CURRENT_RELEASE_FILE" "$candidate_history" 600
      if [ -f "$CURRENT_RELEASE_SUMMARY_FILE" ]; then
        atomic_copy_file "$CURRENT_RELEASE_SUMMARY_FILE" "${candidate_history%.env}.summary.md" 600
      fi
    fi
  done
}

if current_release_matches_plan; then
  update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" no-op-health-check
  COMPOSE_ENV_FILE= "$SCRIPT_DIR/post-deploy-check.sh"
  RUNTIME_PROMOTED=1
  current_cleanup_status=$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_CLEANUP_STATUS)
  if [ "$current_cleanup_status" != complete ]; then
    update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" cleanup-recovery
    printf 'Current runtime already matches the approved SHA and plan; recovering cleanup only.\n'
    if COMPOSE_ENV_FILE= "$SCRIPT_DIR/post-release-cleanup.sh" --release-tag "$RELEASE_TAG"; then
      replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_CLEANUP_STATUS complete
      replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_PHASE complete
      {
        printf '\nCleanup-only recovery completed: invocation=%s recovered_by=%s\n' \
          "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INVOCATION_ID)" "$WORKSPACE_INVOCATION_ID"
      } | atomic_append_file "$CURRENT_RELEASE_SUMMARY_FILE" 600
      sync_current_release_history_after_cleanup
    else
      printf 'Cloud runtime remains healthy, but cleanup-only recovery failed. Do not rerun rollout.\n' >&2
      exit 1
    fi
  else
    printf 'Cloud release is already healthy for the same SHA and plan; rollout is a no-op.\n'
  fi
  no_op_summary="$RELEASE_HISTORY_DIR/$(date -u +%Y%m%dT%H%M%SZ)-noop-$(safe_release_file_name "$WORKSPACE_INVOCATION_ID").summary.md"
  {
    printf '### Cloud deploy no-op\n\n'
    printf -- '- Release tag: `%s`\n' "$RELEASE_TAG"
    printf -- '- Git SHA: `%s`\n' "$WORKSPACE_EXPECTED_SHA"
    printf -- '- Invocation: `%s`\n' "$WORKSPACE_INVOCATION_ID"
    printf -- '- Plan digest: `%s`\n' "$WORKSPACE_PLAN_DIGEST"
    printf -- '- Result: `healthy-no-op`\n'
  } | atomic_write_file "$no_op_summary" 600
  replace_env_value "$DEPLOY_TRANSACTION_MARKER_PATH" DEPLOY_TRANSACTION_PHASE healthy-no-op
  rm -rf "$DEPLOY_SUPPORT_BACKUP_DIR" "${DEPLOY_SUPPORT_STAGING_DIR:-/missing-never}"
  release_managed_lock "$DEPLOY_CONFIG_LOCK_FILE"
  DEPLOY_CONFIG_LOCK_ACQUIRED=0
  if [ "$DEPLOY_RELEASE_LOCK_ACQUIRED" -eq 1 ] \
    && [ "${DEPLOY_RELEASE_LOCK_PARENT_OWNED:-0}" != 1 ] \
    && lock_is_owned_by_workspace_invocation; then
    release_managed_lock "$DEPLOY_RELEASE_LOCK_FILE"
    DEPLOY_RELEASE_LOCK_ACQUIRED=0
  fi
  trap - EXIT HUP INT TERM
  printf 'Cloud no-op history summary: %s\n' "$no_op_summary"
  exit 0
fi

update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" backup
"$SCRIPT_DIR/postgres-backup.sh"

PRE_DEPLOY_BACKUP_FILE=$(read_state_path "$BACKUP_STATE_FILE" || true)
if [ -z "$PRE_DEPLOY_BACKUP_FILE" ]; then
  printf 'Could not resolve the latest successful backup marker: %s\n' "$BACKUP_STATE_FILE" >&2
  exit 1
fi

DEPLOY_RELEASE_ID="$RELEASE_TAG"
DEPLOY_GIT_SHA_VALUE=${DEPLOY_GIT_SHA:-unknown}
DEPLOY_TRIGGERED_BY_VALUE=${DEPLOY_TRIGGERED_BY:-manual}
DEPLOYED_AT_UTC_VALUE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
DEPLOY_RELEASE_NOTES_VALUE=${DEPLOY_RELEASE_NOTES:-}

write_release_manifest \
  "$STAGED_RELEASE_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$PRE_DEPLOY_BACKUP_FILE" \
  "$WORKSPACE_INVOCATION_ID" \
  "$WORKSPACE_EXPECTED_SHA" \
  "$WORKSPACE_PLAN_DIGEST" \
  "${EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256:-}" \
  "$DEPLOY_IMAGE_MANIFEST_DIGEST" \
  "$SELECTED_SERVICES" \
  "$DEPLOY_IMAGE_CONTENT_DIGEST" \
  "$DEPLOY_CONFIG_DIGEST"
write_release_image_env "$STAGED_RELEASE_IMAGE_ENV_FILE"

TEMP_RELEASE_ENV_FILE=$(mktemp "$DEPLOY_DIR/.release-env.XXXXXX")
cp "$DEPLOY_DIR/.env" "$TEMP_RELEASE_ENV_FILE"
[ "$(sha256_file "$DEPLOY_DIR/.env")" = "$DEPLOY_ORIGINAL_ENV_SHA256" ] || {
  printf 'Cloud .env changed while the release snapshot was created; aborting before rollout.\n' >&2
  exit 75
}
apply_app_images_to_dotenv "$TEMP_RELEASE_ENV_FILE"
temp_config_digest=$(canonical_cloud_config_sha256 "$TEMP_RELEASE_ENV_FILE")
[ "$temp_config_digest" = "$DEPLOY_CONFIG_DIGEST" ] || {
  printf 'Cloud configuration changed while the release environment snapshot was created; aborting before rollout.\n' >&2
  exit 75
}
export COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE"
load_dotenv

compose pull $SELECTED_SERVICES
verify_selected_image_digests
update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" rollout
ROLLOUT_STARTED=1
compose up -d postgres redis-cache rabbitmq seq >/dev/null
if [ "$RUN_MIGRATION" = "true" ]; then
  compose run -T --rm iiot-migration
fi
if [ -n "$RUNTIME_SELECTED_SERVICES" ]; then
  if [ -n "$REQUESTED_SERVICES" ]; then
    compose up -d --no-deps $RUNTIME_SELECTED_SERVICES >/dev/null
  else
    compose up -d $RUNTIME_SELECTED_SERVICES >/dev/null
  fi
fi
ensure_nginx_gateway_if_needed
update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" post-deploy-health
post_deploy_verify_edge_installer_catalog=${POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG:-0}
post_deploy_require_edge_installer_catalog=${POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG:-0}
if requires_edge_installer_catalog_verification; then
  printf 'Edge installer catalog verification is required for selected Cloud download services: %s\n' "$RUNTIME_SELECTED_SERVICES"
  post_deploy_verify_edge_installer_catalog=1
  post_deploy_require_edge_installer_catalog=1
fi
COMPOSE_ENV_FILE="$TEMP_RELEASE_ENV_FILE" \
  POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG="$post_deploy_verify_edge_installer_catalog" \
  POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG="$post_deploy_require_edge_installer_catalog" \
  "$SCRIPT_DIR/post-deploy-check.sh"

current_config_digest=$(canonical_cloud_config_sha256 "$DEPLOY_DIR/.env")
[ "$current_config_digest" = "$DEPLOY_CONFIG_DIGEST" ] || {
  printf 'Cloud configuration changed concurrently during rollout; refusing runtime promotion.\n' >&2
  exit 75
}
[ "$(sha256_file "$DEPLOY_DIR/.env")" = "$DEPLOY_ORIGINAL_ENV_SHA256" ] || {
  printf 'Cloud .env changed concurrently during rollout; refusing atomic promotion.\n' >&2
  exit 75
}
if [ -n "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ]; then
  if [ "${DEPLOY_CONFIG_LOCK_TEST_MODE:-}" != enabled ] \
    || [ ! -f "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ] \
    || [ -L "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ] \
    || [ ! -x "$DEPLOY_CONFIG_LOCK_TEST_HOOK" ]; then
    printf 'Cloud config-lock test hook was rejected.\n' >&2
    exit 64
  fi
  "$DEPLOY_CONFIG_LOCK_TEST_HOOK" "$DEPLOY_DIR" "$DEPLOY_ORIGINAL_ENV_SHA256"
fi
[ "$(sha256_file "$DEPLOY_DIR/.env")" = "$DEPLOY_ORIGINAL_ENV_SHA256" ] || {
  printf 'Unsupported direct .env write detected after final hash; operator content was not overwritten.\n' >&2
  exit 75
}
atomic_copy_file "$STAGED_RELEASE_IMAGE_ENV_FILE" "$RELEASE_IMAGE_ENV_FILE" 600

if [ -f "$CURRENT_RELEASE_FILE" ]; then
  atomic_copy_file "$CURRENT_RELEASE_FILE" "$PREVIOUS_RELEASE_FILE" 600
fi

atomic_copy_file "$STAGED_RELEASE_FILE" "$CURRENT_RELEASE_FILE" 600
replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_PHASE runtime-healthy-cleanup-pending
history_file=$(record_release_history "$CURRENT_RELEASE_FILE" "$DEPLOY_RELEASE_ID")
replace_env_value "$DEPLOY_TRANSACTION_MARKER_PATH" DEPLOY_TRANSACTION_PHASE runtime-promoted
RUNTIME_PROMOTED=1
rm -rf "$DEPLOY_SUPPORT_BACKUP_DIR"
write_release_summary \
  "$CURRENT_RELEASE_SUMMARY_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$SELECTED_SERVICES" \
  "$DEPLOY_RELEASE_NOTES_VALUE" \
  "$WORKSPACE_INVOCATION_ID" \
  "$WORKSPACE_PLAN_DIGEST" \
  "${EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256:-}" \
  "$DEPLOY_IMAGE_MANIFEST_DIGEST"

cleanup_log=$(mktemp "$DEPLOY_DIR/post-release-cleanup.XXXXXX")
cleanup_status_file=$(mktemp "$DEPLOY_DIR/post-release-cleanup-status.XXXXXX")
update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" cleanup
printf 'Post-release cleanup started; output is streamed live.\n'
(
  set +e
  COMPOSE_ENV_FILE= \
    "$SCRIPT_DIR/post-release-cleanup.sh" --release-tag "$DEPLOY_RELEASE_ID" 2>&1
  printf '%s\n' "$?" > "$cleanup_status_file"
  exit 0
) | tee "$cleanup_log"
cleanup_status=$(sed -n '1p' "$cleanup_status_file")
case "$cleanup_status" in
  ''|*[!0-9]*)
    cleanup_status=1
    ;;
esac
{
  printf '\n'
  cat "$cleanup_log"
} | atomic_append_file "$CURRENT_RELEASE_SUMMARY_FILE" 600
rm -f "$cleanup_log" "$cleanup_status_file"

history_summary_file=${history_file%.env}.summary.md
if [ "$cleanup_status" -eq 0 ]; then
  replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_CLEANUP_STATUS complete
  replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_PHASE complete
else
  replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_CLEANUP_STATUS partial
  replace_env_value "$CURRENT_RELEASE_FILE" DEPLOY_PHASE runtime-healthy-cleanup-partial
fi
atomic_copy_file "$CURRENT_RELEASE_FILE" "$history_file" 600
atomic_copy_file "$CURRENT_RELEASE_SUMMARY_FILE" "$history_summary_file" 600
cleanup_temp_release_env
unset COMPOSE_ENV_FILE
update_managed_lock_phase "$DEPLOY_RELEASE_LOCK_FILE" finalize

if [ "$cleanup_status" -ne 0 ]; then
  printf 'Release runtime rollout is healthy, but post-release cleanup failed: %s\n' "$DEPLOY_RELEASE_ID" >&2
  printf 'Do not rerun the full deployment blindly; inspect current release, containers and cleanup lock first.\n' >&2
  printf 'Current release summary: %s\n' "$CURRENT_RELEASE_SUMMARY_FILE" >&2
  exit "$cleanup_status"
fi

release_managed_lock "$DEPLOY_CONFIG_LOCK_FILE"
DEPLOY_CONFIG_LOCK_ACQUIRED=0
if [ "${DEPLOY_RELEASE_LOCK_PARENT_OWNED:-0}" != 1 ]; then
  release_managed_lock "$DEPLOY_RELEASE_LOCK_FILE"
fi
DEPLOY_RELEASE_LOCK_ACQUIRED=0
if [ -n "${DEPLOY_SUPPORT_STAGING_DIR:-}" ]; then
  rm -rf "$DEPLOY_SUPPORT_STAGING_DIR"
fi
trap - EXIT HUP INT TERM

printf 'Release deployed successfully: %s\n' "$DEPLOY_RELEASE_ID"
printf 'Current release manifest: %s\n' "$CURRENT_RELEASE_FILE"
printf 'Current release summary: %s\n' "$CURRENT_RELEASE_SUMMARY_FILE"
printf 'Release history record: %s\n' "$history_file"
printf 'Release history summary: %s\n' "$history_summary_file"
