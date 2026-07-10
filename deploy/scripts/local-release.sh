#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOY_DIR/.." && pwd)"
ORIGINAL_ARGS=("$@")

REQUESTED_SERVICES=""
REQUESTED_ALL=false
DRY_RUN=false
SSH_TARGET="${DEPLOY_SSH_TARGET:-}"
REMOTE_DEPLOY_DIR="${REMOTE_DEPLOY_DIR:-/data/iiot-platform/cloud/deploy}"
SSH_TIMEOUT_SECONDS="${SSH_TIMEOUT_SECONDS:-2400}"
SYNC_TIMEOUT_SECONDS="${SYNC_TIMEOUT_SECONDS:-180}"
SSH_CONNECT_TIMEOUT_SECONDS="${SSH_CONNECT_TIMEOUT_SECONDS:-10}"
SSH_OPTIONS=(-o BatchMode=yes -o "ConnectTimeout=$SSH_CONNECT_TIMEOUT_SECONDS")

SUPPORT_FILES=(
  scripts/check-container-nonroot-readiness.sh
  scripts/check-release-state-access.sh
  scripts/deploy-release.sh
  scripts/ensure-oidc-signing-cert.sh
  scripts/export-client-release-history.sh
  scripts/harbor-retention.sh
  scripts/import-client-release-history.sh
  scripts/ops-check.sh
  scripts/post-deploy-check.sh
  scripts/post-release-cleanup.sh
  scripts/postgres-backup.sh
  scripts/postgres-restore.sh
  scripts/postgres-verify-backup.sh
  scripts/pre-deploy-check.sh
  scripts/release-common.sh
  scripts/rollback-release.sh
  scripts/update-deploy-env.sh
  scripts/verify-edge-installer-catalog.sh
  scripts/workspace-release-transaction.sh
  nginx/nginx.conf
  docker-compose.prod.yml
)
SUPPORT_MANIFEST_NAME=.cloud-support-manifest.sha256
SUPPORT_ALLOWLIST_NAME=.cloud-support-allowlist.txt
SUPPORT_INSTALLER_NAME=.install-cloud-support.sh
IMAGE_MANIFEST_NAME=.cloud-image-manifest.env
SYNCED_SUPPORT_MANIFEST_DIGEST=""
SYNCED_IMAGE_MANIFEST_DIGEST=""
SYNCED_IMAGE_CONTENT_DIGEST=""

WORKSPACE_ENTRYPOINT="${IIOT_WORKSPACE_DEPLOY_ENTRYPOINT:-}"
WORKSPACE_INVOCATION_ID="${IIOT_WORKSPACE_DEPLOY_INVOCATION_ID:-}"
WORKSPACE_EXPECTED_SHA="${IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA:-}"
WORKSPACE_PLAN_DIGEST="${IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST:-}"
WORKSPACE_PLAN_FILE="${IIOT_WORKSPACE_DEPLOY_PLAN_FILE:-}"
WORKSPACE_PROFILE_DIGEST="${IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST:-}"

