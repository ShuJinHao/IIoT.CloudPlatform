#!/usr/bin/env bash
set -euo pipefail

TRUSTED_PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
TRUSTED_HOME=${HOME:-/var/empty}
case "$TRUSTED_HOME" in
  /*) ;;
  *) TRUSTED_HOME=/var/empty ;;
esac
PATH=$TRUSTED_PATH
export PATH
unset BASH_ENV ENV CDPATH IFS GLOBIGNORE PS4

DEPLOY_DIR=${1:?deploy directory is required}
STAGING_DIR=${2:?support staging directory is required}
INVOCATION_ID=${3:?workspace invocation id is required}
RELEASE_TAG=${4:?release tag is required}
EXPECTED_SHA=${5:?expected SHA is required}
PLAN_DIGEST=${6:?plan digest is required}
SUPPORT_MANIFEST_DIGEST=${7:?support manifest digest is required}
IMAGE_MANIFEST_DIGEST=${8:?image manifest digest is required}
IMAGE_CONTENT_DIGEST=${9:?image content digest is required}
DEPLOY_SERVICES=${10:?deploy services are required}

INSTALLER="$STAGING_DIR/.install-cloud-support.sh"
IMAGE_MANIFEST="$STAGING_DIR/.cloud-image-manifest.env"
SUPPORT_BACKUP_DIR="$DEPLOY_DIR/releases/support-recovery/$INVOCATION_ID"
DEPLOY_RELEASE_SCRIPT="$DEPLOY_DIR/scripts/deploy-release.sh"
HANDOFF_MARKER="$STAGING_DIR/.deploy-release-started"
TRANSACTION_DIR="$DEPLOY_DIR/releases/transactions"
TRANSACTION_MARKER="$TRANSACTION_DIR/$INVOCATION_ID.env"
RELEASE_LOCK_FILE=""
RELEASE_LOCK_ACQUIRED=0
SUPPORT_TRANSACTION_STARTED=0
SUPPORT_INSTALLED=0
FAILURE_PHASE=""
CHILD_PID=""
CHILD_ROLE=""
PARENT_SIGNAL_STATUS=0
PROMOTED_RECOVERY_CLEANUP_ONLY=0
PROCESS_GROUP_GRACE_ATTEMPTS=30
PROCESS_GROUP_QUIESCED=1

fail_transaction() {
  printf 'Cloud workspace release transaction rejected: %s\n' "$*" >&2
  exit 64
}

case "$INVOCATION_ID" in
  ''|*[!A-Za-z0-9._:-]*) fail_transaction "unsafe invocation id" ;;
esac
case "$RELEASE_TAG" in
  sha-[0-9a-f]*) ;;
  *) fail_transaction "unsafe release tag: $RELEASE_TAG" ;;
esac
case "$EXPECTED_SHA" in *[!0-9a-f]*|'') fail_transaction "unsafe expected SHA" ;; esac
[ "${#EXPECTED_SHA}" -eq 40 ] || fail_transaction "expected SHA must contain 40 hex characters"
for digest_value in "$PLAN_DIGEST" "$SUPPORT_MANIFEST_DIGEST" "$IMAGE_MANIFEST_DIGEST" "$IMAGE_CONTENT_DIGEST"; do
  case "$digest_value" in *[!0-9a-f]*|'') fail_transaction "unsafe transaction digest" ;; esac
  [ "${#digest_value}" -eq 64 ] || fail_transaction "transaction digest must contain 64 hex characters"
done
[ "$RELEASE_TAG" = "sha-$EXPECTED_SHA" ] || fail_transaction "release tag and expected SHA differ"
[ "$(readlink -f "$DEPLOY_DIR")" = "$DEPLOY_DIR" ] || fail_transaction "deploy directory is not canonical"
case "$(readlink -f "$STAGING_DIR")" in
  "$DEPLOY_DIR/.support-staging/"*) ;;
  *) fail_transaction "support staging directory escaped the deploy staging root" ;;
esac
[ -f "$STAGING_DIR/scripts/release-common.sh" ] && [ ! -L "$STAGING_DIR/scripts/release-common.sh" ] \
  || fail_transaction "staged release-common.sh is missing or unsafe"
[ -f "$INSTALLER" ] && [ ! -L "$INSTALLER" ] || fail_transaction "support installer is missing or unsafe"
[ -f "$IMAGE_MANIFEST" ] && [ ! -L "$IMAGE_MANIFEST" ] || fail_transaction "image manifest is missing or unsafe"
readonly TRUSTED_PATH TRUSTED_HOME DEPLOY_DIR STAGING_DIR INVOCATION_ID RELEASE_TAG EXPECTED_SHA \
  PLAN_DIGEST SUPPORT_MANIFEST_DIGEST IMAGE_MANIFEST_DIGEST IMAGE_CONTENT_DIGEST DEPLOY_SERVICES \
  INSTALLER IMAGE_MANIFEST SUPPORT_BACKUP_DIR DEPLOY_RELEASE_SCRIPT HANDOFF_MARKER \
  TRANSACTION_DIR TRANSACTION_MARKER
readonly PROCESS_GROUP_GRACE_ATTEMPTS

# shellcheck source=release-common.sh
. "$STAGING_DIR/scripts/release-common.sh"
load_dotenv "$DEPLOY_DIR/.env"

process_group_alive() {
  local group_pid=$1
  [ -n "$group_pid" ] && kill -0 -- "-$group_pid" 2>/dev/null
}

start_isolated_child() {
  CHILD_ROLE=$1
  shift
  if command -v setsid >/dev/null 2>&1; then
    setsid "$@" &
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c \
      'import os, sys; os.setsid(); os.execvpe(sys.argv[1], sys.argv[1:], os.environ)' \
      "$@" &
  else
    printf 'Cloud transaction requires setsid or python3 to isolate child process groups.\n' >&2
    return 127
  fi
  CHILD_PID=$!
}

terminate_child_group() {
  local group_pid=$1
  local group_role=$2
  local attempt=0
  [ -n "$group_pid" ] || return 0
  if process_group_alive "$group_pid"; then
    printf 'Cloud transaction stopping %s process group pgid=%s with TERM.\n' \
      "$group_role" "$group_pid" >&2
    kill -TERM -- "-$group_pid" 2>/dev/null || true
    while process_group_alive "$group_pid" && [ "$attempt" -lt "$PROCESS_GROUP_GRACE_ATTEMPTS" ]; do
      sleep 0.1
      attempt=$((attempt + 1))
    done
  fi
  if process_group_alive "$group_pid"; then
    printf 'Cloud transaction escalating %s process group pgid=%s to KILL.\n' \
      "$group_role" "$group_pid" >&2
    kill -KILL -- "-$group_pid" 2>/dev/null || true
    attempt=0
    while process_group_alive "$group_pid" && [ "$attempt" -lt "$PROCESS_GROUP_GRACE_ATTEMPTS" ]; do
      sleep 0.1
      attempt=$((attempt + 1))
    done
  fi
  wait "$group_pid" 2>/dev/null || true
  if process_group_alive "$group_pid"; then
    printf 'Cloud transaction cannot prove %s process group termination: pgid=%s\n' \
      "$group_role" "$group_pid" >&2
    return 86
  fi
}

wait_isolated_child() {
  local child_pid=$CHILD_PID
  local child_role=$CHILD_ROLE
  local child_status
  if wait "$child_pid"; then
    child_status=0
  else
    child_status=$?
  fi
  if process_group_alive "$child_pid"; then
    if ! terminate_child_group "$child_pid" "$child_role"; then
      PROCESS_GROUP_QUIESCED=0
      CHILD_PID=""
      CHILD_ROLE=""
      return 86
    fi
  fi
  CHILD_PID=""
  CHILD_ROLE=""
  return "$child_status"
}

lock_owned_by_transaction() {
  [ -n "$RELEASE_LOCK_FILE" ] \
    && [ -r "${RELEASE_LOCK_FILE}.d/pid" ] \
    && [ "$(managed_lock_read_field "${RELEASE_LOCK_FILE}.d" pid)" = "$$" ] \
    && [ "$(managed_lock_read_field "${RELEASE_LOCK_FILE}.d" invocation-id)" = "$INVOCATION_ID" ] \
    && [ "$(managed_lock_read_field "${RELEASE_LOCK_FILE}.d" plan-digest)" = "$PLAN_DIGEST" ]
}

blocked_evidence_matches_invocation() {
  local blocked_file="$DEPLOY_DIR/releases/deploy-blocked.env"
  [ -f "$blocked_file" ] \
    && grep -qx "DEPLOY_INVOCATION_ID=$INVOCATION_ID" "$blocked_file"
}

write_blocked_evidence_for() {
  local blocked_invocation="$1"
  local blocked_expected_sha="$2"
  local blocked_plan_digest="$3"
  local blocked_phase="$4"
  local blocked_exit="$5"
  local restore_status="$6"
  local blocked_marker_path="$7"
  local blocked_backup_path="$8"
  local blocked_staging_path="$9"
  local blocked_lock_path="${10}"
  local releases_dir="$DEPLOY_DIR/releases"
  local history_dir="$releases_dir/history"
  local blocked_file="$releases_dir/deploy-blocked.env"
  local history_file
  case "$blocked_invocation" in
    ''|*[!A-Za-z0-9._:-]*) blocked_invocation=unknown-orphan ;;
  esac
  case "$blocked_expected_sha" in
    ''|*[!0-9a-f]*) blocked_expected_sha=unknown ;;
  esac
  case "$blocked_plan_digest" in
    ''|*[!0-9a-f]*) blocked_plan_digest=unknown ;;
  esac
  for evidence_path in "$blocked_marker_path" "$blocked_backup_path" "$blocked_staging_path" "$blocked_lock_path"; do
    case "$evidence_path" in
      *[!A-Za-z0-9._:/-]*)
        printf 'Cloud durable recovery evidence contains an unsafe path and cannot be recorded.\n' >&2
        return 86
        ;;
    esac
  done
  mkdir -p "$history_dir"
  history_file="$history_dir/$(date -u +%Y%m%dT%H%M%SZ)-blocked-$blocked_invocation.env"
  {
    printf 'DEPLOY_INVOCATION_ID=%s\n' "$blocked_invocation"
    printf 'DEPLOY_EXPECTED_SHA=%s\n' "$blocked_expected_sha"
    printf 'DEPLOY_PLAN_DIGEST=%s\n' "$blocked_plan_digest"
    printf 'DEPLOY_PHASE=%s\n' "$blocked_phase"
    printf 'DEPLOY_EXIT_CODE=%s\n' "$blocked_exit"
    printf 'DEPLOY_SUPPORT_RESTORE_STATUS=%s\n' "$restore_status"
    printf 'DEPLOY_ORPHAN_MARKER_PATH=%s\n' "$blocked_marker_path"
    printf 'DEPLOY_SUPPORT_BACKUP_DIR=%s\n' "$blocked_backup_path"
    printf 'DEPLOY_SUPPORT_STAGING_DIR=%s\n' "$blocked_staging_path"
    printf 'DEPLOY_RELEASE_LOCK_FILE=%s\n' "$blocked_lock_path"
    printf 'RECORDED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } | atomic_write_file "$blocked_file" 600
  atomic_copy_file "$blocked_file" "$history_file" 600
  printf 'Cloud durable blocked evidence: %s\n' "$blocked_file" >&2
}

write_blocked_evidence() {
  write_blocked_evidence_for \
    "$INVOCATION_ID" \
    "$EXPECTED_SHA" \
    "$PLAN_DIGEST" \
    "$1" \
    "$2" \
    "$3" \
    "$TRANSACTION_MARKER" \
    "$SUPPORT_BACKUP_DIR" \
    "$STAGING_DIR" \
    "$RELEASE_LOCK_FILE"
}

write_transaction_marker() {
  local marker_phase="$1"
  local marker_child_status="${2:-}"
  mkdir -p "$TRANSACTION_DIR"
  {
    printf 'DEPLOY_INVOCATION_ID=%s\n' "$INVOCATION_ID"
    printf 'DEPLOY_EXPECTED_SHA=%s\n' "$EXPECTED_SHA"
    printf 'DEPLOY_PLAN_DIGEST=%s\n' "$PLAN_DIGEST"
    printf 'DEPLOY_RELEASE_TAG=%s\n' "$RELEASE_TAG"
    printf 'DEPLOY_SUPPORT_MANIFEST_SHA256=%s\n' "$SUPPORT_MANIFEST_DIGEST"
    printf 'DEPLOY_IMAGE_MANIFEST_SHA256=%s\n' "$IMAGE_MANIFEST_DIGEST"
    printf 'DEPLOY_IMAGE_CONTENT_SHA256=%s\n' "$IMAGE_CONTENT_DIGEST"
    printf 'DEPLOY_SERVICES=%s\n' "$DEPLOY_SERVICES"
    printf 'DEPLOY_TRANSACTION_PHASE=%s\n' "$marker_phase"
    printf 'DEPLOY_TRANSACTION_PARENT_PID=%s\n' "$$"
    printf 'DEPLOY_TRANSACTION_CHILD_PID=%s\n' "${CHILD_PID:-}"
    printf 'DEPLOY_TRANSACTION_CHILD_STATUS=%s\n' "$marker_child_status"
    printf 'DEPLOY_SUPPORT_STAGING_DIR=%s\n' "$STAGING_DIR"
    printf 'DEPLOY_SUPPORT_BACKUP_DIR=%s\n' "$SUPPORT_BACKUP_DIR"
    printf 'DEPLOY_RELEASE_LOCK_FILE=%s\n' "$RELEASE_LOCK_FILE"
    printf 'RECORDED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } | atomic_write_file "$TRANSACTION_MARKER" 600
}

transaction_marker_promotion_proven() {
  local marker_path=$1
  local marker_phase marker_invocation marker_expected marker_plan
  [ -f "$marker_path" ] && [ ! -L "$marker_path" ] || return 1
  marker_phase=$(read_manifest_value "$marker_path" DEPLOY_TRANSACTION_PHASE)
  marker_invocation=$(read_manifest_value "$marker_path" DEPLOY_INVOCATION_ID)
  marker_expected=$(read_manifest_value "$marker_path" DEPLOY_EXPECTED_SHA)
  marker_plan=$(read_manifest_value "$marker_path" DEPLOY_PLAN_DIGEST)
  case "$marker_invocation" in ''|*[!A-Za-z0-9._:-]*) return 1 ;; esac
  case "$marker_expected" in ''|*[!0-9a-f]*) return 1 ;; esac
  case "$marker_plan" in ''|*[!0-9a-f]*) return 1 ;; esac
  case "$marker_phase" in
    runtime-promoted|complete)
      [ -f "$DEPLOY_DIR/releases/current-release.env" ] \
        && [ "$(read_manifest_value "$DEPLOY_DIR/releases/current-release.env" DEPLOY_INVOCATION_ID)" = "$marker_invocation" ] \
        && [ "$(read_manifest_value "$DEPLOY_DIR/releases/current-release.env" DEPLOY_EXPECTED_SHA)" = "$marker_expected" ] \
        && [ "$(read_manifest_value "$DEPLOY_DIR/releases/current-release.env" DEPLOY_PLAN_DIGEST)" = "$marker_plan" ] \
        && release_image_env_matches_manifest \
          "$DEPLOY_DIR/releases/current-images.env" \
          "$DEPLOY_DIR/releases/current-release.env"
      return
      ;;
    healthy-no-op)
      [ -f "$DEPLOY_DIR/releases/current-release.env" ] \
        && [ "$(read_manifest_value "$DEPLOY_DIR/releases/current-release.env" DEPLOY_EXPECTED_SHA)" = "$marker_expected" ] \
        && [ "$(read_manifest_value "$DEPLOY_DIR/releases/current-release.env" DEPLOY_PLAN_DIGEST)" = "$marker_plan" ] \
        && release_image_env_matches_manifest \
          "$DEPLOY_DIR/releases/current-images.env" \
          "$DEPLOY_DIR/releases/current-release.env"
      return
      ;;
    *) return 1 ;;
  esac
}

transaction_promotion_proven() {
  transaction_marker_promotion_proven "$TRANSACTION_MARKER"
}

cleanup_proven_promoted_transaction() {
  local marker_path=$1
  local marker_invocation marker_expected marker_plan marker_staging marker_backup marker_lock
  local lock_status transaction_history
  marker_invocation=$(read_manifest_value "$marker_path" DEPLOY_INVOCATION_ID)
  marker_expected=$(read_manifest_value "$marker_path" DEPLOY_EXPECTED_SHA)
  marker_plan=$(read_manifest_value "$marker_path" DEPLOY_PLAN_DIGEST)
  marker_staging=$(read_manifest_value "$marker_path" DEPLOY_SUPPORT_STAGING_DIR)
  marker_backup=$(read_manifest_value "$marker_path" DEPLOY_SUPPORT_BACKUP_DIR)
  marker_lock=$(read_manifest_value "$marker_path" DEPLOY_RELEASE_LOCK_FILE)
  [ "$marker_path" = "$TRANSACTION_DIR/$marker_invocation.env" ] || return 86
  [ "$marker_backup" = "$DEPLOY_DIR/releases/support-recovery/$marker_invocation" ] || return 86
  case "$marker_staging" in "$DEPLOY_DIR/.support-staging/"*) ;; *) return 86 ;; esac
  [ "$marker_lock" = "$RELEASE_LOCK_FILE" ] || return 86

  if [ -d "${marker_lock}.d" ]; then
    lock_status=$(managed_lock_status_for_dir "${marker_lock}.d")
    [ "$lock_status" = stale ] || return 75
    [ "$(managed_lock_read_field "${marker_lock}.d" invocation-id)" = "$marker_invocation" ] || return 86
    remove_stale_managed_lock "$marker_lock" || return $?
  fi

  # Promotion proof is authoritative: cleanup must never invoke restore-support.
  rm -rf "$marker_backup" "$marker_staging"
  rmdir "$DEPLOY_DIR/.support-staging" 2>/dev/null || true
  mkdir -p "$DEPLOY_DIR/releases/history"
  transaction_history="$DEPLOY_DIR/releases/history/$(date -u +%Y%m%dT%H%M%SZ)-transaction-$marker_invocation.env"
  atomic_copy_file "$marker_path" "$transaction_history" 600
  rm -f "$marker_path"
  if [ "$marker_expected" = "$EXPECTED_SHA" ] && [ "$marker_plan" = "$PLAN_DIGEST" ]; then
    PROMOTED_RECOVERY_CLEANUP_ONLY=1
  else
    PROMOTED_RECOVERY_CLEANUP_ONLY=2
  fi
  printf 'Recovered proven promoted Cloud transaction without restoring support: invocation=%s\n' \
    "$marker_invocation"
}

scan_durable_transaction_state() {
  local candidate marker_phase lock_state lock_invocation orphan_found=0
  local orphan_invocation=unknown-orphan orphan_expected=unknown orphan_plan=unknown
  local orphan_marker=missing orphan_backup=missing orphan_staging=missing orphan_lock="$RELEASE_LOCK_FILE"
  mkdir -p "$TRANSACTION_DIR"

  lock_state=absent
  lock_invocation=""
  if [ -d "${RELEASE_LOCK_FILE}.d" ]; then
    lock_state=$(managed_lock_status_for_dir "${RELEASE_LOCK_FILE}.d")
    lock_invocation=$(managed_lock_read_field "${RELEASE_LOCK_FILE}.d" invocation-id)
    case "$lock_state" in
      live|initializing)
        printf 'Cloud release transaction lock is active; fail-fast without mutation: %s\n' \
          "$(describe_managed_lock "$RELEASE_LOCK_FILE")" >&2
        return 75
        ;;
    esac
  fi

  for candidate in "$TRANSACTION_DIR"/*.env; do
    [ -f "$candidate" ] || continue
    marker_phase=$(read_manifest_value "$candidate" DEPLOY_TRANSACTION_PHASE)
    if transaction_marker_promotion_proven "$candidate"; then
      if [ "$lock_state" != absent ] \
        && [ "$lock_invocation" != "$(read_manifest_value "$candidate" DEPLOY_INVOCATION_ID)" ]; then
        printf 'Promoted marker and durable release lock invocation differ; refusing cleanup.\n' >&2
        orphan_found=1
      elif cleanup_proven_promoted_transaction "$candidate"; then
        lock_state=absent
        lock_invocation=""
        continue
      else
        cleanup_status=$?
        [ "$cleanup_status" -eq 75 ] && return 75
        orphan_found=1
      fi
    else
      printf 'Incomplete or unproven durable Cloud transaction marker requires operator recovery: %s phase=%s\n' \
        "$candidate" "${marker_phase:-missing}" >&2
      orphan_found=1
    fi
    if [ "$orphan_marker" = missing ]; then
      orphan_marker=$candidate
      orphan_invocation=$(read_manifest_value "$candidate" DEPLOY_INVOCATION_ID)
      orphan_expected=$(read_manifest_value "$candidate" DEPLOY_EXPECTED_SHA)
      orphan_plan=$(read_manifest_value "$candidate" DEPLOY_PLAN_DIGEST)
      orphan_backup=$(read_manifest_value "$candidate" DEPLOY_SUPPORT_BACKUP_DIR)
      orphan_staging=$(read_manifest_value "$candidate" DEPLOY_SUPPORT_STAGING_DIR)
    fi
  done

  if [ "$lock_state" != absent ]; then
    printf 'Stale or malformed Cloud release transaction lock requires durable recovery; it will not be auto-removed: %s\n' \
      "$(describe_managed_lock "$RELEASE_LOCK_FILE")" >&2
    orphan_found=1
    if [ "$orphan_invocation" = unknown-orphan ] && [ -n "$lock_invocation" ]; then
      orphan_invocation=$lock_invocation
      orphan_plan=$(managed_lock_read_field "${RELEASE_LOCK_FILE}.d" plan-digest)
      orphan_backup="$DEPLOY_DIR/releases/support-recovery/$lock_invocation"
      orphan_staging="$DEPLOY_DIR/.support-staging/$lock_invocation"
      orphan_marker="$TRANSACTION_DIR/$lock_invocation.env"
    fi
  fi

  for candidate in "$DEPLOY_DIR/.support-staging"/*; do
    [ -e "$candidate" ] || continue
    [ "$(readlink -f "$candidate")" = "$(readlink -f "$STAGING_DIR")" ] && continue
    printf 'Orphan Cloud support staging directory requires operator recovery: %s\n' "$candidate" >&2
    orphan_found=1
    [ "$orphan_staging" != missing ] || orphan_staging=$candidate
  done
  for candidate in "$DEPLOY_DIR/releases/support-recovery"/*; do
    [ -e "$candidate" ] || continue
    printf 'Orphan Cloud support recovery directory requires operator recovery: %s\n' "$candidate" >&2
    orphan_found=1
    [ "$orphan_backup" != missing ] || orphan_backup=$candidate
  done

  if [ "$orphan_found" -ne 0 ]; then
    write_blocked_evidence_for \
      "$orphan_invocation" \
      "$orphan_expected" \
      "$orphan_plan" \
      blocked-orphaned-durable-transaction \
      78 \
      unconfirmed \
      "$orphan_marker" \
      "$orphan_backup" \
      "$orphan_staging" \
      "$orphan_lock"
    return 78
  fi
}

support_restore_confirmed() {
  [ -f "$SUPPORT_BACKUP_DIR/restore-result" ] \
    && [ "$(sed -n '1p' "$SUPPORT_BACKUP_DIR/restore-result")" = restored ]
}

restore_support() {
  [ -x "$SUPPORT_BACKUP_DIR/restore-support.sh" ] || return 86
  sh "$SUPPORT_BACKUP_DIR/restore-support.sh" "$DEPLOY_DIR" "$SUPPORT_BACKUP_DIR"
  support_restore_confirmed
}

cleanup_transaction() {
  local transaction_status=$?
  local final_status=$transaction_status
  local preserve_lock=0
  local preserve_staging=0
  local preserve_backup=0
  local blocked_phase
  trap - EXIT HUP INT TERM
  set +e

  if [ "$PROCESS_GROUP_QUIESCED" -ne 1 ]; then
    blocked_phase=${FAILURE_PHASE:-blocked-child-process-group-not-quiesced}
    write_blocked_evidence "$blocked_phase" "$transaction_status" unconfirmed
    if lock_owned_by_transaction; then
      printf '%s\n' "$blocked_phase" | atomic_write_file "${RELEASE_LOCK_FILE}.d/phase" 600
    fi
    printf 'Cloud child process group termination is unconfirmed; restore and lock release are prohibited.\n' >&2
    exit 86
  fi

  if transaction_promotion_proven; then
    # Runtime/current/marker agreement is the commit proof. Once present, old
    # support is never restored even if the parent is interrupted during
    # transaction artifact cleanup.
    rm -rf "$SUPPORT_BACKUP_DIR"
    if [ "$RELEASE_LOCK_ACQUIRED" -eq 1 ] && lock_owned_by_transaction; then
      release_managed_lock "$RELEASE_LOCK_FILE" || true
      RELEASE_LOCK_ACQUIRED=0
    fi
    rm -rf "$STAGING_DIR"
    rmdir "$DEPLOY_DIR/.support-staging" 2>/dev/null || true
    if [ -f "$TRANSACTION_MARKER" ]; then
      mkdir -p "$DEPLOY_DIR/releases/history"
      promoted_history="$DEPLOY_DIR/releases/history/$(date -u +%Y%m%dT%H%M%SZ)-transaction-$INVOCATION_ID.env"
      if atomic_copy_file "$TRANSACTION_MARKER" "$promoted_history" 600; then
        rm -f "$TRANSACTION_MARKER"
      fi
    fi
    exit "$final_status"
  fi

  if [ -d "$SUPPORT_BACKUP_DIR" ]; then
    if blocked_evidence_matches_invocation && support_restore_confirmed; then
      preserve_backup=1
    elif support_restore_confirmed; then
      if [ -n "$FAILURE_PHASE" ]; then
        write_blocked_evidence "$FAILURE_PHASE" "$transaction_status" restored
        preserve_backup=1
      else
        rm -rf "$SUPPORT_BACKUP_DIR"
      fi
    elif restore_support; then
      if [ -n "$FAILURE_PHASE" ]; then
        write_blocked_evidence "$FAILURE_PHASE" "$transaction_status" restored
        preserve_backup=1
      elif blocked_evidence_matches_invocation; then
        preserve_backup=1
      else
        rm -rf "$SUPPORT_BACKUP_DIR"
      fi
    else
      blocked_phase=${FAILURE_PHASE:-blocked-support-restore}
      write_blocked_evidence "$blocked_phase" "$transaction_status" failed
      if lock_owned_by_transaction; then
        printf '%s\n' "$blocked_phase" | atomic_write_file "${RELEASE_LOCK_FILE}.d/phase" 600
      fi
      printf 'Cloud support restore is unconfirmed; lock, staging and recovery backup are preserved.\n' >&2
      preserve_lock=1
      preserve_staging=1
      preserve_backup=1
      final_status=86
    fi
  elif [ -n "$FAILURE_PHASE" ] && [ "$SUPPORT_INSTALLED" -eq 1 ]; then
    write_blocked_evidence "$FAILURE_PHASE" "$transaction_status" missing
    if lock_owned_by_transaction; then
      printf '%s\n' "$FAILURE_PHASE" | atomic_write_file "${RELEASE_LOCK_FILE}.d/phase" 600
    fi
    preserve_lock=1
    preserve_staging=1
    final_status=86
  fi

  if [ "$preserve_backup" -eq 0 ] && [ -d "$SUPPORT_BACKUP_DIR" ]; then
    rm -rf "$SUPPORT_BACKUP_DIR"
  fi
  if [ "$preserve_lock" -eq 0 ] && [ "$RELEASE_LOCK_ACQUIRED" -eq 1 ] && lock_owned_by_transaction; then
    release_managed_lock "$RELEASE_LOCK_FILE" || true
    RELEASE_LOCK_ACQUIRED=0
  fi
  if [ "$preserve_staging" -eq 0 ]; then
    rm -rf "$STAGING_DIR"
    rmdir "$DEPLOY_DIR/.support-staging" 2>/dev/null || true
  fi
  if [ "$final_status" -ne 86 ] \
    && [ -z "$FAILURE_PHASE" ] \
    && ! blocked_evidence_matches_invocation \
    && ! transaction_promotion_proven; then
    rm -f "$TRANSACTION_MARKER"
  fi
  exit "$final_status"
}

handle_parent_signal() {
  local signal_name="$1"
  local signal_status="$2"
  local signal_phase
  trap - HUP INT TERM
  PARENT_SIGNAL_STATUS=$signal_status
  signal_phase=$(printf '%s' "$signal_name" | tr '[:upper:]' '[:lower:]')
  if ! transaction_promotion_proven; then
    FAILURE_PHASE="blocked-parent-${signal_phase}-before-promotion"
  fi
  if [ -n "$CHILD_PID" ] && process_group_alive "$CHILD_PID"; then
    printf 'Cloud transaction parent forwarding %s to release child pid=%s and waiting.\n' \
      "$signal_name" "$CHILD_PID" >&2
    kill -"$signal_name" -- "-$CHILD_PID" 2>/dev/null || true
    if ! terminate_child_group "$CHILD_PID" "$CHILD_ROLE"; then
      PROCESS_GROUP_QUIESCED=0
      FAILURE_PHASE=blocked-child-process-group-not-quiesced
    else
      CHILD_PID=""
      CHILD_ROLE=""
    fi
  fi
  exit "$signal_status"
}

if [ -e "$DEPLOY_DIR/releases/deploy-blocked.env" ]; then
  printf 'Cloud deployment is blocked by durable recovery evidence: %s\n' "$DEPLOY_DIR/releases/deploy-blocked.env" >&2
  exit 78
fi
RELEASE_LOCK_FILE=$(resolve_managed_lock_file \
  "${DEPLOY_RELEASE_LOCK_FILE:-$CLOUD_RELEASE_LOCK_FILE_DEFAULT}" \
  "$DEPLOY_DIR/.cloud-release.lock")
scan_durable_transaction_state || exit $?
case "$PROMOTED_RECOVERY_CLEANUP_ONLY" in
  1)
    rm -rf "$STAGING_DIR"
    rmdir "$DEPLOY_DIR/.support-staging" 2>/dev/null || true
    printf 'Cloud promoted transaction recovery completed in cleanup-only mode; no support install or rollout was repeated.\n'
    exit 0
    ;;
  2)
    rm -rf "$STAGING_DIR"
    rmdir "$DEPLOY_DIR/.support-staging" 2>/dev/null || true
    printf 'A different promoted Cloud transaction was cleaned; rerun the requested release after verifying current state.\n' >&2
    exit 75
    ;;
esac
trap cleanup_transaction EXIT
trap 'handle_parent_signal HUP 129' HUP
trap 'handle_parent_signal INT 130' INT
trap 'handle_parent_signal TERM 143' TERM
acquire_managed_lock \
  "$RELEASE_LOCK_FILE" \
  cloud-release \
  "$RELEASE_TAG" \
  support-install \
  "workspace-invocation-$INVOCATION_ID"
RELEASE_LOCK_ACQUIRED=1
printf '%s\n' "$INVOCATION_ID" | atomic_write_file "${RELEASE_LOCK_FILE}.d/invocation-id" 600
printf '%s\n' "$PLAN_DIGEST" | atomic_write_file "${RELEASE_LOCK_FILE}.d/plan-digest" 600
write_transaction_marker support-install-started

SUPPORT_TRANSACTION_STARTED=1
if ! start_isolated_child installer \
  env -i \
  PATH="$TRUSTED_PATH" \
  HOME="$TRUSTED_HOME" \
  LANG=C \
  sh "$INSTALLER" \
    "$DEPLOY_DIR" \
    "$STAGING_DIR" \
    "$INVOCATION_ID" \
    "$RELEASE_TAG" \
    "$SUPPORT_MANIFEST_DIGEST" \
    "$$" \
    "$RELEASE_LOCK_FILE"; then
  FAILURE_PHASE=blocked-support-installer-start
  exit 127
fi
write_transaction_marker support-installer-running
if wait_isolated_child; then
  SUPPORT_INSTALLED=1
  write_transaction_marker support-installed
else
  installer_status=$?
  if [ -d "$SUPPORT_BACKUP_DIR" ] && ! support_restore_confirmed; then
    FAILURE_PHASE=blocked-support-install-restore
  fi
  exit "$installer_status"
fi

FAILURE_PHASE=blocked-deploy-release-start
if [ ! -f "$DEPLOY_RELEASE_SCRIPT" ] \
  || [ -L "$DEPLOY_RELEASE_SCRIPT" ] \
  || [ ! -r "$DEPLOY_RELEASE_SCRIPT" ] \
  || [ ! -x "$DEPLOY_RELEASE_SCRIPT" ]; then
  printf 'Installed deploy-release target is missing, unsafe, unreadable or non-executable: %s\n' "$DEPLOY_RELEASE_SCRIPT" >&2
  exit 126
fi
if ! bash -n "$DEPLOY_RELEASE_SCRIPT"; then
  printf 'Installed deploy-release target failed Bash syntax validation: %s\n' "$DEPLOY_RELEASE_SCRIPT" >&2
  exit 126
fi

update_managed_lock_phase "$RELEASE_LOCK_FILE" preflight
rm -f "$HANDOFF_MARKER"
FAILURE_PHASE=""
cd "$DEPLOY_DIR"
write_transaction_marker child-starting
if ! start_isolated_child release \
  env -i \
  PATH="$TRUSTED_PATH" \
  HOME="$TRUSTED_HOME" \
  LANG=C \
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID="$INVOCATION_ID" \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
  DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
  DEPLOY_RELEASE_LOCK_PARENT_OWNED=1 \
  DEPLOY_RELEASE_LOCK_OWNER_PID="$$" \
  DEPLOY_RELEASE_LOCK_FILE="$RELEASE_LOCK_FILE" \
  DEPLOY_RELEASE_HANDOFF_MARKER="$HANDOFF_MARKER" \
  DEPLOY_TRANSACTION_MARKER="$TRANSACTION_MARKER" \
  DEPLOY_SUPPORT_STAGING_DIR="$STAGING_DIR" \
  DEPLOY_SUPPORT_BACKUP_DIR="$SUPPORT_BACKUP_DIR" \
  DEPLOY_IMAGE_MANIFEST="$IMAGE_MANIFEST" \
  DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_MANIFEST_DIGEST" \
  DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
  EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_MANIFEST_DIGEST" \
  DEPLOY_GIT_SHA="$EXPECTED_SHA" \
  DEPLOY_TRIGGERED_BY=workspace \
  bash "$DEPLOY_RELEASE_SCRIPT" "$RELEASE_TAG" --services "$DEPLOY_SERVICES"; then
  FAILURE_PHASE=blocked-deploy-release-start
  exit 127
fi
write_transaction_marker child-running
if wait_isolated_child; then
  child_status=0
else
  child_status=$?
fi

if [ "$child_status" -ne 0 ]; then
  if transaction_promotion_proven; then
    FAILURE_PHASE=""
  elif [ ! -f "$HANDOFF_MARKER" ]; then
    FAILURE_PHASE=blocked-deploy-release-start
  else
    FAILURE_PHASE=blocked-child-rollout-unproven
  fi
  exit "$child_status"
fi

if ! transaction_promotion_proven; then
  FAILURE_PHASE=blocked-child-success-without-promotion-proof
  exit 86
fi
write_transaction_marker complete 0

if [ "$RELEASE_LOCK_ACQUIRED" -eq 1 ] && lock_owned_by_transaction; then
  release_managed_lock "$RELEASE_LOCK_FILE"
  RELEASE_LOCK_ACQUIRED=0
fi
rm -rf "$STAGING_DIR"
rmdir "$DEPLOY_DIR/.support-staging" 2>/dev/null || true
transaction_history="$DEPLOY_DIR/releases/history/$(date -u +%Y%m%dT%H%M%SZ)-transaction-$INVOCATION_ID.env"
atomic_copy_file "$TRANSACTION_MARKER" "$transaction_history" 600
rm -f "$TRANSACTION_MARKER"
trap - EXIT HUP INT TERM
printf 'Cloud workspace release transaction completed: invocation=%s release=%s\n' "$INVOCATION_ID" "$RELEASE_TAG"
