#!/bin/sh
set -eu

MIRROR_REGISTRY=${MIRROR_REGISTRY:-}
MIRROR_NAMESPACE=${MIRROR_NAMESPACE:-mirror}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

if [ -z "$MIRROR_REGISTRY" ]; then
  fail "MIRROR_REGISTRY is required. Pass the intranet Harbor registry explicitly, for example MIRROR_REGISTRY=<harbor-registry>."
fi
case "$MIRROR_REGISTRY" in
  *.example*|*internal.example*)
    fail "MIRROR_REGISTRY still uses the documentation example domain: $MIRROR_REGISTRY"
    ;;
esac
case "$MIRROR_REGISTRY" in
  *.*|*:*|localhost)
    ;;
  *)
    fail "MIRROR_REGISTRY must include an explicit Harbor registry host, for example harbor.local:5000 or 10.0.0.1:5000: $MIRROR_REGISTRY"
    ;;
esac
case "$MIRROR_NAMESPACE" in
  .|..|*.example*|*internal.example*)
    fail "MIRROR_NAMESPACE must be a single Harbor project/namespace segment using lowercase letters, digits, dot, underscore, or hyphen: $MIRROR_NAMESPACE"
    ;;
esac
if ! printf '%s' "$MIRROR_NAMESPACE" | grep -Eq '^[a-z0-9._-]+$'; then
  fail "MIRROR_NAMESPACE must be a single Harbor project/namespace segment using lowercase letters, digits, dot, underscore, or hyphen: $MIRROR_NAMESPACE"
fi

mirror_image() {
  source_image=$1
  target_name=$2
  target_image="$MIRROR_REGISTRY/$MIRROR_NAMESPACE/$target_name"

  if ! docker image inspect "$source_image" >/dev/null 2>&1; then
    docker pull "$source_image"
  fi

  docker tag "$source_image" "$target_image"
  docker push "$target_image"
  printf 'Mirrored %s -> %s\n' "$source_image" "$target_image"
}

docker version >/dev/null

mirror_image "timescale/timescaledb:latest-pg17" "timescaledb:latest-pg17"
mirror_image "redis:7.4-alpine" "redis:7.4-alpine"
mirror_image "rabbitmq:3-management-alpine" "rabbitmq:3-management-alpine"
mirror_image "datalust/seq:2024.3" "seq:2024.3"
mirror_image "mcr.microsoft.com/dotnet/sdk:10.0.301" "dotnet-sdk:10.0.301"
mirror_image "mcr.microsoft.com/dotnet/aspnet:10.0.9" "dotnet-aspnet:10.0.9"
mirror_image "nginx:1.27-alpine" "nginx:1.27-alpine"
mirror_image "node:22-slim" "node:22-slim"