usage() {
  cat <<'EOF'
Usage:
  REGISTRY=<harbor-registry> deploy/scripts/local-release.sh --services httpapi,gateway,dataworker,migration --ssh-target github-runner@<shared-host>
  REGISTRY=<harbor-registry> VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge \
    deploy/scripts/local-release.sh --services web --ssh-target github-runner@<shared-host>
  REGISTRY=<harbor-registry> VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge \
    deploy/scripts/local-release.sh --all --ssh-target github-runner@<shared-host>

Builds selected Cloud images locally, pushes Harbor tags, then SSH-triggers
the server-side deploy/scripts/deploy-release.sh entrypoint.
EOF
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

sha256_early_file() {
  local path="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{print $1}'
  else
    fail "sha256sum or shasum is required."
  fi
}

require_uint_range() {
  local name="$1"
  local value="$2"
  local minimum="$3"
  local maximum="$4"
  [[ "$value" =~ ^[0-9]+$ ]] || fail "$name must contain decimal digits only: $value"
  [ "$value" -ge "$minimum" ] && [ "$value" -le "$maximum" ] \
    || fail "$name must be between $minimum and $maximum: $value"
}

validate_transport_inputs() {
  case "$REMOTE_DEPLOY_DIR" in
    /*) ;;
    *) fail "REMOTE_DEPLOY_DIR must be an absolute path: $REMOTE_DEPLOY_DIR" ;;
  esac
  [[ "$REMOTE_DEPLOY_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] \
    || fail "REMOTE_DEPLOY_DIR contains whitespace, quotes, control characters, or unsupported characters: $REMOTE_DEPLOY_DIR"
  case "$REMOTE_DEPLOY_DIR" in
    *..*|*//* ) fail "REMOTE_DEPLOY_DIR must not contain '..' or empty path segments: $REMOTE_DEPLOY_DIR" ;;
  esac

  [[ "$SSH_TARGET" =~ ^([A-Za-z0-9][A-Za-z0-9._-]*@)?[A-Za-z0-9][A-Za-z0-9._-]*$ ]] \
    || fail "SSH target must be a safe alias or user@host and must not start with '-': $SSH_TARGET"

  require_uint_range SSH_TIMEOUT_SECONDS "$SSH_TIMEOUT_SECONDS" 60 14400
  require_uint_range SYNC_TIMEOUT_SECONDS "$SYNC_TIMEOUT_SECONDS" 10 1800
  require_uint_range SSH_CONNECT_TIMEOUT_SECONDS "$SSH_CONNECT_TIMEOUT_SECONDS" 1 120
}

validate_workspace_contract() {
  local actual_sha="${1:-}"

  if [ "$DRY_RUN" = true ]; then
    return
  fi

  [ "$WORKSPACE_ENTRYPOINT" = 1 ] \
    || fail "Formal Cloud release must be delegated by deploy/Invoke-WorkspaceDeploy.ps1 (missing IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1)."
  [[ "$WORKSPACE_INVOCATION_ID" =~ ^[A-Za-z0-9._:-]{1,128}$ ]] \
    || fail "Formal Cloud release requires a safe IIOT_WORKSPACE_DEPLOY_INVOCATION_ID."
  [[ "$WORKSPACE_EXPECTED_SHA" =~ ^[0-9a-f]{40}$ ]] \
    || fail "Formal Cloud release requires a lowercase full 40-hex IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA."
  [[ "$WORKSPACE_PLAN_DIGEST" =~ ^[0-9a-f]{64}$ ]] \
    || fail "Formal Cloud release requires a lowercase 64-hex IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST."
  [[ "$WORKSPACE_PROFILE_DIGEST" =~ ^[0-9a-f]{64}$ ]] \
    || fail "Formal Cloud release requires a lowercase 64-hex IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST."
  [ -n "$WORKSPACE_PLAN_FILE" ] && [ -r "$WORKSPACE_PLAN_FILE" ] \
    || fail "Formal Cloud release requires a readable IIOT_WORKSPACE_DEPLOY_PLAN_FILE."

  if [ -n "$actual_sha" ] && [ "$actual_sha" != "$WORKSPACE_EXPECTED_SHA" ]; then
    fail "Cloud fixed source SHA does not match the approved release plan: expected=$WORKSPACE_EXPECTED_SHA actual=$actual_sha"
  fi
}

canonical_requested_services() {
  local values
  local item
  local normalized
  values=""
  if [ "$REQUESTED_ALL" = true ]; then
    printf '%s\n' 'dataworker,gateway,httpapi,migration,web'
    return
  fi
  for item in $(printf '%s' "$REQUESTED_SERVICES" | tr ',' ' '); do
    case "$item" in
      httpapi|iiot-httpapi) normalized=httpapi ;;
      gateway|iiot-gateway) normalized=gateway ;;
      dataworker|iiot-dataworker) normalized=dataworker ;;
      migration|iiot-migration|iiot-migrationworkapp) normalized=migration ;;
      web|iiot-web) normalized=web ;;
      *) fail "Unsupported Cloud service in release plan binding: $item" ;;
    esac
    values="$values\n$normalized"
  done
  printf '%b\n' "$values" | sed '/^$/d' | sort -u | paste -sd, -
}

validate_release_plan_binding() {
  local actual_digest
  local expected_services
  local expected_all

  if [ "$DRY_RUN" = true ] && [ -z "$WORKSPACE_PLAN_FILE" ]; then
    return
  fi
  [ -r "$WORKSPACE_PLAN_FILE" ] || fail "Workspace release plan is missing or unreadable: ${WORKSPACE_PLAN_FILE:-missing}"
  actual_digest="$(sha256_early_file "$WORKSPACE_PLAN_FILE")"
  [ "$actual_digest" = "$WORKSPACE_PLAN_DIGEST" ] \
    || fail "Workspace release plan digest mismatch: expected=$WORKSPACE_PLAN_DIGEST actual=$actual_digest"
  expected_services="$(canonical_requested_services)"
  expected_all=false
  [ "$REQUESTED_ALL" = true ] && expected_all=true

  IIOT_PLAN_VALIDATE_PATH="$WORKSPACE_PLAN_FILE" \
  IIOT_PLAN_VALIDATE_SHA="$WORKSPACE_EXPECTED_SHA" \
  IIOT_PLAN_VALIDATE_SERVICES="$expected_services" \
  IIOT_PLAN_VALIDATE_ALL="$expected_all" \
  IIOT_PLAN_VALIDATE_PROFILE="$WORKSPACE_PROFILE_DIGEST" \
  pwsh -NoProfile -Command '& {
    $ErrorActionPreference = "Stop"
    $PlanPath = $env:IIOT_PLAN_VALIDATE_PATH
    $ExpectedSha = $env:IIOT_PLAN_VALIDATE_SHA
    $ExpectedServices = $env:IIOT_PLAN_VALIDATE_SERVICES
    $ExpectedAll = $env:IIOT_PLAN_VALIDATE_ALL
    $ExpectedProfileDigest = $env:IIOT_PLAN_VALIDATE_PROFILE
    $plan = Get-Content -Raw -Encoding UTF8 -LiteralPath $PlanPath | ConvertFrom-Json
    if ([string]$plan.target -cne "Cloud") { throw "release plan target must be Cloud" }
    if (([string]$plan.fullSha).ToLowerInvariant() -cne $ExpectedSha) { throw "release plan fullSha mismatch" }
    if (([string]$plan.profileDigest).ToLowerInvariant() -cne $ExpectedProfileDigest) { throw "release plan profileDigest mismatch" }
    $services = @($plan.services | ForEach-Object { ([string]$_).Trim() } | Sort-Object -Unique -CaseSensitive) -join ","
    if ($services -cne $ExpectedServices) { throw "release plan services mismatch: expected=$ExpectedServices actual=$services" }
    $expectedAllBoolean = [bool]::Parse($ExpectedAll)
    if ([bool]$plan.all -ne $expectedAllBoolean) { throw "release plan all-services mismatch" }
  }
  ' \
    || fail "Workspace release plan content does not match this Cloud invocation."
  printf 'Cloud workspace release plan binding verified: digest=%s services=%s\n' "$actual_digest" "$expected_services"
}

print_shell_argument() {
  local value="$1"
  local escaped
  case "$value" in
    '')
      printf "''"
      ;;
    *[!A-Za-z0-9_./:=,@+-]*)
      escaped=${value//\'/\'\\\'\'}
      printf "'%s'" "$escaped"
      ;;
    *)
      printf '%s' "$value"
      ;;
  esac
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --services)
      shift
      REQUESTED_SERVICES="${1:-}"
      ;;
    --services=*)
      REQUESTED_SERVICES="${1#--services=}"
      ;;
    --all)
      REQUESTED_ALL=true
      ;;
    --ssh-target)
      shift
      SSH_TARGET="${1:-}"
      ;;
    --ssh-target=*)
      SSH_TARGET="${1#--ssh-target=}"
      ;;
    --remote-dir)
      shift
      REMOTE_DEPLOY_DIR="${1:-}"
      ;;
    --remote-dir=*)
      REMOTE_DEPLOY_DIR="${1#--remote-dir=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown local-release option: $1"
      ;;
  esac
  shift
done

if [ "$REQUESTED_ALL" = true ] && [ -n "$REQUESTED_SERVICES" ]; then
  fail "Use either --all or --services, not both."
fi
if [ "$REQUESTED_ALL" != true ] && [ -z "$REQUESTED_SERVICES" ]; then
  fail "Cloud local release requires explicit --services or --all."
fi
validate_workspace_contract
if [ -z "${DEPLOY_ARTIFACT_DIR:-}" ]; then
  DEPLOY_ARTIFACT_DIR="$REPO_ROOT/artifacts/deploy/runs/${WORKSPACE_INVOCATION_ID:-dry-run-$$}"
  export DEPLOY_ARTIFACT_DIR
fi
if [ -z "$SSH_TARGET" ]; then
  fail "Cloud local release requires DEPLOY_SSH_TARGET or --ssh-target."
fi
validate_transport_inputs
validate_release_plan_binding
case "$SSH_TARGET" in
  *.example*|*internal.example*)
    fail "SSH target still uses the documentation example domain: $SSH_TARGET"
    ;;
  root@*)
    if [ "${ALLOW_ROOT_SSH_DEPLOY:-}" != "emergency" ]; then
      fail "Cloud local release refuses root SSH by default. Use a dedicated deploy user, or set ALLOW_ROOT_SSH_DEPLOY=emergency for an approved break-glass path."
    fi
    ;;
esac

child_process_ids() {
  local parent_pid="$1"
  ps -axo pid=,ppid= | awk -v parent_pid="$parent_pid" '$2 == parent_pid { print $1 }'
}

signal_process_tree() {
  local signal_name="$1"
  local root_pid="$2"
  local child_pid

  for child_pid in $(child_process_ids "$root_pid"); do
    signal_process_tree "$signal_name" "$child_pid"
  done
  kill "-$signal_name" "$root_pid" 2>/dev/null || true
}

run_with_timeout() {
  local seconds="$1"
  local label="$2"
  shift 2
  local marker
  local cmd_pid
  local timer_pid
  local exit_code
  marker="$(mktemp)"
  rm -f "$marker"

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] %s:' "$label"
    for argument in "$@"; do
      printf ' '
      print_shell_argument "$argument"
    done
    printf '\n'
    return 0
  fi

  "$@" &
  cmd_pid=$!
  (
    local sleep_pid=""
    stop_watchdog() {
      if [ -n "$sleep_pid" ]; then
        kill "$sleep_pid" 2>/dev/null || true
        wait "$sleep_pid" 2>/dev/null || true
      fi
      exit 0
    }
    trap stop_watchdog HUP INT TERM

    sleep "$seconds" &
    sleep_pid=$!
    wait "$sleep_pid" || exit 0
    if kill -0 "$cmd_pid" 2>/dev/null; then
      printf 'Timed out after %s seconds: %s\n' "$seconds" "$label" >&2
      : > "$marker"
      signal_process_tree TERM "$cmd_pid"
      grace_attempt=0
      while kill -0 "$cmd_pid" 2>/dev/null && [ "$grace_attempt" -lt 50 ]; do
        sleep 0.1
        grace_attempt=$((grace_attempt + 1))
      done
      if kill -0 "$cmd_pid" 2>/dev/null; then
        signal_process_tree KILL "$cmd_pid"
      fi
    fi
  ) &
  timer_pid=$!

  if wait "$cmd_pid"; then
    exit_code=0
  else
    exit_code=$?
  fi
  kill "$timer_pid" 2>/dev/null || true
  wait "$timer_pid" 2>/dev/null || true

  if [ -f "$marker" ]; then
    rm -f "$marker"
    return 124
  fi
  rm -f "$marker"
  return "$exit_code"
}

print_deploy_diagnostics() {
  cat >&2 <<EOF

Cloud deployment step failed. Use the immediately preceding error to identify whether it was access, lock, support sync, build, SSH, or timeout.
Diagnostics to run before retrying:
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && docker compose --env-file .env -f docker-compose.prod.yml ps'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && tail -n 200 releases/current-release.summary.md'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && ls -l releases'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && docker compose --env-file .env -f docker-compose.prod.yml logs --tail=200 iiot-httpapi iiot-gateway iiot-dataworker'
  docker buildx ls
  docker system df
EOF
}

check_remote_support_file_access() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] verify Cloud deploy support target access on %s:%s\n' "$SSH_TARGET" "$REMOTE_DEPLOY_DIR"
    printf '[dry-run] protected remote paths remain untouched: .env certs/ releases/ backups/\n'
    return
  fi

  if run_with_timeout "$SYNC_TIMEOUT_SECONDS" "check Cloud deploy support target access" \
    bash -c '
      set -euo pipefail
      ssh_target="$1"
      remote_dir="$2"
      connect_timeout="$3"
      ssh -o BatchMode=yes -o "ConnectTimeout=$connect_timeout" "$ssh_target" "REMOTE_DEPLOY_DIR='\''$remote_dir'\'' sh -s" <<'\''EOF'\''
set -eu

current_user=$(id -un 2>/dev/null || id -u)
current_group=$(id -gn 2>/dev/null || id -g)

fail_target() {
  path=$1
  reason=$2
  printf "Cloud deploy support target is not accessible to standard non-root deploy user %s:%s: %s (%s)\n" \
    "$current_user" "$current_group" "$path" "$reason" >&2
  exit 73
}

ensure_writable_directory() {
  path=$1
  if [ -e "$path" ] && [ ! -d "$path" ]; then
    fail_target "$path" "path exists but is not a directory"
  fi
  if ! mkdir -p "$path" 2>/dev/null; then
    fail_target "$path" "cannot create directory"
  fi
  if [ ! -r "$path" ] || [ ! -w "$path" ] || [ ! -x "$path" ]; then
    fail_target "$path" "directory must be readable, writable and searchable"
  fi

  probe="$path/.cloud-support-write-probe.$$"
  if ! (umask 077 && : > "$probe") 2>/dev/null; then
    fail_target "$path" "cannot create atomic replacement files"
  fi
  rm -f "$probe"
}

ensure_writable_directory "$REMOTE_DEPLOY_DIR"
ensure_writable_directory "$REMOTE_DEPLOY_DIR/scripts"
ensure_writable_directory "$REMOTE_DEPLOY_DIR/nginx"

if [ ! -f "$REMOTE_DEPLOY_DIR/.env" ] || [ ! -r "$REMOTE_DEPLOY_DIR/.env" ]; then
  fail_target "$REMOTE_DEPLOY_DIR/.env" "existing production environment file must be readable; sync never replaces it"
fi

printf "Cloud deploy support target access check passed: %s\n" "$REMOTE_DEPLOY_DIR"
printf "Cloud deploy support protected paths: untouched .env certs/ releases/ backups/\n"
EOF
    ' bash "$SSH_TARGET" "$REMOTE_DEPLOY_DIR" "$SSH_CONNECT_TIMEOUT_SECONDS"; then
    access_status=0
  else
    access_status=$?
  fi
  if [ "$access_status" -ne 0 ]; then
    print_deploy_diagnostics
    exit "$access_status"
  fi
}

check_remote_release_locks() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] verify Cloud release/cleanup locks are absent or proven stale on %s:%s\n' "$SSH_TARGET" "$REMOTE_DEPLOY_DIR"
    return
  fi

  if run_with_timeout "$SYNC_TIMEOUT_SECONDS" "check Cloud release and cleanup locks" \
    bash -c '
      set -euo pipefail
      ssh_target="$1"
      remote_dir="$2"
      connect_timeout="$3"
      ssh -o BatchMode=yes -o "ConnectTimeout=$connect_timeout" "$ssh_target" "REMOTE_DEPLOY_DIR='\''$remote_dir'\'' sh -s" <<'\''EOF'\''
set -eu
common="$REMOTE_DEPLOY_DIR/scripts/release-common.sh"

if [ -r "$common" ] && grep -q "ensure_managed_lock_available" "$common"; then
  DEPLOY_DIR=$REMOTE_DEPLOY_DIR
  export DEPLOY_DIR
  . "$common"
  unset DEPLOY_RELEASE_LOCK_FILE POST_RELEASE_CLEANUP_LOCK_FILE BASH_ENV ENV CDPATH IFS
  release_lock=$(resolve_managed_lock_file \
    "$CLOUD_RELEASE_LOCK_FILE_DEFAULT" \
    "$REMOTE_DEPLOY_DIR/.cloud-release.lock")
  cleanup_lock=$(resolve_managed_lock_file \
    "$POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT" \
    "$REMOTE_DEPLOY_DIR/.post-release-cleanup.lock")
  config_lock=$(resolve_managed_lock_file \
    "${CLOUD_CONFIG_LOCK_FILE_DEFAULT:-/data/iiot-platform/.locks/cloud-config.lock}" \
    "$REMOTE_DEPLOY_DIR/.cloud-config.lock")
  readonly_lock_precheck() {
    readonly_lock_file=$1
    readonly_lock_status=$(managed_lock_status_for_dir "${readonly_lock_file}.d")
    case "$readonly_lock_status" in
      absent) return 0 ;;
      *)
        printf "Cloud lock precheck is read-only and found non-absent state: status=%s %s\n" \
          "$readonly_lock_status" \
          "$(describe_managed_lock "$readonly_lock_file")" >&2
        return 75
        ;;
    esac
  }
  readonly_lock_precheck "$release_lock"
  readonly_lock_precheck "$cleanup_lock"
  readonly_lock_precheck "$config_lock"
  printf "Cloud release/cleanup/config lock precheck passed: release=%s cleanup=%s config=%s\n" \
    "$release_lock" "$cleanup_lock" "$config_lock"
  exit 0
fi

for legacy_lock_dir in \
  /data/iiot-platform/.locks/cloud-release.lock.d \
  /data/iiot-platform/.locks/deploy-cleanup.lock.d \
  /data/iiot-platform/.locks/cloud-config.lock.d \
  "$REMOTE_DEPLOY_DIR/.cloud-release.lock.d" \
  "$REMOTE_DEPLOY_DIR/.post-release-cleanup.lock.d" \
  "$REMOTE_DEPLOY_DIR/.cloud-config.lock.d"
do
  if [ -d "$legacy_lock_dir" ]; then
    printf "Cloud deploy lock exists but the remote support scripts cannot prove whether it is stale: %s\n" "$legacy_lock_dir" >&2
    printf "Inspect the lock owner before support sync or image build; do not wait or rerun blindly.\n" >&2
    exit 75
  fi
done
printf "Cloud legacy lock precheck passed; no release/cleanup lock directory exists.\n"
EOF
    ' bash "$SSH_TARGET" "$REMOTE_DEPLOY_DIR" "$SSH_CONNECT_TIMEOUT_SECONDS"; then
    lock_status=0
  else
    lock_status=$?
  fi
  if [ "$lock_status" -ne 0 ]; then
    print_deploy_diagnostics
    exit "$lock_status"
  fi
}

sha256_file() {
  local path="$1"

  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{print $1}'
    return
  fi
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{print $1}'
    return
  fi

  fail "sha256sum or shasum is required to build the Cloud support manifest."
}

read_manifest_value() {
  local manifest_path="$1"
  local key="$2"
  sed -n "s/^${key}=//p" "$manifest_path" | tail -n 1
}

validate_run_image_manifest() {
  local manifest_path="$1"
  local services_csv="$2"
  local key
  local image_ref
  local digest

  [ -f "$manifest_path" ] || fail "Missing run-private Cloud image manifest: $manifest_path"
  [ "$(read_manifest_value "$manifest_path" CLOUD_DEPLOY_INVOCATION_ID)" = "${WORKSPACE_INVOCATION_ID:-standalone}" ] \
    || fail "Cloud image manifest invocation does not match this run."
  [ "$(read_manifest_value "$manifest_path" CLOUD_DEPLOY_EXPECTED_SHA)" = "${WORKSPACE_EXPECTED_SHA:-${TAG#sha-}}" ] \
    || fail "Cloud image manifest SHA does not match the approved source SHA."
  [ "$(read_manifest_value "$manifest_path" CLOUD_DEPLOY_PLAN_DIGEST)" = "${WORKSPACE_PLAN_DIGEST:-standalone}" ] \
    || fail "Cloud image manifest plan digest does not match this invocation."
  [ "$(read_manifest_value "$manifest_path" CLOUD_DEPLOY_RELEASE_TAG)" = "$TAG" ] \
    || fail "Cloud image manifest release tag does not match the frozen source."
  [ "$(read_manifest_value "$manifest_path" CLOUD_DEPLOY_SERVICES)" = "$services_csv" ] \
    || fail "Cloud image manifest services do not match the run-private services file."

  for key in IIOT_HTTPAPI_IMAGE IIOT_GATEWAY_IMAGE IIOT_DATAWORKER_IMAGE IIOT_MIGRATION_IMAGE IIOT_WEB_IMAGE; do
    image_ref="$(read_manifest_value "$manifest_path" "$key")"
    [ -n "$image_ref" ] || continue
    case "$image_ref" in
      *:"$TAG") ;;
      *) fail "Cloud image manifest contains an image outside the frozen release tag: $key=$image_ref" ;;
    esac
    digest="$(read_manifest_value "$manifest_path" "${key}_DIGEST")"
    if [ "$DRY_RUN" = true ]; then
      [ "$digest" = dry-run ] || fail "Dry-run Cloud image manifest digest is invalid: ${key}_DIGEST=$digest"
    else
      [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] \
        || fail "Cloud image manifest is missing an OCI digest: ${key}_DIGEST=$digest"
    fi
  done
}

image_manifest_content_digest() {
  local manifest_path="$1"
  local content_file
  content_file="$(mktemp)"
  grep -E '^(CLOUD_DEPLOY_(EXPECTED_SHA|PLAN_DIGEST|RELEASE_TAG|SERVICES)|IIOT_(HTTPAPI|GATEWAY|DATAWORKER|MIGRATION|WEB)_IMAGE(_DIGEST)?)=' "$manifest_path" \
    | LC_ALL=C sort > "$content_file"
  [ -s "$content_file" ] || {
    rm -f "$content_file"
    fail "Cloud image manifest has no stable candidate content."
  }
  sha256_file "$content_file"
  rm -f "$content_file"
}

validate_local_support_package() {
  local package_dir="$1"
  local manifest="$package_dir/$SUPPORT_MANIFEST_NAME"
  local relative_path

  [ -s "$manifest" ] || fail "Cloud support manifest is empty: $manifest"
  while IFS= read -r relative_path; do
    [ -n "$relative_path" ] || continue
    case "$relative_path" in
      scripts/harbor-retention.sh|scripts/post-release-cleanup.sh)
        bash -n "$package_dir/$relative_path"
        ;;
      scripts/*.sh)
        sh -n "$package_dir/$relative_path"
        ;;
    esac
  done < "$package_dir/$SUPPORT_ALLOWLIST_NAME"
  printf 'Cloud support package validated locally before image build: %s\n' "$package_dir"
}

create_support_package() {
  local package_dir="$1"
  local manifest="$package_dir/$SUPPORT_MANIFEST_NAME"
  local allowlist="$package_dir/$SUPPORT_ALLOWLIST_NAME"
  local relative_path
  local source_path
  local target_path
  local digest

  : > "$manifest"
  : > "$allowlist"
  for relative_path in "${SUPPORT_FILES[@]}"; do
    case "$relative_path" in
      .env|.env/*|certs|certs/*|releases|releases/*|backups|backups/*)
        fail "Protected Cloud production state may not enter the support whitelist: $relative_path"
        ;;
    esac

    source_path="$DEPLOY_DIR/$relative_path"
    [ -f "$source_path" ] || fail "Missing Cloud deploy support file: $source_path"
    target_path="$package_dir/$relative_path"
    mkdir -p "$(dirname "$target_path")"
    cp "$source_path" "$target_path"
    digest="$(sha256_file "$source_path")"
    printf '%s  %s\n' "$digest" "$relative_path" >> "$manifest"
    printf '%s\n' "$relative_path" >> "$allowlist"
  done

  cat > "$package_dir/$SUPPORT_INSTALLER_NAME" <<'EOF'
#!/bin/sh
set -eu

DEPLOY_DIR=$1
STAGING_DIR=$2
INVOCATION_ID=$3
RELEASE_TAG=$4
EXPECTED_SUPPORT_DIGEST=$5
LOCK_OWNER_PID=$6
LOCK_FILE=$7
MANIFEST="$STAGING_DIR/.cloud-support-manifest.sha256"
ALLOWLIST="$STAGING_DIR/.cloud-support-allowlist.txt"
BACKUP_DIR="$DEPLOY_DIR/releases/support-recovery/$INVOCATION_ID"

fail_install() {
  printf 'Cloud deploy support staging validation failed: %s\n' "$*" >&2
  exit 73
}

for command_name in sha256sum sh bash docker install cmp sed grep awk sort readlink cp mv rm ln; do
  command -v "$command_name" >/dev/null 2>&1 || fail_install "required command not found: $command_name"
done
docker compose version >/dev/null 2>&1 || fail_install "docker compose is not available"

[ -f "$DEPLOY_DIR/.env" ] || fail_install "production .env is missing: $DEPLOY_DIR/.env"
[ -r "$DEPLOY_DIR/.env" ] || fail_install "production .env is not readable: $DEPLOY_DIR/.env"
[ -f "$MANIFEST" ] || fail_install "support manifest is missing"
[ -f "$ALLOWLIST" ] || fail_install "support allowlist is missing"
[ ! -L "$MANIFEST" ] && [ ! -L "$ALLOWLIST" ] \
  || fail_install "support manifest and allowlist must be regular non-symlink files"

case "$INVOCATION_ID" in
  ''|*[!A-Za-z0-9._:-]*) fail_install "unsafe invocation id" ;;
esac
case "$RELEASE_TAG" in
  sha-[0-9a-f]*) ;;
  *) fail_install "unsafe release tag: $RELEASE_TAG" ;;
esac
[ "$(readlink -f "$DEPLOY_DIR")" = "$DEPLOY_DIR" ] \
  || fail_install "deploy directory must be a canonical path without symlink components: $DEPLOY_DIR"

safe_support_path() {
  candidate_path=$1
  case "$candidate_path" in
    ''|/*|*..*|*[!A-Za-z0-9._/-]*|.env|.env/*|certs|certs/*|releases|releases/*|backups|backups/*)
      fail_install "unsafe or protected support path: $candidate_path"
      ;;
  esac
}

assert_target_path_not_symlinked() {
  candidate_path=$1
  safe_support_path "$candidate_path"
  current_path=$DEPLOY_DIR
  remaining_path=$candidate_path
  while [ -n "$remaining_path" ]; do
    first_component=${remaining_path%%/*}
    if [ "$remaining_path" = "$first_component" ]; then
      remaining_path=
    else
      remaining_path=${remaining_path#*/}
    fi
    current_path="$current_path/$first_component"
    [ ! -L "$current_path" ] || fail_install "support target contains a symlink component: $current_path"
  done
}

