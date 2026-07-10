#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOY_DIR/.." && pwd)"

REGISTRY="${REGISTRY:-}"
HARBOR_PROJECT="${HARBOR_PROJECT:-iiot}"
TAG="${TAG:-sha-$(git -C "$REPO_ROOT" rev-parse HEAD)}"
PLATFORM="${PLATFORM:-linux/amd64}"
BUILD_TIMEOUT_SECONDS="${BUILD_TIMEOUT_SECONDS:-900}"
HARBOR_TIMEOUT_SECONDS="${HARBOR_TIMEOUT_SECONDS:-120}"
DRY_RUN=false
REQUESTED_SERVICES=""
REQUESTED_ALL=false
BUILT_DIGEST_LINES=()

WORKSPACE_INVOCATION_ID="${IIOT_WORKSPACE_DEPLOY_INVOCATION_ID:-standalone}"
WORKSPACE_EXPECTED_SHA="${IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA:-${TAG#sha-}}"
WORKSPACE_PLAN_DIGEST="${IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST:-standalone}"

usage() {
  cat <<'EOF'
Usage:
  REGISTRY=<harbor-registry> deploy/scripts/build-and-push.sh --services httpapi,gateway,dataworker,migration [--dry-run]
  REGISTRY=<harbor-registry> VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge \
    deploy/scripts/build-and-push.sh --services web [--dry-run]
  REGISTRY=<harbor-registry> VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge \
    deploy/scripts/build-and-push.sh --all [--dry-run]

Builds selected Cloud application images locally and pushes them to Harbor.
Production use must pass either --services or --all explicitly.
EOF
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
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
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown build-and-push option: $1"
      ;;
  esac
  shift
done

if [ "$REQUESTED_ALL" = true ] && [ -n "$REQUESTED_SERVICES" ]; then
  fail "Use either --all or --services, not both."
fi
if [ "$REQUESTED_ALL" != true ] && [ -z "$REQUESTED_SERVICES" ]; then
  fail "Cloud local image build requires explicit --services or --all."
fi
require_uint_range BUILD_TIMEOUT_SECONDS "$BUILD_TIMEOUT_SECONDS" 60 7200
require_uint_range HARBOR_TIMEOUT_SECONDS "$HARBOR_TIMEOUT_SECONDS" 10 600
if [ -z "$REGISTRY" ]; then
  fail "REGISTRY is required. Pass the intranet Harbor registry explicitly, for example REGISTRY=<harbor-registry>."
fi
case "$REGISTRY" in
  *.example*|*internal.example*)
    fail "REGISTRY still uses the documentation example domain: $REGISTRY"
    ;;
esac
case "$REGISTRY" in
  *.*|*:*|localhost)
    ;;
  *)
    fail "REGISTRY must include an explicit Harbor registry host, for example harbor.local:5000 or 10.0.0.1:5000: $REGISTRY"
    ;;
esac
if [[ "$HARBOR_PROJECT" == "." || "$HARBOR_PROJECT" == ".." || "$HARBOR_PROJECT" == *.example* || "$HARBOR_PROJECT" == *internal.example* || ! "$HARBOR_PROJECT" =~ ^[a-z0-9._-]+$ ]]; then
  fail "HARBOR_PROJECT must be a single Harbor project segment using lowercase letters, digits, dot, underscore, or hyphen: $HARBOR_PROJECT"
fi

IMAGE_PREFIX="${REGISTRY}/${HARBOR_PROJECT}"
DOTNET_SDK_IMAGE="${DOTNET_SDK_IMAGE:-${REGISTRY}/mirror/dotnet-sdk:10.0.301}"
DOTNET_ASPNET_IMAGE="${DOTNET_ASPNET_IMAGE:-${REGISTRY}/mirror/dotnet-aspnet:10.0.9}"
NODE_BASE_IMAGE="${NODE_BASE_IMAGE:-${REGISTRY}/mirror/node:22-slim}"
NGINX_BASE_IMAGE="${NGINX_BASE_IMAGE:-${REGISTRY}/mirror/nginx:1.27-alpine}"
VITE_AICOPILOT_CHALLENGE_URL="${VITE_AICOPILOT_CHALLENGE_URL:-}"

normalize_services() {
  local services_input="${1:-}"
  local normalized=""
  local service
  local item

  if [ "$REQUESTED_ALL" = true ]; then
    printf '%s\n' "httpapi gateway dataworker migration web"
    return
  fi

  for item in $(printf '%s' "$services_input" | tr ',' ' '); do
    case "$item" in
      httpapi|iiot-httpapi)
        service=httpapi
        ;;
      gateway|iiot-gateway)
        service=gateway
        ;;
      dataworker|iiot-dataworker)
        service=dataworker
        ;;
      migration|iiot-migration|iiot-migrationworkapp)
        service=migration
        ;;
      web|iiot-web)
        service=web
        ;;
      *)
        fail "Unsupported Cloud image service: $item"
        ;;
    esac

    case " $normalized " in
      *" $service "*)
        ;;
      *)
        normalized="$normalized $service"
        ;;
    esac
  done

  normalized="$(printf '%s' "$normalized" | awk '{$1=$1; print}')"
  [ -n "$normalized" ] || fail "No Cloud image services were selected."
  printf '%s\n' "$normalized"
}

