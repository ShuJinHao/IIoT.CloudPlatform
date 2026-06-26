#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

. "$SCRIPT_DIR/release-common.sh"

RELEASE_TAG=""
DATA_PATH="${POST_RELEASE_DATA_PATH:-/data}"
LOCK_FILE="${POST_RELEASE_CLEANUP_LOCK_FILE:-/data/iiot-platform/.locks/deploy-cleanup.lock}"
CONTAINERD_ROOT="${POST_RELEASE_CONTAINERD_ROOT:-/data/iiot-platform/runtime/containerd}"
HARBOR_GC_ENABLED="${POST_RELEASE_HARBOR_GC_ENABLED:-1}"
HARBOR_GC_REQUIRED="${POST_RELEASE_HARBOR_GC_REQUIRED:-1}"
DRY_RUN=false

usage() {
  cat <<'USAGE' >&2
Usage: post-release-cleanup.sh [--release-tag sha-HEX] [--dry-run]

Runs safe post-release cleanup after Cloud deployment:
- Docker/BuildKit build cache
- local Docker application image tags not referenced by current .env
- Harbor application sha-* tags not referenced by current .env
- Harbor GC trigger or explicit failure
- disk and containerd usage summaries
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --release-tag|--keep-tag)
      shift
      RELEASE_TAG="${1:-}"
      ;;
    --release-tag=*|--keep-tag=*)
      RELEASE_TAG="${1#*=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    -*)
      printf 'Unknown post-release-cleanup option: %s\n' "$1" >&2
      usage
      exit 64
      ;;
    *)
      if [ -n "$RELEASE_TAG" ] && [ "$RELEASE_TAG" != "$1" ]; then
        printf 'Unexpected post-release-cleanup argument: %s\n' "$1" >&2
        exit 64
      fi
      RELEASE_TAG="$1"
      ;;
  esac
  shift
done

if [ -n "$RELEASE_TAG" ]; then
  ensure_release_tag "$RELEASE_TAG"
fi

require_command docker
load_dotenv

cleanup_failed=0
lock_dir=""
declare -a current_refs=()
declare -a app_repositories=()
declare -a harbor_entries=()

append_unique() {
  local value="$1"
  shift
  local existing
  for existing in "$@"; do
    if [ "$existing" = "$value" ]; then
      return 1
    fi
  done
  return 0
}

image_tag_from_ref() {
  local image_ref="$1"
  local last_segment="${image_ref##*/}"
  if [[ "$last_segment" == *:* ]]; then
    printf '%s\n' "${last_segment##*:}"
    return
  fi
  printf '\n'
}

harbor_base_from_registry() {
  local raw_url="$1"
  case "$raw_url" in
    http://*|https://*)
      printf '%s\n' "${raw_url%/}"
      ;;
    *:80|localhost:*|127.*|10.*|192.168.*|172.*)
      printf 'http://%s\n' "${raw_url%/}"
      ;;
    *)
      printf 'https://%s\n' "${raw_url%/}"
      ;;
  esac
}

collect_current_images() {
  local key
  local image_ref
  local repository
  local tag
  local registry
  local path_without_registry
  local project
  local harbor_repository
  local entry

  for key in $APP_IMAGE_KEYS; do
    eval "image_ref=\${$key:-}"
    if [ -z "$image_ref" ]; then
      printf 'Missing application image for cleanup: %s\n' "$key" >&2
      exit 64
    fi

    repository="$(image_repository_from_ref "$image_ref")"
    tag="$(image_tag_from_ref "$image_ref")"
    if [[ ! "$tag" =~ ^sha-[0-9a-f]+$ ]]; then
      printf 'Application image is not a production sha-* tag and will not be used for cleanup: %s=%s\n' "$key" "$image_ref" >&2
      continue
    fi

    registry="${repository%%/*}"
    path_without_registry="${repository#*/}"
    project="${OCI_NAMESPACE:-${HARBOR_PROJECT:-${path_without_registry%%/*}}}"
    if [[ "$path_without_registry" == "$project/"* ]]; then
      harbor_repository="${path_without_registry#"$project/"}"
    else
      harbor_repository="${path_without_registry#*/}"
    fi

    append_unique "$repository:$tag" "${current_refs[@]}" && current_refs+=("$repository:$tag")
    append_unique "$repository" "${app_repositories[@]}" && app_repositories+=("$repository")
    entry="$registry|$project|$harbor_repository|$tag"
    append_unique "$entry" "${harbor_entries[@]}" && harbor_entries+=("$entry")
  done
}

