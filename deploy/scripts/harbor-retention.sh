#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE' >&2
Usage: harbor-retention.sh [--dry-run] [--keep N] [--keep-tag sha-HEX] [--project PROJECT] REPOSITORY...

Deletes old Harbor sha-* tags for application image repositories.
Required env:
  HARBOR_URL or OCI_REGISTRY
  HARBOR_PROJECT or OCI_NAMESPACE
  HARBOR_USERNAME or OCI_REGISTRY_USERNAME
  HARBOR_PASSWORD or OCI_REGISTRY_PASSWORD
USAGE
}

KEEP="${HARBOR_KEEP_SHA_TAGS:-3}"
KEEP_TAG="${HARBOR_KEEP_SHA_TAG:-}"
PROJECT="${HARBOR_PROJECT:-${OCI_NAMESPACE:-}}"
DRY_RUN=false
REPOSITORIES=()

while [ "$#" -gt 0 ]; do
  case "$1" in
    --dry-run)
      DRY_RUN=true
      ;;
    --keep)
      shift
      KEEP="${1:-}"
      ;;
    --keep=*)
      KEEP="${1#--keep=}"
      ;;
    --keep-tag)
      shift
      KEEP_TAG="${1:-}"
      ;;
    --keep-tag=*)
      KEEP_TAG="${1#--keep-tag=}"
      ;;
    --project)
      shift
      PROJECT="${1:-}"
      ;;
    --project=*)
      PROJECT="${1#--project=}"
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    -*)
      printf 'Unknown option: %s\n' "$1" >&2
      usage
      exit 64
      ;;
    *)
      REPOSITORIES+=("$1")
      ;;
  esac
  shift
done

if [ "${#REPOSITORIES[@]}" -eq 0 ] && [ -n "${HARBOR_REPOSITORIES:-}" ]; then
  IFS=', ' read -r -a REPOSITORIES <<< "$HARBOR_REPOSITORIES"
fi

RAW_URL="${HARBOR_URL:-${OCI_REGISTRY:-}}"
USERNAME="${HARBOR_USERNAME:-${OCI_REGISTRY_USERNAME:-}}"
PASSWORD="${HARBOR_PASSWORD:-${OCI_REGISTRY_PASSWORD:-}}"

if [ -z "$RAW_URL" ] || [ -z "$PROJECT" ] || [ -z "$USERNAME" ] || [ -z "$PASSWORD" ]; then
  printf 'Missing Harbor retention configuration.\n' >&2
  usage
  exit 64
fi
if [[ "$PROJECT" == "." || "$PROJECT" == ".." || "$PROJECT" == *.example* || "$PROJECT" == *internal.example* || ! "$PROJECT" =~ ^[a-z0-9._-]+$ ]]; then
  printf 'HARBOR_PROJECT/OCI_NAMESPACE must be a single Harbor project segment using lowercase letters, digits, dot, underscore, or hyphen: %s\n' "$PROJECT" >&2
  exit 64
fi

if ! [[ "$KEEP" =~ ^[0-9]+$ ]] \
  || ! awk -v value="$KEEP" 'BEGIN { exit !(value >= 1 && value <= 1000) }'; then
  printf 'HARBOR_KEEP_SHA_TAGS must be a decimal integer in range 1..1000.\n' >&2
  exit 64
fi

if [ -n "$KEEP_TAG" ] && ! [[ "$KEEP_TAG" =~ ^sha-[0-9a-f]+$ ]]; then
  printf 'HARBOR_KEEP_SHA_TAG must match sha-<hex>: %s\n' "$KEEP_TAG" >&2
  exit 64
fi

if [ "${#REPOSITORIES[@]}" -eq 0 ]; then
  printf 'At least one Harbor repository is required.\n' >&2
  usage
  exit 64
fi

case "$RAW_URL" in
  http://*|https://*)
    HARBOR_BASE="${RAW_URL%/}"
    ;;
  *:80|localhost:*|127.*|10.*|192.168.*|172.*)
    HARBOR_BASE="http://${RAW_URL%/}"
    ;;
  *)
    HARBOR_BASE="https://${RAW_URL%/}"
    ;;
esac

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'Required command not found: %s\n' "$1" >&2
    exit 127
  }
}

urlencode() {
  python3 - "$1" <<'PY'
import sys
import urllib.parse
print(urllib.parse.quote(sys.argv[1], safe=""))
PY
}