for protected_path in .env certs releases backups; do
  [ ! -e "$STAGING_DIR/$protected_path" ] || fail_install "protected path entered staging: $protected_path"
done

sed 's/^[0-9a-f][0-9a-f]*  //' "$MANIFEST" > "$STAGING_DIR/.cloud-support-manifest-paths.txt"
cmp "$ALLOWLIST" "$STAGING_DIR/.cloud-support-manifest-paths.txt" >/dev/null \
  || fail_install "manifest paths do not match the explicit support allowlist"

(cd "$STAGING_DIR" && sha256sum -c "$MANIFEST")

while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  safe_support_path "$relative_path"
  assert_target_path_not_symlinked "$relative_path"
  [ -f "$STAGING_DIR/$relative_path" ] || fail_install "staged support file is missing: $relative_path"
  [ ! -L "$STAGING_DIR/$relative_path" ] || fail_install "staged support file must not be a symlink: $relative_path"

  case "$relative_path" in
    scripts/harbor-retention.sh|scripts/post-release-cleanup.sh)
      bash -n "$STAGING_DIR/$relative_path"
      ;;
    scripts/*.sh)
      sh -n "$STAGING_DIR/$relative_path"
      ;;
  esac
done < "$ALLOWLIST"

docker compose \
  --env-file "$DEPLOY_DIR/.env" \
  -f "$STAGING_DIR/docker-compose.prod.yml" \
  config -q

# Exercise the candidate support tree against the real protected state before
# changing any installed support file.
cleanup_candidate_links() {
  rm -f "$STAGING_DIR/.env" "$STAGING_DIR/certs" "$STAGING_DIR/releases" "$STAGING_DIR/backups"
}
trap cleanup_candidate_links EXIT HUP INT TERM
ln -s "$DEPLOY_DIR/.env" "$STAGING_DIR/.env"
for protected_path in certs releases backups; do
  if [ -e "$DEPLOY_DIR/$protected_path" ]; then
    ln -s "$DEPLOY_DIR/$protected_path" "$STAGING_DIR/$protected_path"
  fi
done
DEPLOY_RELEASE_LOCK_FILE="$LOCK_FILE" \
DEPLOY_RELEASE_LOCK_OWNER_PID="$LOCK_OWNER_PID" \
EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$EXPECTED_SUPPORT_DIGEST" \
  sh "$STAGING_DIR/scripts/pre-deploy-check.sh" "$RELEASE_TAG"
cleanup_candidate_links
trap - EXIT HUP INT TERM

# Build a durable, validated union of the new allowlist and every path from the
# previous manifest. This is the only set the restore path may copy or remove.
[ ! -e "$BACKUP_DIR" ] || fail_install "support recovery directory already exists: $BACKUP_DIR"
support_install_phase=building-backup
install_committed=0
cleanup_install_temps() {
  while IFS= read -r relative_path; do
    [ -n "$relative_path" ] || continue
    rm -f "$DEPLOY_DIR/$relative_path.cloud-support-new.$$"
  done < "$ALLOWLIST"
  rm -f "$DEPLOY_DIR/.cloud-support-manifest.sha256.cloud-support-new.$$"
}
cleanup_or_restore_install() {
  install_status=$?
  trap - EXIT HUP INT TERM
  cleanup_install_temps || true
  case "$support_install_phase" in
    building-backup)
      rm -rf "$BACKUP_DIR"
      ;;
    installing)
      if [ "$install_committed" -ne 1 ]; then
        if ! sh "$BACKUP_DIR/restore-support.sh" "$DEPLOY_DIR" "$BACKUP_DIR"; then
          printf 'Cloud support installation failed and restore also failed; backup preserved: %s\n' "$BACKUP_DIR" >&2
          exit 86
        fi
      fi
      ;;
  esac
  exit "$install_status"
}
trap cleanup_or_restore_install EXIT HUP INT TERM

mkdir -p "$BACKUP_DIR/files"
chmod 700 "$BACKUP_DIR"
restore_paths="$BACKUP_DIR/paths.txt"
: > "$restore_paths"
while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  safe_support_path "$relative_path"
  printf '%s\n' "$relative_path" >> "$restore_paths"
done < "$ALLOWLIST"

old_manifest="$DEPLOY_DIR/.cloud-support-manifest.sha256"
if [ -e "$old_manifest" ]; then
  [ -f "$old_manifest" ] && [ ! -L "$old_manifest" ] \
    || fail_install "installed support manifest must be a regular non-symlink file"
  while read -r old_digest relative_path; do
    [ -n "${relative_path:-}" ] || continue
    case "$old_digest" in
      *[!0-9a-f]*|'') fail_install "installed support manifest contains an invalid digest" ;;
    esac
    [ "${#old_digest}" -eq 64 ] || fail_install "installed support manifest digest must contain 64 hex characters"
    safe_support_path "$relative_path"
    printf '%s\n' "$relative_path" >> "$restore_paths"
  done < "$old_manifest"
  cp -p "$old_manifest" "$BACKUP_DIR/old-manifest.sha256"
  printf 'present\n' > "$BACKUP_DIR/old-manifest.state"
else
  printf 'missing\n' > "$BACKUP_DIR/old-manifest.state"
fi
LC_ALL=C sort -u "$restore_paths" -o "$restore_paths"

restore_state="$BACKUP_DIR/state.txt"
: > "$restore_state"
while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  assert_target_path_not_symlinked "$relative_path"
  target_path="$DEPLOY_DIR/$relative_path"
  if [ -e "$target_path" ]; then
    [ -f "$target_path" ] && [ ! -L "$target_path" ] \
      || fail_install "installed support target must be a regular non-symlink file: $relative_path"
    mkdir -p "$BACKUP_DIR/files/$(dirname "$relative_path")"
    cp -p "$target_path" "$BACKUP_DIR/files/$relative_path"
    printf 'present %s %s\n' "$(sha256sum "$target_path" | awk '{print $1}')" "$relative_path" >> "$restore_state"
  else
    printf 'missing - %s\n' "$relative_path" >> "$restore_state"
  fi
done < "$restore_paths"

cat > "$BACKUP_DIR/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
set -eu
DEPLOY_DIR=$1
BACKUP_DIR=$2

restore_fail() {
  printf 'Cloud support restore failed: %s\n' "$*" >&2
  exit 86
}
safe_support_path() {
  candidate_path=$1
  case "$candidate_path" in
    ''|/*|*..*|*[!A-Za-z0-9._/-]*|.env|.env/*|certs|certs/*|releases|releases/*|backups|backups/*)
      restore_fail "unsafe or protected restore path: $candidate_path"
      ;;
  esac
}
assert_target_path_not_symlinked() {
  candidate_path=$1
  safe_support_path "$candidate_path"
  current_path=$DEPLOY_DIR
  remaining_path=$candidate_path
  while [ -n "$remaining_path" ]; do
    first_component=${remaining_path%%/*}
    if [ "$remaining_path" = "$first_component" ]; then remaining_path=; else remaining_path=${remaining_path#*/}; fi
    current_path="$current_path/$first_component"
    [ ! -L "$current_path" ] || restore_fail "restore target contains a symlink component: $current_path"
  done
}
[ "$(readlink -f "$DEPLOY_DIR")" = "$DEPLOY_DIR" ] || restore_fail "deploy directory is not canonical"
[ -f "$BACKUP_DIR/state.txt" ] && [ ! -L "$BACKUP_DIR/state.txt" ] || restore_fail "restore state is missing or unsafe"
while read -r state expected_digest relative_path; do
  [ -n "${relative_path:-}" ] || continue
  assert_target_path_not_symlinked "$relative_path"
  target_path="$DEPLOY_DIR/$relative_path"
  case "$state" in
    present)
      backup_path="$BACKUP_DIR/files/$relative_path"
      [ -f "$backup_path" ] && [ ! -L "$backup_path" ] || restore_fail "backup file is missing: $relative_path"
      [ "$(sha256sum "$backup_path" | awk '{print $1}')" = "$expected_digest" ] || restore_fail "backup hash mismatch: $relative_path"
      mkdir -p "$(dirname "$target_path")"
      temp_path="$target_path.cloud-support-restore.$$"
      cp -p "$backup_path" "$temp_path"
      mv -f "$temp_path" "$target_path"
      ;;
    missing)
      rm -f "$target_path"
      ;;
    *) restore_fail "unknown restore state for $relative_path: $state" ;;
  esac
