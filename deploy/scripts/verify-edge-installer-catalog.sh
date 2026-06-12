#!/bin/sh
set -eu

BASE_URL=${BASE_URL:-http://10.98.90.154:81}
CHANNEL=${CHANNEL:-stable}
TARGET_RUNTIME=${TARGET_RUNTIME:-win-x64}
EXPECTED_VERSION=${EXPECTED_VERSION:-}

usage() {
  cat <<'USAGE'
Usage:
  BASE_URL=http://10.98.90.154:81 CHANNEL=stable TARGET_RUNTIME=win-x64 EXPECTED_VERSION=1.2.0 \
    ./scripts/verify-edge-installer-catalog.sh

Checks:
  - public latest catalog returns a published host release
  - host version/runtime/channel match the expected values
  - at least one plugin is published in the catalog
  - installer-artifact.json is reachable
  - installer-artifact.json describes v2 launcher/host/plugins directories
  - installer stub URL is reachable and Content-Length matches the manifest when size is present

This script is read-only. It does not call installer-package and does not rotate device secrets.
USAGE
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

command -v curl >/dev/null 2>&1 || {
  printf 'curl is required.\n' >&2
  exit 1
}

command -v python3 >/dev/null 2>&1 || {
  printf 'python3 is required.\n' >&2
  exit 1
}

tmp_dir=$(mktemp -d)
cleanup() {
  rm -rf "$tmp_dir"
}
trap cleanup EXIT HUP INT TERM

catalog_file="$tmp_dir/catalog.json"
artifact_file="$tmp_dir/installer-artifact.json"

api_url="${BASE_URL%/}/api/v1/public/client-downloads/latest?channel=$CHANNEL&targetRuntime=$TARGET_RUNTIME"
printf 'Checking public catalog: %s\n' "$api_url"
curl -fsS "$api_url" -o "$catalog_file"

artifact_url=$(
  CATALOG_FILE="$catalog_file" \
  BASE_URL="$BASE_URL" \
  CHANNEL="$CHANNEL" \
  TARGET_RUNTIME="$TARGET_RUNTIME" \
  EXPECTED_VERSION="$EXPECTED_VERSION" \
  python3 - <<'PY'
import json
import os
import sys

catalog = json.loads(open(os.environ["CATALOG_FILE"], encoding="utf-8").read())
channel = os.environ["CHANNEL"]
target_runtime = os.environ["TARGET_RUNTIME"]
expected_version = os.environ.get("EXPECTED_VERSION", "").strip()

host = catalog.get("latestHost")
if not host:
    raise SystemExit("catalog latestHost is empty")

plugins = catalog.get("plugins") or []
if not plugins:
    raise SystemExit("catalog plugins is empty")

if (host.get("channel") or "").lower() != channel.lower():
    raise SystemExit(f"host channel mismatch: {host.get('channel')} != {channel}")

if (host.get("targetRuntime") or "").lower() != target_runtime.lower():
    raise SystemExit(f"host targetRuntime mismatch: {host.get('targetRuntime')} != {target_runtime}")

if expected_version and host.get("version") != expected_version:
    raise SystemExit(f"host version mismatch: {host.get('version')} != {expected_version}")

download_url = host.get("downloadUrl")
if not download_url:
    download_url = (
        os.environ["BASE_URL"].rstrip("/")
        + f"/edge-updates/installers/{channel}/{host.get('version')}/installer-artifact.json"
    )

print(download_url)
print(
    f"host_version={host.get('version')} plugin_count={len(plugins)} "
    f"plugins={','.join(p.get('moduleId', '') for p in plugins)}",
    file=sys.stderr,
)
PY
)

printf 'Checking installer manifest: %s\n' "$artifact_url"
curl -fsS "$artifact_url" -o "$artifact_file"

ARTIFACT_URL="$artifact_url" \
ARTIFACT_FILE="$artifact_file" \
CATALOG_FILE="$catalog_file" \
CHANNEL="$CHANNEL" \
TARGET_RUNTIME="$TARGET_RUNTIME" \
EXPECTED_VERSION="$EXPECTED_VERSION" \
python3 - <<'PY' >"$tmp_dir/artifact-paths.env"
import json
import os
from urllib.parse import urljoin

artifact_url = os.environ["ARTIFACT_URL"]
artifact = json.loads(open(os.environ["ARTIFACT_FILE"], encoding="utf-8").read())
catalog = json.loads(open(os.environ["CATALOG_FILE"], encoding="utf-8").read())
channel = os.environ["CHANNEL"]
target_runtime = os.environ["TARGET_RUNTIME"]
expected_version = os.environ.get("EXPECTED_VERSION", "").strip()

if (artifact.get("channel") or "").lower() != channel.lower():
    raise SystemExit(f"artifact channel mismatch: {artifact.get('channel')} != {channel}")

if (artifact.get("targetRuntime") or "").lower() != target_runtime.lower():
    raise SystemExit(f"artifact targetRuntime mismatch: {artifact.get('targetRuntime')} != {target_runtime}")

if expected_version and artifact.get("version") != expected_version:
    raise SystemExit(f"artifact version mismatch: {artifact.get('version')} != {expected_version}")

if artifact.get("schemaVersion") != 2:
    raise SystemExit(f"artifact schemaVersion must be 2, actual: {artifact.get('schemaVersion')}")

if not artifact.get("modules"):
    raise SystemExit("artifact modules is empty")

base = artifact_url.rsplit("/", 1)[0] + "/"
stub_file = artifact.get("installerStubFile")
launcher_directory = (artifact.get("launcherDirectory") or "").strip("/")
host_directory = (artifact.get("hostDirectory") or "").strip("/")
plugins_root = (artifact.get("pluginsRoot") or "").strip("/")
if not stub_file or not launcher_directory or not host_directory or not plugins_root:
    raise SystemExit("artifact stub/launcher/host/plugins directory is empty")

for module in artifact.get("modules") or []:
    if not module.get("moduleId") or not module.get("pluginDirectory"):
        raise SystemExit("artifact module plugin mapping is incomplete")

print(f"STUB_URL={urljoin(base, stub_file)}")
print(f"STUB_SIZE={artifact.get('installerStubSize')}")
print(
    "artifact_version=%s launcher=%s host=%s pluginsRoot=%s artifact_modules=%s"
    % (
        artifact.get("version"),
        launcher_directory,
        host_directory,
        plugins_root,
        ",".join(module.get("moduleId", "") for module in artifact.get("modules", [])),
    )
)
PY

STUB_URL=$(awk -F= '/^STUB_URL=/{print $2}' "$tmp_dir/artifact-paths.env")
STUB_SIZE=$(awk -F= '/^STUB_SIZE=/{print $2}' "$tmp_dir/artifact-paths.env")
grep '^artifact_' "$tmp_dir/artifact-paths.env"

check_head_content_length() {
  url=$1
  expected_size=$2
  label=$3
  headers_file="$tmp_dir/$label.headers"
  curl -fsSI "$url" -o "$headers_file"
  actual_size=$(awk -F': ' 'tolower($1) == "content-length" {gsub("\r", "", $2); print $2; exit}' "$headers_file")
  if [ -z "$actual_size" ]; then
    printf '%s HEAD response did not include Content-Length.\n' "$label" >&2
    exit 1
  fi
  if [ -n "$expected_size" ] && [ "$expected_size" != "None" ] && [ "$actual_size" != "$expected_size" ]; then
    printf '%s Content-Length mismatch: %s != %s\n' "$label" "$actual_size" "$expected_size" >&2
    exit 1
  fi
}

printf 'Checking installer stub URL: %s\n' "$STUB_URL"
check_head_content_length "$STUB_URL" "$STUB_SIZE" "installer-stub"

printf 'Edge installer catalog verification passed.\n'
