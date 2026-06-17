#!/bin/sh
set -eu

MIRROR_REGISTRY=${MIRROR_REGISTRY:-10.98.90.154:80}
MIRROR_NAMESPACE=${MIRROR_NAMESPACE:-mirror}

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
mirror_image "nginx:1.27-alpine" "nginx:1.27-alpine"
mirror_image "node:22-slim" "node:22-slim"