service_csv() {
  printf '%s' "$1" | awk '{$1=$1; gsub(/ /, ","); print}'
}

print_diagnostics() {
  local service="${1:-}"
  local image_name="${2:-}"
  cat >&2 <<EOF

Local Cloud image build failed or timed out.
Diagnostics to run before retrying:
  docker buildx ls
  docker system df
  docker images '${IMAGE_PREFIX}/*'
  curl -fsS 'http://${REGISTRY}/api/v2.0/projects/${HARBOR_PROJECT}/repositories/${image_name:-<repository>}/artifacts?with_tag=true&page_size=20'
  git status --short
  git rev-parse HEAD

Context:
  service=${service:-unknown}
  image=${image_name:-unknown}
  tag=$TAG
  timeout_seconds=$BUILD_TIMEOUT_SECONDS
EOF
}

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

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

validate_local_tools() {
  require_command git
  require_command docker
  docker buildx version >/dev/null 2>&1 || fail "docker buildx is not available."
  if [ -n "${HARBOR_USERNAME:-${OCI_REGISTRY_USERNAME:-}}" ] && [ -n "${HARBOR_PASSWORD:-${OCI_REGISTRY_PASSWORD:-}}" ]; then
    local username="${HARBOR_USERNAME:-$OCI_REGISTRY_USERNAME}"
    local password="${HARBOR_PASSWORD:-$OCI_REGISTRY_PASSWORD}"
    if [ "$DRY_RUN" = true ]; then
      printf '[dry-run] docker login %s --username %s --password-stdin\n' "$REGISTRY" "$username"
    else
      printf '%s\n' "$password" | run_with_timeout "$HARBOR_TIMEOUT_SECONDS" "docker login $REGISTRY" \
        docker login "$REGISTRY" --username "$username" --password-stdin
    fi
  else
    printf 'Harbor credentials were not provided; using existing Docker login for %s.\n' "$REGISTRY"
  fi
}

image_name_for_service() {
  case "$1" in
    httpapi) printf '%s\n' iiot-httpapi ;;
    gateway) printf '%s\n' iiot-gateway ;;
    dataworker) printf '%s\n' iiot-dataworker ;;
    migration) printf '%s\n' iiot-migrationworkapp ;;
    web) printf '%s\n' iiot-web ;;
    *) fail "Unsupported Cloud image service: $1" ;;
  esac
}

dockerfile_for_service() {
  case "$1" in
    httpapi) printf '%s\n' src/hosts/IIoT.HttpApi/Dockerfile ;;
    gateway) printf '%s\n' src/hosts/IIoT.Gateway/Dockerfile ;;
    dataworker) printf '%s\n' src/hosts/IIoT.DataWorker/Dockerfile ;;
    migration) printf '%s\n' src/hosts/IIoT.MigrationWorkApp/Dockerfile ;;
    web) printf '%s\n' src/hosts/IIoT.AppHost/iiot-web.Dockerfile ;;
    *) fail "Unsupported Cloud image service: $1" ;;
  esac
}

env_key_for_service() {
  case "$1" in
    httpapi) printf '%s\n' IIOT_HTTPAPI_IMAGE ;;
    gateway) printf '%s\n' IIOT_GATEWAY_IMAGE ;;
    dataworker) printf '%s\n' IIOT_DATAWORKER_IMAGE ;;
    migration) printf '%s\n' IIOT_MIGRATION_IMAGE ;;
    web) printf '%s\n' IIOT_WEB_IMAGE ;;
    *) fail "Unsupported Cloud image service: $1" ;;
  esac
}