require_command curl
require_command python3

PROJECT_PATH="$(urlencode "$PROJECT")"
API="$HARBOR_BASE/api/v2.0"
PAGE_SIZE=100

collect_delete_candidates() {
  local page_dir="$1"
  python3 - "$KEEP" "$KEEP_TAG" "$page_dir" <<'PY'
import json
import os
import re
import sys

keep = int(sys.argv[1])
keep_tag = sys.argv[2]
page_dir = sys.argv[3]
pattern = re.compile(r"^sha-[0-9a-f]+$")
seen = {}

for name in sorted(os.listdir(page_dir)):
    if not name.endswith(".json"):
        continue
    with open(os.path.join(page_dir, name), encoding="utf-8") as handle:
        artifacts = json.load(handle)
    for artifact in artifacts:
        digest = artifact.get("digest") or ""
        artifact_time = artifact.get("push_time") or ""
        for tag in artifact.get("tags") or []:
            tag_name = tag.get("name") or ""
            if not pattern.fullmatch(tag_name):
                continue
            pushed = tag.get("push_time") or artifact_time
            current = seen.get(tag_name)
            candidate = (pushed, digest, tag_name)
            if current is None or candidate > current:
                seen[tag_name] = candidate

ordered = sorted(seen.values(), key=lambda item: (item[0], item[2]), reverse=True)
if keep_tag:
    candidates = [item for item in ordered if item[2] != keep_tag]
else:
    candidates = ordered[keep:]

for pushed, digest, tag_name in candidates:
    print(f"{tag_name}\t{digest}\t{pushed}")
PY
}

for repository in "${REPOSITORIES[@]}"; do
  [ -n "$repository" ] || continue
  repo_path="$(urlencode "$repository")"
  tmp_dir="$(mktemp -d)"
  trap 'rm -rf "$tmp_dir"' EXIT

  page=1
  while true; do
    page_file="$tmp_dir/page-$page.json"
    curl --fail --silent --show-error \
      --user "$USERNAME:$PASSWORD" \
      "$API/projects/$PROJECT_PATH/repositories/$repo_path/artifacts?with_tag=true&page_size=$PAGE_SIZE&page=$page&sort=-push_time" \
      --output "$page_file"

    count="$(python3 - "$page_file" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as handle:
    print(len(json.load(handle)))
PY
)"
    if ! [[ "$count" =~ ^[0-9]+$ ]] \
      || ! awk -v value="$count" -v maximum="$PAGE_SIZE" 'BEGIN { exit !(value >= 0 && value <= maximum) }'; then
      printf 'Harbor artifact page returned an invalid item count.\n' >&2
      exit 65
    fi
    [ "$count" -ge "$PAGE_SIZE" ] || break
    if [ "$page" -ge 10000 ]; then
      printf 'Harbor artifact pagination exceeded the safe page limit.\n' >&2
      exit 65
    fi
    page=$((page + 1))
  done

  mapfile -t candidates < <(collect_delete_candidates "$tmp_dir")
  if [ "${#candidates[@]}" -eq 0 ]; then
    if [ -n "$KEEP_TAG" ]; then
      printf 'Harbor retention: %s/%s has no sha-* tags outside keep-tag=%s.\n' "$PROJECT" "$repository" "$KEEP_TAG"
    else
      printf 'Harbor retention: %s/%s has no old sha-* tags beyond keep=%s.\n' "$PROJECT" "$repository" "$KEEP"
    fi
    rm -rf "$tmp_dir"
    trap - EXIT
    continue
  fi

  for candidate in "${candidates[@]}"; do
    IFS=$'\t' read -r tag digest pushed <<< "$candidate"
    printf 'Harbor retention: deleting %s/%s:%s pushed=%s\n' "$PROJECT" "$repository" "$tag" "$pushed"
    if [ "$DRY_RUN" = "true" ]; then
      continue
    fi

    digest_path="$(urlencode "$digest")"
    tag_path="$(urlencode "$tag")"
    curl --fail --silent --show-error \
      --user "$USERNAME:$PASSWORD" \
      --request DELETE \
      "$API/projects/$PROJECT_PATH/repositories/$repo_path/artifacts/$digest_path/tags/$tag_path" \
      --output /dev/null
  done

  rm -rf "$tmp_dir"
  trap - EXIT
done

printf 'Harbor retention completed. Harbor GC must run on the registry schedule to reclaim disk space.\n'
