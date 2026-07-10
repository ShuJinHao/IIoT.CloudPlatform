#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOY_DIR/.." && pwd)"

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
  scripts/verify-edge-installer-catalog.sh
  nginx/nginx.conf
  docker-compose.prod.yml
)
SUPPORT_MANIFEST_NAME=.cloud-support-manifest.sha256
SUPPORT_ALLOWLIST_NAME=.cloud-support-allowlist.txt
SUPPORT_INSTALLER_NAME=.install-cloud-support.sh

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
if [ -z "$SSH_TARGET" ]; then
  fail "Cloud local release requires DEPLOY_SSH_TARGET or --ssh-target."
fi
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
    sleep "$seconds"
    if kill -0 "$cmd_pid" 2>/dev/null; then
      printf 'Timed out after %s seconds: %s\n' "$seconds" "$label" >&2
      : > "$marker"
      kill -TERM "$cmd_pid" 2>/dev/null || true
      sleep 5
      kill -KILL "$cmd_pid" 2>/dev/null || true
    fi
  ) &
  timer_pid=$!

  set +e
  wait "$cmd_pid"
  exit_code=$?
  set -e
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

Cloud SSH deploy failed or timed out.
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

  if ! run_with_timeout "$SYNC_TIMEOUT_SECONDS" "check Cloud deploy support target access" \
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
    print_deploy_diagnostics
    exit 73
  fi
}

check_remote_release_locks() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] verify Cloud release/cleanup locks are absent or proven stale on %s:%s\n' "$SSH_TARGET" "$REMOTE_DEPLOY_DIR"
    return
  fi

  if ! run_with_timeout "$SYNC_TIMEOUT_SECONDS" "check Cloud release and cleanup locks" \
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
  load_dotenv "$REMOTE_DEPLOY_DIR/.env"
  release_lock=$(resolve_managed_lock_file \
    "${DEPLOY_RELEASE_LOCK_FILE:-$CLOUD_RELEASE_LOCK_FILE_DEFAULT}" \
    "$REMOTE_DEPLOY_DIR/.cloud-release.lock")
  cleanup_lock=$(resolve_managed_lock_file \
    "${POST_RELEASE_CLEANUP_LOCK_FILE:-$POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT}" \
    "$REMOTE_DEPLOY_DIR/.post-release-cleanup.lock")
  ensure_managed_lock_available "$release_lock"
  ensure_managed_lock_available "$cleanup_lock"
  printf "Cloud release/cleanup lock precheck passed: release=%s cleanup=%s\n" "$release_lock" "$cleanup_lock"
  exit 0
fi

for legacy_lock_dir in \
  /data/iiot-platform/.locks/cloud-release.lock.d \
  /data/iiot-platform/.locks/deploy-cleanup.lock.d \
  "$REMOTE_DEPLOY_DIR/.cloud-release.lock.d" \
  "$REMOTE_DEPLOY_DIR/.post-release-cleanup.lock.d"
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
    print_deploy_diagnostics
    exit 75
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
MANIFEST="$STAGING_DIR/.cloud-support-manifest.sha256"
ALLOWLIST="$STAGING_DIR/.cloud-support-allowlist.txt"

fail_install() {
  printf 'Cloud deploy support staging validation failed: %s\n' "$*" >&2
  exit 73
}

for command_name in sha256sum sh bash docker install cmp sed grep; do
  command -v "$command_name" >/dev/null 2>&1 || fail_install "required command not found: $command_name"
done
docker compose version >/dev/null 2>&1 || fail_install "docker compose is not available"

[ -f "$DEPLOY_DIR/.env" ] || fail_install "production .env is missing: $DEPLOY_DIR/.env"
[ -r "$DEPLOY_DIR/.env" ] || fail_install "production .env is not readable: $DEPLOY_DIR/.env"
[ -f "$MANIFEST" ] || fail_install "support manifest is missing"
[ -f "$ALLOWLIST" ] || fail_install "support allowlist is missing"

for protected_path in .env certs releases backups; do
  [ ! -e "$STAGING_DIR/$protected_path" ] || fail_install "protected path entered staging: $protected_path"
done

sed 's/^[0-9a-f][0-9a-f]*  //' "$MANIFEST" > "$STAGING_DIR/.cloud-support-manifest-paths.txt"
cmp "$ALLOWLIST" "$STAGING_DIR/.cloud-support-manifest-paths.txt" >/dev/null \
  || fail_install "manifest paths do not match the explicit support allowlist"

(cd "$STAGING_DIR" && sha256sum -c "$MANIFEST")

