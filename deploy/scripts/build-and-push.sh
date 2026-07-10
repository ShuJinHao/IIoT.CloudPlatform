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
  image_name="$(image_name_for_service "$service")"
  dockerfile="$(dockerfile_for_service "$service")"
  image="$IMAGE_PREFIX/$image_name:$TAG"

  printf 'Building Cloud image: service=%s image=%s\n' "$service" "$image"
  if ! run_with_timeout "$BUILD_TIMEOUT_SECONDS" "build/push $service" \
    docker buildx build \
      --platform "$PLATFORM" \
      --push \
      --file "$REPO_ROOT/$dockerfile" \
      --build-arg "DOTNET_SDK_IMAGE=$DOTNET_SDK_IMAGE" \
      --build-arg "DOTNET_ASPNET_IMAGE=$DOTNET_ASPNET_IMAGE" \
      --build-arg "VITE_AICOPILOT_CHALLENGE_URL=$VITE_AICOPILOT_CHALLENGE_URL" \
      --build-arg "NODE_BASE_IMAGE=$NODE_BASE_IMAGE" \
      --build-arg "NGINX_BASE_IMAGE=$NGINX_BASE_IMAGE" \
      --tag "$image" \
      "$REPO_ROOT"; then
    print_diagnostics "$service" "$image_name"
    exit 124
  fi
}

emit_outputs() {
  local services="$1"
  local services_csv="$2"
  local artifact_dir="$REPO_ROOT/artifacts/deploy"
  local services_file="$artifact_dir/cloud-built-services.txt"
  local images_file="$artifact_dir/cloud-images.env"
  mkdir -p "$artifact_dir"
  printf '%s\n' "$services_csv" > "$services_file"
  : > "$images_file"

  printf '\nRelease tag: %s\n' "$TAG"
  printf 'Deploy services input: %s\n' "$services_csv"
  for service in $services; do
    local key
    local image_name
    key="$(env_key_for_service "$service")"
    image_name="$(image_name_for_service "$service")"
    printf '%s=%s/%s:%s\n' "$key" "$IMAGE_PREFIX" "$image_name" "$TAG" | tee -a "$images_file"
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