acquire_lock() {
  local lock_parent
  lock_parent="$(dirname "$LOCK_FILE")"
  if ! mkdir -p "$lock_parent" 2>/dev/null; then
    LOCK_FILE="$DEPLOY_DIR/.post-release-cleanup.lock"
    lock_parent="$(dirname "$LOCK_FILE")"
    mkdir -p "$lock_parent"
    printf -- '- Cleanup lock parent was not writable; using deploy-local lock: `%s`\n' "$LOCK_FILE"
  fi

  lock_dir="${LOCK_FILE}.d"
  local attempts=0
  while ! mkdir "$lock_dir" 2>/dev/null; do
    attempts=$((attempts + 1))
    if [ "$attempts" -ge "${POST_RELEASE_CLEANUP_LOCK_ATTEMPTS:-180}" ]; then
      printf 'Could not acquire cleanup lock: %s\n' "$lock_dir" >&2
      exit 75
    fi
    sleep 5
  done

  printf '%s\n' "$$" > "$lock_dir/pid"
  trap release_lock EXIT HUP INT TERM
}

release_lock() {
  if [ -n "$lock_dir" ] && [ -d "$lock_dir" ]; then
    rm -rf "$lock_dir"
  fi
}

disk_percent() {
  df -P "$DATA_PATH" | awk 'NR == 2 { gsub("%", "", $5); print $5 }'
}

print_disk_summary() {
  local label="$1"
  printf '\n#### Disk %s\n\n' "$label"
  printf '```text\n'
  df -h "$DATA_PATH" || true
  printf '```\n'
}

print_docker_summary() {
  local label="$1"
  printf '\n#### Docker %s\n\n' "$label"
  printf '```text\n'
  docker system df || true
  printf '```\n'
}