while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  case "$relative_path" in
    /*|*..*|.env|.env/*|certs|certs/*|releases|releases/*|backups|backups/*)
      fail_install "unsafe or protected support path: $relative_path"
      ;;
  esac
  [ -f "$STAGING_DIR/$relative_path" ] || fail_install "staged support file is missing: $relative_path"

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

cleanup_install_temps() {
  while IFS= read -r relative_path; do
    [ -n "$relative_path" ] || continue
    rm -f "$DEPLOY_DIR/$relative_path.cloud-support-new.$$"
  done < "$ALLOWLIST"
  rm -f "$DEPLOY_DIR/.cloud-support-manifest.sha256.cloud-support-new.$$"
}
trap cleanup_install_temps EXIT HUP INT TERM

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

trap - EXIT HUP INT TERM
cleanup_install_temps
printf 'Cloud deploy support files synchronized and sha256-verified: %s\n' "$DEPLOY_DIR"
printf 'Cloud deploy support manifest installed: %s\n' "$support_manifest_target"
printf 'Cloud deploy support protected paths: untouched .env certs/ releases/ backups/\n'
EOF
  chmod 700 "$package_dir/$SUPPORT_INSTALLER_NAME"
}

sync_remote_deploy_files() {
  local relative_path
  local package_dir
  local remote_dir_quoted
  local remote_command
  local sync_status

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] Cloud deploy support whitelist:\n'
    for relative_path in "${SUPPORT_FILES[@]}"; do
      printf '[dry-run]   %s\n' "$relative_path"
    done
    printf '[dry-run] stage support files on %s:%s/.support-staging\n' "$SSH_TARGET" "$REMOTE_DEPLOY_DIR"
    printf '[dry-run] validate staged sha256 manifest, shell syntax and docker compose config before install\n'
    printf '[dry-run] atomically install whitelist, persist support manifest and verify installed sha256 values\n'
    printf '[dry-run] protected remote paths remain untouched: .env certs/ releases/ backups/\n'
    return
  fi

  package_dir="$(mktemp -d)"
  create_support_package "$package_dir"
  remote_dir_quoted="$(printf '%q' "$REMOTE_DEPLOY_DIR")"
  remote_command="set -eu
deploy_dir=$remote_dir_quoted
staging_parent=\"\$deploy_dir/.support-staging\"
mkdir -p \"\$staging_parent\"
staging_dir=\$(mktemp -d \"\$staging_parent/cloud-support.XXXXXX\")
cleanup_support_staging() {
  rm -rf \"\$staging_dir\"
  rmdir \"\$staging_parent\" 2>/dev/null || true
}
trap cleanup_support_staging EXIT HUP INT TERM
tar --no-same-owner -xf - -C \"\$staging_dir\"
sh \"\$staging_dir/$SUPPORT_INSTALLER_NAME\" \"\$deploy_dir\" \"\$staging_dir\""

  if COPYFILE_DISABLE=1 run_with_timeout "$SYNC_TIMEOUT_SECONDS" "sync and validate Cloud deploy support files" \
    bash -c '
      set -euo pipefail
      package_dir="$1"
      ssh_target="$2"
      connect_timeout="$3"
      remote_command="$4"
      COPYFILE_DISABLE=1 tar -C "$package_dir" -cf - . \
        | ssh -o BatchMode=yes -o "ConnectTimeout=$connect_timeout" "$ssh_target" "$remote_command"
    ' bash "$package_dir" "$SSH_TARGET" "$SSH_CONNECT_TIMEOUT_SECONDS" "$remote_command"; then
    sync_status=0
  else
    sync_status=$?
  fi
  rm -rf "$package_dir"

  if [ "$sync_status" -ne 0 ]; then
    print_deploy_diagnostics
    exit "$sync_status"
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
  sha="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  remote="${GIT_REMOTE:-origin}"
  git -C "$REPO_ROOT" fetch --quiet "$remote" '+refs/heads/*:refs/remotes/'"$remote"'/*'
  if ! git -C "$REPO_ROOT" branch -r --contains "$sha" | grep -q "$remote/"; then
    fail "HEAD $sha is not present on remote $remote. Push to GitHub before production release."
  fi
}

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

require_pushed_clean_head
check_remote_support_file_access
check_remote_release_locks
sync_remote_deploy_files
check_remote_release_locks
"$SCRIPT_DIR/build-and-push.sh" "${BUILD_ARGS[@]}"

SERVICES_FILE="$REPO_ROOT/artifacts/deploy/cloud-built-services.txt"
if [ ! -f "$SERVICES_FILE" ]; then
  fail "Missing built services file: $SERVICES_FILE"
fi
DEPLOY_SERVICES="$(tr -d '\r\n' < "$SERVICES_FILE")"
[ -n "$DEPLOY_SERVICES" ] || fail "Built services file is empty: $SERVICES_FILE"

REMOTE_COMMAND="cd '$REMOTE_DEPLOY_DIR' && DEPLOY_GIT_SHA='${TAG#sha-}' DEPLOY_TRIGGERED_BY=local ./scripts/deploy-release.sh '$TAG' --services '$DEPLOY_SERVICES'"

printf '\nCloud local deploy command:\n'
printf 'ssh'
for argument in "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$REMOTE_COMMAND"; do
  printf ' '
  print_shell_argument "$argument"
done
printf '\n'

if ! run_with_timeout "$SSH_TIMEOUT_SECONDS" "ssh Cloud deploy-release" \
  ssh "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$REMOTE_COMMAND"; then
  print_deploy_diagnostics
  exit 124
fi