done < "$BACKUP_DIR/state.txt"

manifest_target="$DEPLOY_DIR/.cloud-support-manifest.sha256"
case "$(sed -n '1p' "$BACKUP_DIR/old-manifest.state")" in
  present)
    [ -f "$BACKUP_DIR/old-manifest.sha256" ] && [ ! -L "$BACKUP_DIR/old-manifest.sha256" ] || restore_fail "old manifest backup is missing"
    while read -r old_digest relative_path; do
      [ -n "${relative_path:-}" ] || continue
      safe_support_path "$relative_path"
      case "$old_digest" in *[!0-9a-f]*|'') restore_fail "old manifest contains an invalid digest" ;; esac
      [ "${#old_digest}" -eq 64 ] || restore_fail "old manifest digest length is invalid"
    done < "$BACKUP_DIR/old-manifest.sha256"
    cp -p "$BACKUP_DIR/old-manifest.sha256" "$manifest_target.cloud-support-restore.$$"
    mv -f "$manifest_target.cloud-support-restore.$$" "$manifest_target"
    (cd "$DEPLOY_DIR" && sha256sum -c "$manifest_target")
    ;;
  missing) rm -f "$manifest_target" ;;
  *) restore_fail "old manifest state is invalid" ;;
esac
printf 'restored\n' > "$BACKUP_DIR/restore-result"
printf 'Cloud support restored from durable backup: %s\n' "$BACKUP_DIR"
RESTORE_EOF
chmod 700 "$BACKUP_DIR/restore-support.sh"
support_install_phase=installing

