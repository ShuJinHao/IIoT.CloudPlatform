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

usage() {
  cat <<'EOF'
Usage:
  deploy/scripts/local-release.sh --services httpapi,web --ssh-target root@10.98.90.154
  deploy/scripts/local-release.sh --all --ssh-target root@10.98.90.154

Builds selected Cloud images locally, pushes Harbor tags, then SSH-triggers
the server-side deploy/scripts/deploy-release.sh entrypoint.
EOF
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
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
    printf ' %q' "$@"
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
"$SCRIPT_DIR/build-and-push.sh" "${BUILD_ARGS[@]}"

SERVICES_FILE="$REPO_ROOT/artifacts/deploy/cloud-built-services.txt"
if [ ! -f "$SERVICES_FILE" ]; then
  fail "Missing built services file: $SERVICES_FILE"
fi
DEPLOY_SERVICES="$(tr -d '\r\n' < "$SERVICES_FILE")"
[ -n "$DEPLOY_SERVICES" ] || fail "Built services file is empty: $SERVICES_FILE"

REMOTE_COMMAND="cd '$REMOTE_DEPLOY_DIR' && DEPLOY_GIT_SHA='${TAG#sha-}' DEPLOY_TRIGGERED_BY=local ./scripts/deploy-release.sh '$TAG' --services '$DEPLOY_SERVICES'"

printf '\nCloud local deploy command:\n'
printf 'ssh %s %q\n' "$SSH_TARGET" "$REMOTE_COMMAND"

if ! run_with_timeout "$SSH_TIMEOUT_SECONDS" "ssh Cloud deploy-release" \
  ssh "$SSH_TARGET" "$REMOTE_COMMAND"; then
  print_deploy_diagnostics
  exit 124
fi