print_containerd_summary() {
  printf '\n#### containerd summary\n\n'
  if [ ! -d "$CONTAINERD_ROOT" ]; then
    printf -- '- containerd root not found: `%s`\n' "$CONTAINERD_ROOT"
    return
  fi

  printf '```text\n'
  du -sh "$CONTAINERD_ROOT" 2>/dev/null || true
  du -sh "$CONTAINERD_ROOT"/* 2>/dev/null | sort -h || true
  printf '```\n'
  printf -- '- containerd destructive cleanup is skipped by default; set `POST_RELEASE_CONTAINERD_PRUNE=1` only after namespace/ref/lease checks are confirmed.\n'
}

check_initial_disk_guard() {
  local percent
  percent="$(disk_percent)"
  if [ -z "$percent" ]; then
    return
  fi

  if [ "$percent" -ge 90 ]; then
    printf -- '- Disk guard before cleanup: `/data` is `%s%%`; only cleanup/emergency work should continue.\n' "$percent"
  elif [ "$percent" -ge 85 ]; then
    printf -- '- Disk guard before cleanup: `/data` is `%s%%`; cleanup is required before routine deployment continues.\n' "$percent"
  elif [ "$percent" -ge 80 ]; then
    printf -- '- Disk guard before cleanup: `/data` is `%s%%`; warning threshold reached.\n' "$percent"
  else
    printf -- '- Disk guard before cleanup: `/data` is `%s%%`.\n' "$percent"
  fi
}

check_final_disk_guard() {
  local percent
  percent="$(disk_percent)"
  if [ -z "$percent" ]; then
    return
  fi

  if [ "$percent" -ge 85 ]; then
    printf 'Disk remains above cleanup threshold after post-release cleanup: %s%%\n' "$percent" >&2
    cleanup_failed=1
  else
    printf -- '- Disk guard after cleanup: `/data` is `%s%%`.\n' "$percent"
  fi
}

prune_build_cache() {
  printf '\n#### Build cache cleanup\n\n'
  if [ "$DRY_RUN" = "true" ]; then
    printf -- '- dry-run: would run `docker builder prune --all --force`.\n'
    return
  fi

  if docker builder prune --all --force; then
    printf -- '- Docker/BuildKit build cache cleanup completed.\n'
  else
    printf 'Docker/BuildKit build cache cleanup failed.\n' >&2
    cleanup_failed=1
  fi
}

is_current_ref() {
  local candidate="$1"
  local current
  for current in "${current_refs[@]}"; do
    if [ "$candidate" = "$current" ]; then
      return 0
    fi
  done
  return 1
}

is_app_repository() {
  local candidate="$1"
  local repository
  for repository in "${app_repositories[@]}"; do
    if [ "$candidate" = "$repository" ]; then
      return 0
    fi
  done
  return 1
}

prune_local_docker_images() {
  printf '\n#### Local Docker application image cleanup\n\n'
  local image_ref
  local repository
  local tag
  local removed=0

  while IFS= read -r image_ref; do
    [ -n "$image_ref" ] || continue
    case "$image_ref" in
      '<none>:<none>')
        continue
        ;;
    esac

    repository="${image_ref%:*}"
    tag="${image_ref##*:}"
    [[ "$tag" =~ ^sha-[0-9a-f]+$ ]] || continue
    is_app_repository "$repository" || continue
    is_current_ref "$image_ref" && continue

    printf -- '- Removing local old app image tag: `%s`\n' "$image_ref"
    removed=$((removed + 1))
    if [ "$DRY_RUN" != "true" ] && ! docker image rm "$image_ref"; then
      printf 'Failed to remove local image tag: %s\n' "$image_ref" >&2
      cleanup_failed=1
    fi
  done < <(docker image ls --format '{{.Repository}}:{{.Tag}}' | sort -u)

  if [ "$removed" -eq 0 ]; then
    printf -- '- No local old Cloud application image tags found.\n'
  fi
}

run_harbor_retention() {
  printf '\n#### Harbor tag retention\n\n'
  local username="${HARBOR_USERNAME:-${OCI_REGISTRY_USERNAME:-}}"
  local password="${HARBOR_PASSWORD:-${OCI_REGISTRY_PASSWORD:-}}"
  local entry
  local registry
  local project
  local repository
  local keep_tag

  if [ -z "$username" ] || [ -z "$password" ]; then
    printf 'Harbor credentials are required for post-release tag retention.\n' >&2
    cleanup_failed=1
    return
  fi

  for entry in "${harbor_entries[@]}"; do
    IFS='|' read -r registry project repository keep_tag <<< "$entry"
    printf -- '- Harbor `%s/%s/%s`: keep `%s`, delete other `sha-*` tags.\n' "$registry" "$project" "$repository" "$keep_tag"
    if [ "$DRY_RUN" = "true" ]; then
      continue
    fi

    if ! HARBOR_URL="${HARBOR_URL:-$registry}" \
      HARBOR_PROJECT="$project" \
      HARBOR_USERNAME="$username" \
      HARBOR_PASSWORD="$password" \
      "$SCRIPT_DIR/harbor-retention.sh" --keep-tag "$keep_tag" --project "$project" "$repository"; then
      printf 'Harbor retention failed for %s/%s:%s\n' "$project" "$repository" "$keep_tag" >&2
      cleanup_failed=1
    fi
  done
}

trigger_harbor_gc() {
  printf '\n#### Harbor GC\n\n'
  if [ "$HARBOR_GC_ENABLED" = "0" ] || [ "$HARBOR_GC_ENABLED" = "false" ]; then
    printf -- '- Harbor GC trigger skipped by POST_RELEASE_HARBOR_GC_ENABLED.\n'
    return
  fi

  local username="${HARBOR_USERNAME:-${OCI_REGISTRY_USERNAME:-}}"
  local password="${HARBOR_PASSWORD:-${OCI_REGISTRY_PASSWORD:-}}"
  local registry="${HARBOR_URL:-${OCI_REGISTRY:-}}"
  local response_file
  local http_code
  local harbor_base

  if [ -z "$registry" ] && [ "${#harbor_entries[@]}" -gt 0 ]; then
    registry="${harbor_entries[0]%%|*}"
  fi

  if [ -z "$registry" ] || [ -z "$username" ] || [ -z "$password" ]; then
    printf 'Harbor GC requires registry and credentials.\n' >&2
    [ "$HARBOR_GC_REQUIRED" = "0" ] || cleanup_failed=1
    return
  fi

  harbor_base="$(harbor_base_from_registry "$registry")"
  response_file="$(mktemp)"
  http_code="$(curl --silent --show-error \
    --user "$username:$password" \
    --request POST \
    --header 'Content-Type: application/json' \
    --data '{"schedule":{"type":"Manual"},"delete_untagged":true}' \
    --output "$response_file" \
    --write-out '%{http_code}' \
    "$harbor_base/api/v2.0/system/gc/schedule" || true)"

  case "$http_code" in
    2*|409)
      printf -- '- Harbor GC trigger accepted: HTTP %s.\n' "$http_code"
      ;;
    *)
      printf 'Harbor GC trigger failed: HTTP %s\n' "${http_code:-curl-error}" >&2
      sed 's/^/  /' "$response_file" >&2 || true
      [ "$HARBOR_GC_REQUIRED" = "0" ] || cleanup_failed=1
      ;;
  esac
  rm -f "$response_file"
}

printf '### Post-release cleanup\n\n'
printf -- '- Project: `CloudPlatform`\n'
if [ -n "$RELEASE_TAG" ]; then
  printf -- '- Release tag: `%s`\n' "$RELEASE_TAG"
fi
printf -- '- Deploy dir: `%s`\n' "$DEPLOY_DIR"
printf -- '- Data path: `%s`\n' "$DATA_PATH"

collect_current_images
acquire_lock
check_initial_disk_guard
print_disk_summary before
print_docker_summary before
print_containerd_summary
prune_build_cache
prune_local_docker_images
run_harbor_retention
trigger_harbor_gc
print_docker_summary after
print_disk_summary after
check_final_disk_guard

if [ "$cleanup_failed" -ne 0 ]; then
  printf '\nPost-release cleanup completed with failures.\n' >&2
  exit 1
fi

printf '\nPost-release cleanup completed successfully.\n'