while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  case "$relative_path" in
    scripts/release-common.sh|nginx/nginx.conf|docker-compose.prod.yml)
      file_mode=644
      ;;
    scripts/*.sh)
      file_mode=755
      ;;
    *)
      fail_install "unsupported install target: $relative_path"
      ;;
  esac

  target_path="$DEPLOY_DIR/$relative_path"
  temp_path="$target_path.cloud-support-new.$$"
  install -m "$file_mode" "$STAGING_DIR/$relative_path" "$temp_path"
  mv -f "$temp_path" "$target_path"
done < "$ALLOWLIST"

(cd "$DEPLOY_DIR" && sha256sum -c "$MANIFEST")
support_manifest_target="$DEPLOY_DIR/.cloud-support-manifest.sha256"
support_manifest_temp="$support_manifest_target.cloud-support-new.$$"
install -m 644 "$MANIFEST" "$support_manifest_temp"
mv -f "$support_manifest_temp" "$support_manifest_target"
docker compose \
  --env-file "$DEPLOY_DIR/.env" \
  -f "$DEPLOY_DIR/docker-compose.prod.yml" \
  config -q

install_committed=1
trap - EXIT HUP INT TERM
cleanup_install_temps
printf 'installed\n' > "$BACKUP_DIR/install-result"
printf 'Cloud deploy support files synchronized and sha256-verified: %s\n' "$DEPLOY_DIR"
printf 'Cloud deploy support manifest installed: %s\n' "$support_manifest_target"
printf 'Cloud deploy support protected paths: untouched .env certs/ releases/ backups/\n'
EOF
  chmod 700 "$package_dir/$SUPPORT_INSTALLER_NAME"
  sh -n "$package_dir/$SUPPORT_INSTALLER_NAME" || fail "Generated Cloud support installer failed shell syntax validation."
}

stage_support_and_deploy() {
  local package_dir="$1"
  local deploy_services="$2"
  local support_manifest_digest="$3"
  local image_manifest_digest="$4"
  local image_content_digest="$5"
  local remote_dir_quoted
  local remote_command
  local remote_status

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] after successful image build, stage the run-bound support and image manifests on %s:%s/.support-staging\n' "$SSH_TARGET" "$REMOTE_DEPLOY_DIR"
    printf '[dry-run] acquire one Cloud release lock for support install, rollout, promotion and cleanup\n'
    printf '[dry-run] invoke remote release with invocation=%s expected_sha=%s plan_digest=%s services=%s\n' \
      "${WORKSPACE_INVOCATION_ID:-dry-run}" "${WORKSPACE_EXPECTED_SHA:-${TAG#sha-}}" "${WORKSPACE_PLAN_DIGEST:-dry-run}" "$deploy_services"
    return
  fi

  remote_dir_quoted="$(printf '%q' "$REMOTE_DEPLOY_DIR")"
  remote_command="set -euo pipefail
umask 077
deploy_dir=$remote_dir_quoted
[ \"\$(readlink -f \"\$deploy_dir\")\" = \"\$deploy_dir\" ] || { printf 'Remote deploy directory is not canonical: %s\\n' \"\$deploy_dir\" >&2; exit 64; }
staging_parent=\"\$deploy_dir/.support-staging\"
mkdir -p \"\$staging_parent\"
staging_dir=\$(mktemp -d \"\$staging_parent/cloud-${WORKSPACE_INVOCATION_ID}.XXXXXX\")
cleanup_staging_before_handoff() {
  status=\$?
  trap - EXIT HUP INT TERM
  rm -rf \"\$staging_dir\"
  rmdir \"\$staging_parent\" 2>/dev/null || true
  exit \"\$status\"
}
trap cleanup_staging_before_handoff EXIT HUP INT TERM
tar --no-same-owner -xf - -C \"\$staging_dir\"
printf '%s  %s\\n' '$support_manifest_digest' '$SUPPORT_MANIFEST_NAME' | (cd \"\$staging_dir\" && sha256sum -c -)
printf '%s  %s\\n' '$image_manifest_digest' '$IMAGE_MANIFEST_NAME' | (cd \"\$staging_dir\" && sha256sum -c -)
grep -qx 'CLOUD_DEPLOY_INVOCATION_ID=${WORKSPACE_INVOCATION_ID}' \"\$staging_dir/$IMAGE_MANIFEST_NAME\"
grep -qx 'CLOUD_DEPLOY_EXPECTED_SHA=${WORKSPACE_EXPECTED_SHA}' \"\$staging_dir/$IMAGE_MANIFEST_NAME\"
grep -qx 'CLOUD_DEPLOY_PLAN_DIGEST=${WORKSPACE_PLAN_DIGEST}' \"\$staging_dir/$IMAGE_MANIFEST_NAME\"
grep -qx 'CLOUD_DEPLOY_RELEASE_TAG=$TAG' \"\$staging_dir/$IMAGE_MANIFEST_NAME\"
grep -qx 'CLOUD_DEPLOY_SERVICES=$deploy_services' \"\$staging_dir/$IMAGE_MANIFEST_NAME\"
transaction_script=\"\$staging_dir/scripts/workspace-release-transaction.sh\"
[ -f \"\$transaction_script\" ] && [ ! -L \"\$transaction_script\" ] && [ -x \"\$transaction_script\" ] \
  || { printf 'Staged workspace release transaction is missing or unsafe: %s\\n' \"\$transaction_script\" >&2; exit 126; }
bash -n \"\$transaction_script\"
exec bash \"\$transaction_script\" \
  \"\$deploy_dir\" \
  \"\$staging_dir\" \
  '${WORKSPACE_INVOCATION_ID}' \
  '$TAG' \
  '${WORKSPACE_EXPECTED_SHA}' \
  '${WORKSPACE_PLAN_DIGEST}' \
  '$support_manifest_digest' \
  '$image_manifest_digest' \
  '$image_content_digest' \
  '$deploy_services'"

  if COPYFILE_DISABLE=1 run_with_timeout "$SSH_TIMEOUT_SECONDS" "stage Cloud support and execute release transaction" \
    bash -c '
      set -euo pipefail
      package_dir="$1"
      ssh_target="$2"
      connect_timeout="$3"
      remote_command="$4"
      remote_bash_command="env -i PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin HOME=\"\$HOME\" bash -c $(printf %q "$remote_command")"
      COPYFILE_DISABLE=1 tar -C "$package_dir" -cf - . \
        | ssh -o BatchMode=yes -o "ConnectTimeout=$connect_timeout" "$ssh_target" "$remote_bash_command"
    ' bash "$package_dir" "$SSH_TARGET" "$SSH_CONNECT_TIMEOUT_SECONDS" "$remote_command"; then
    remote_status=0
  else
    remote_status=$?
  fi

  if [ "$remote_status" -ne 0 ]; then
    print_deploy_diagnostics
    exit "$remote_status"
  fi
}

require_pushed_clean_head() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] skip clean/pushed HEAD enforcement.\n'
    return
  fi

  if [ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]; then
    git -C "$REPO_ROOT" status --short >&2
    fail "Cloud local release requires a clean worktree."
  fi

  local sha
  local remote
  local branch
  local remote_ref
  local remote_sha
  sha="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  remote="${GIT_REMOTE:-origin}"
  [[ "$remote" =~ ^[A-Za-z0-9._-]+$ ]] && [[ "$remote" != -* ]] \
    || fail "Cloud Git remote name is invalid: $remote"
  branch="${CLOUD_RELEASE_SOURCE_BRANCH:-$(git -C "$REPO_ROOT" symbolic-ref --quiet --short HEAD)}"
  [[ "$branch" =~ ^[A-Za-z0-9._/-]+$ ]] && [[ "$branch" != -* ]] \
    || fail "Cloud release source branch is invalid or detached without an approved source branch: $branch"
  remote_ref="refs/remotes/$remote/$branch"
  git -C "$REPO_ROOT" fetch --quiet --no-tags "$remote" "+refs/heads/$branch:$remote_ref"
  remote_sha="$(git -C "$REPO_ROOT" rev-parse --verify "$remote_ref")"
  [ "$sha" = "$remote_sha" ] \
    || fail "Cloud expected SHA is no longer the freshly fetched $remote/$branch tip: expected=$sha remoteTip=$remote_sha"
}

SNAPSHOT_PARENT=""
SNAPSHOT_REPO=""
SNAPSHOT_CHILD_PID=""

cleanup_release_snapshot() {
  if [ -n "$SNAPSHOT_REPO" ]; then
    git -C "$REPO_ROOT" worktree remove --force "$SNAPSHOT_REPO" >/dev/null 2>&1 || true
  fi
  if [ -n "$SNAPSHOT_PARENT" ]; then
    rm -rf "$SNAPSHOT_PARENT"
  fi
  SNAPSHOT_REPO=""
  SNAPSHOT_PARENT=""
}

handle_release_snapshot_signal() {
  local signal_name="$1"
  local signal_exit_code="$2"
  trap - EXIT HUP INT TERM
  printf 'Cloud fixed-commit release interrupted by signal %s; stopping the snapshot process tree.\n' "$signal_name" >&2
  if [ -n "$SNAPSHOT_CHILD_PID" ] && kill -0 "$SNAPSHOT_CHILD_PID" 2>/dev/null; then
    signal_process_tree TERM "$SNAPSHOT_CHILD_PID"
    wait "$SNAPSHOT_CHILD_PID" 2>/dev/null || true
  fi
  cleanup_release_snapshot
  exit "$signal_exit_code"
}

run_from_fixed_commit_snapshot() {
  local release_sha
  local run_id
  local artifact_dir
  local snapshot_status
  local release_branch

  release_sha="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  release_branch="$(git -C "$REPO_ROOT" symbolic-ref --quiet --short HEAD)"
  validate_workspace_contract "$release_sha"
  run_id="${WORKSPACE_INVOCATION_ID:-$(date -u +%Y%m%dT%H%M%SZ)-${release_sha:0:12}-$$}"
  artifact_dir="${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy/runs/$run_id}"
  case "$artifact_dir" in
    /*) ;;
    *) artifact_dir="$REPO_ROOT/$artifact_dir" ;;
  esac
  mkdir -p "$artifact_dir"

  SNAPSHOT_PARENT="$(mktemp -d "${TMPDIR:-/tmp}/iiot-cloud-release.XXXXXX")"
  SNAPSHOT_REPO="$SNAPSHOT_PARENT/repo"
  trap cleanup_release_snapshot EXIT
  trap 'handle_release_snapshot_signal HUP 129' HUP
  trap 'handle_release_snapshot_signal INT 130' INT
  trap 'handle_release_snapshot_signal TERM 143' TERM

  if ! git -C "$REPO_ROOT" worktree add --quiet --detach "$SNAPSHOT_REPO" "$release_sha"; then
    printf 'Could not create the fixed-commit Cloud release worktree for %s.\n' "$release_sha" >&2
    trap - EXIT HUP INT TERM
    cleanup_release_snapshot
    return 70
  fi

  printf 'Cloud release source frozen: sha=%s worktree=%s artifacts=%s\n' \
    "$release_sha" "$SNAPSHOT_REPO" "$artifact_dir"
  CLOUD_RELEASE_SNAPSHOT_ACTIVE=1 \
  CLOUD_RELEASE_SOURCE_SHA="$release_sha" \
  CLOUD_RELEASE_SOURCE_BRANCH="$release_branch" \
  DEPLOY_ARTIFACT_DIR="$artifact_dir" \
    bash "$SNAPSHOT_REPO/deploy/scripts/local-release.sh" "${ORIGINAL_ARGS[@]}" &
  SNAPSHOT_CHILD_PID=$!
  if wait "$SNAPSHOT_CHILD_PID"; then
    snapshot_status=0
  else
    snapshot_status=$?
  fi
  SNAPSHOT_CHILD_PID=""

  trap - EXIT HUP INT TERM
  cleanup_release_snapshot
  return "$snapshot_status"
}

if [ "$DRY_RUN" != true ]; then
  validate_workspace_contract "$(git -C "$REPO_ROOT" rev-parse HEAD)"
fi
require_pushed_clean_head
if [ "$DRY_RUN" != true ] && [ "${CLOUD_RELEASE_SNAPSHOT_ACTIVE:-0}" != 1 ]; then
  if run_from_fixed_commit_snapshot; then
    exit 0
  else
    exit $?
  fi
fi

if [ "${CLOUD_RELEASE_SNAPSHOT_ACTIVE:-0}" = 1 ]; then
  CURRENT_SNAPSHOT_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  if [ -z "${CLOUD_RELEASE_SOURCE_SHA:-}" ] || [ "$CURRENT_SNAPSHOT_SHA" != "$CLOUD_RELEASE_SOURCE_SHA" ]; then
    fail "Cloud release snapshot SHA mismatch: expected=${CLOUD_RELEASE_SOURCE_SHA:-missing} actual=$CURRENT_SNAPSHOT_SHA"
  fi
  validate_workspace_contract "$CURRENT_SNAPSHOT_SHA"
fi

TAG="sha-$(git -C "$REPO_ROOT" rev-parse HEAD)"
BUILD_ARGS=()
if [ "$REQUESTED_ALL" = true ]; then
  BUILD_ARGS+=(--all)
else
  BUILD_ARGS+=(--services "$REQUESTED_SERVICES")
fi
if [ "$DRY_RUN" = true ]; then
  BUILD_ARGS+=(--dry-run)
fi

LOCAL_SUPPORT_PACKAGE_DIR="$(mktemp -d)"
cleanup_local_support_package() {
  rm -rf "$LOCAL_SUPPORT_PACKAGE_DIR"
}
trap cleanup_local_support_package EXIT HUP INT TERM
create_support_package "$LOCAL_SUPPORT_PACKAGE_DIR"
validate_local_support_package "$LOCAL_SUPPORT_PACKAGE_DIR"

# Image build/push deliberately precedes every remote support-file mutation.
# A failed build therefore cannot install a half-new server-side deploy stack.
"$SCRIPT_DIR/build-and-push.sh" "${BUILD_ARGS[@]}"

SERVICES_FILE="${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}/cloud-built-services.txt"
IMAGES_FILE="${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}/cloud-images.env"
if [ ! -f "$SERVICES_FILE" ]; then
  fail "Missing built services file: $SERVICES_FILE"
fi
DEPLOY_SERVICES="$(tr -d '\r\n' < "$SERVICES_FILE")"
[ -n "$DEPLOY_SERVICES" ] || fail "Built services file is empty: $SERVICES_FILE"
validate_run_image_manifest "$IMAGES_FILE" "$DEPLOY_SERVICES"
cp "$IMAGES_FILE" "$LOCAL_SUPPORT_PACKAGE_DIR/$IMAGE_MANIFEST_NAME"

SYNCED_SUPPORT_MANIFEST_DIGEST="$(sha256_file "$LOCAL_SUPPORT_PACKAGE_DIR/$SUPPORT_MANIFEST_NAME")"
SYNCED_IMAGE_MANIFEST_DIGEST="$(sha256_file "$LOCAL_SUPPORT_PACKAGE_DIR/$IMAGE_MANIFEST_NAME")"
SYNCED_IMAGE_CONTENT_DIGEST="$(image_manifest_content_digest "$LOCAL_SUPPORT_PACKAGE_DIR/$IMAGE_MANIFEST_NAME")"
printf 'Cloud deploy support manifest digest bound to this release: %s\n' "$SYNCED_SUPPORT_MANIFEST_DIGEST"
printf 'Cloud image manifest digest bound to this invocation: %s\n' "$SYNCED_IMAGE_MANIFEST_DIGEST"
printf 'Cloud stable image candidate content digest: %s\n' "$SYNCED_IMAGE_CONTENT_DIGEST"

# These checks are deliberately read-only and run after a successful image
# build but before any support staging. Stale or malformed release state must
# be preserved for transaction recovery, never deleted by the workstation.
check_remote_support_file_access
check_remote_release_locks

stage_support_and_deploy \
  "$LOCAL_SUPPORT_PACKAGE_DIR" \
  "$DEPLOY_SERVICES" \
  "$SYNCED_SUPPORT_MANIFEST_DIGEST" \
  "$SYNCED_IMAGE_MANIFEST_DIGEST" \
  "$SYNCED_IMAGE_CONTENT_DIGEST"

trap - EXIT HUP INT TERM
cleanup_local_support_package