build_and_push_service() {
  local service="$1"
  local image_name
  local dockerfile
  local image
  local artifact_dir
  local metadata_file
  local image_digest
  image_name="$(image_name_for_service "$service")"
  dockerfile="$(dockerfile_for_service "$service")"
  image="$IMAGE_PREFIX/$image_name:$TAG"
  artifact_dir="${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}"
  mkdir -p "$artifact_dir/build-metadata"
  metadata_file="$artifact_dir/build-metadata/$service.json"

  printf 'Building Cloud image: service=%s image=%s\n' "$service" "$image"
  if run_with_timeout "$BUILD_TIMEOUT_SECONDS" "build/push $service" \
    docker buildx build \
      --platform "$PLATFORM" \
      --push \
      --file "$REPO_ROOT/$dockerfile" \
      --build-arg "DOTNET_SDK_IMAGE=$DOTNET_SDK_IMAGE" \
      --build-arg "DOTNET_ASPNET_IMAGE=$DOTNET_ASPNET_IMAGE" \
      --build-arg "VITE_AICOPILOT_CHALLENGE_URL=$VITE_AICOPILOT_CHALLENGE_URL" \
      --build-arg "NODE_BASE_IMAGE=$NODE_BASE_IMAGE" \
      --build-arg "NGINX_BASE_IMAGE=$NGINX_BASE_IMAGE" \
      --metadata-file "$metadata_file" \
      --tag "$image" \
      "$REPO_ROOT"; then
    local build_status=0
  else
    local build_status=$?
  fi
  if [ "$build_status" -ne 0 ]; then
    if [ "$build_status" -eq 124 ]; then
      printf 'Cloud image build timed out after %s seconds: service=%s\n' "$BUILD_TIMEOUT_SECONDS" "$service" >&2
    else
      printf 'Cloud image build failed with exit code %s: service=%s\n' "$build_status" "$service" >&2
    fi
    print_diagnostics "$service" "$image_name"
    exit "$build_status"
  fi

  if [ "$DRY_RUN" = true ]; then
    image_digest=dry-run
  else
    [ -s "$metadata_file" ] || fail "Cloud build did not write image metadata: $metadata_file"
    image_digest="$(tr -d '\r\n' < "$metadata_file" | sed -n 's/.*"containerimage.digest"[[:space:]]*:[[:space:]]*"\(sha256:[0-9a-f]\{64\}\)".*/\1/p')"
    [[ "$image_digest" =~ ^sha256:[0-9a-f]{64}$ ]] \
      || fail "Cloud build metadata does not contain a valid OCI digest: service=$service metadata=$metadata_file"
  fi
  BUILT_DIGEST_LINES+=("$(env_key_for_service "$service")_DIGEST=$image_digest")
}

emit_outputs() {
  local services="$1"
  local services_csv="$2"
  local artifact_dir="${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}"
  local services_file="$artifact_dir/cloud-built-services.txt"
  local images_file="$artifact_dir/cloud-images.env"
  mkdir -p "$artifact_dir"
  printf '%s\n' "$services_csv" > "$services_file"
  : > "$images_file"

  {
    printf 'CLOUD_DEPLOY_INVOCATION_ID=%s\n' "$WORKSPACE_INVOCATION_ID"
    printf 'CLOUD_DEPLOY_EXPECTED_SHA=%s\n' "$WORKSPACE_EXPECTED_SHA"
    printf 'CLOUD_DEPLOY_PLAN_DIGEST=%s\n' "$WORKSPACE_PLAN_DIGEST"
    printf 'CLOUD_DEPLOY_RELEASE_TAG=%s\n' "$TAG"
    printf 'CLOUD_DEPLOY_SERVICES=%s\n' "$services_csv"
  } >> "$images_file"

  printf '\nRelease tag: %s\n' "$TAG"
  printf 'Deploy services input: %s\n' "$services_csv"
  for service in $services; do
    local key
    local image_name
    key="$(env_key_for_service "$service")"
    image_name="$(image_name_for_service "$service")"
    printf '%s=%s/%s:%s\n' "$key" "$IMAGE_PREFIX" "$image_name" "$TAG" | tee -a "$images_file"
  done
  for digest_line in "${BUILT_DIGEST_LINES[@]}"; do
    printf '%s\n' "$digest_line" | tee -a "$images_file"
  done
  printf 'Built services file: %s\n' "$services_file"
  printf 'Image manifest file: %s\n' "$images_file"
}

SELECTED_SERVICES="$(normalize_services "$REQUESTED_SERVICES")"
SELECTED_SERVICES_CSV="$(service_csv "$SELECTED_SERVICES")"

case " $SELECTED_SERVICES " in
  *" web "*)
    if [ -z "$VITE_AICOPILOT_CHALLENGE_URL" ]; then
      fail "VITE_AICOPILOT_CHALLENGE_URL is required for web image builds. Pass the browser-reachable AICopilot challenge URL explicitly, for example VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge."
    fi
    case "$VITE_AICOPILOT_CHALLENGE_URL" in
      *.example*|*internal.example*)
        fail "VITE_AICOPILOT_CHALLENGE_URL still uses the documentation example domain."
        ;;
    esac
    ;;
esac

validate_local_tools

for service in $SELECTED_SERVICES; do
  build_and_push_service "$service"
done

emit_outputs "$SELECTED_SERVICES" "$SELECTED_SERVICES_CSV"
