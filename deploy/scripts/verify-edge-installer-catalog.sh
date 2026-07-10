#!/bin/sh
set -eu

BASE_URL=${BASE_URL:-}
CHANNEL=${CHANNEL:-stable}
TARGET_RUNTIME=${TARGET_RUNTIME:-win-x64}
EXPECTED_VERSION=${EXPECTED_VERSION:-}
EXPECTED_PLUGIN_MODULE_ID=${EXPECTED_PLUGIN_MODULE_ID:-}
EXPECTED_PLUGIN_VERSION=${EXPECTED_PLUGIN_VERSION:-}

usage() {
  cat <<'USAGE'
Usage:
  BASE_URL=http://<cloud-host>:81 CHANNEL=stable TARGET_RUNTIME=win-x64 EXPECTED_VERSION=1.2.0 \
    EXPECTED_PLUGIN_MODULE_ID=LaserCutting EXPECTED_PLUGIN_VERSION=1.2.0 \
    ./scripts/verify-edge-installer-catalog.sh

Checks:
  - public catalog v2 returns a published host version
  - host version/runtime/channel match the expected values
  - at least one plugin version is published in the catalog
  - optional EXPECTED_PLUGIN_MODULE_ID + EXPECTED_PLUGIN_VERSION match a published plugin version
  - installer-artifact.json is reachable
  - installer-artifact.json describes v2 launcher/host/plugins directories
  - installer stub URL is reachable and Content-Length matches the manifest when size is present
  - installer stub can be downloaded with HTTP 200 and non-zero bytes
  - Velopack RELEASES and channel manifests are reachable
  - at least one Velopack .nupkg referenced by the manifests is downloadable

This script is read-only. It does not call installer-package and does not rotate device secrets.
USAGE
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

if [ -z "$BASE_URL" ]; then
  printf 'BASE_URL is required. Pass the intranet Cloud Gateway origin explicitly, for example BASE_URL=http://<cloud-host>:81.\n' >&2
  exit 64
fi
case "$BASE_URL" in
  *.example*|*internal.example*)
    printf 'BASE_URL still uses the documentation example domain: %s\n' "$BASE_URL" >&2
    exit 64
    ;;
esac

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
  EXPECTED_PLUGIN_MODULE_ID="$EXPECTED_PLUGIN_MODULE_ID" \
  EXPECTED_PLUGIN_VERSION="$EXPECTED_PLUGIN_VERSION" \
  python3 - <<'PY'
import json
import os
import sys
from urllib.parse import urljoin

catalog = json.loads(open(os.environ["CATALOG_FILE"], encoding="utf-8").read())
channel = os.environ["CHANNEL"]
target_runtime = os.environ["TARGET_RUNTIME"]
expected_version = os.environ.get("EXPECTED_VERSION", "").strip()
expected_plugin_module_id = os.environ.get("EXPECTED_PLUGIN_MODULE_ID", "").strip()
expected_plugin_version = os.environ.get("EXPECTED_PLUGIN_VERSION", "").strip()
base_url = os.environ["BASE_URL"].rstrip("/") + "/"

if catalog.get("catalogSchemaVersion") != 2:
    raise SystemExit(f"catalogSchemaVersion must be 2, actual: {catalog.get('catalogSchemaVersion')}")

host_component = catalog.get("host") or {}
host_versions = host_component.get("versions") or []
if not host_versions:
    raise SystemExit("catalog host.versions is empty")

host = host_versions[0]

plugin_components = catalog.get("plugins") or []
plugin_versions = [
    (component.get("moduleId", ""), version)
    for component in plugin_components
    for version in (component.get("versions") or [])
]
if not plugin_versions:
    raise SystemExit("catalog plugin versions is empty")

if bool(expected_plugin_module_id) != bool(expected_plugin_version):
    raise SystemExit("EXPECTED_PLUGIN_MODULE_ID and EXPECTED_PLUGIN_VERSION must be provided together")

if expected_plugin_module_id:
    matching_plugin_versions = [
        version
        for module_id, version in plugin_versions
        if module_id == expected_plugin_module_id
    ]
    if not matching_plugin_versions:
        raise SystemExit(f"expected plugin module not found in catalog: {expected_plugin_module_id}")
    if not any(version.get("version") == expected_plugin_version for version in matching_plugin_versions):
        actual_versions = ",".join(
            str(version.get("version"))
            for version in matching_plugin_versions
            if version.get("version")
        )
        raise SystemExit(
            f"expected plugin version mismatch for {expected_plugin_module_id}: "
            f"{actual_versions or '<none>'} != {expected_plugin_version}"
        )

if (host.get("channel") or "").lower() != channel.lower():
    raise SystemExit(f"host channel mismatch: {host.get('channel')} != {channel}")

if (host.get("targetRuntime") or "").lower() != target_runtime.lower():
    raise SystemExit(f"host targetRuntime mismatch: {host.get('targetRuntime')} != {target_runtime}")

if expected_version and host.get("version") != expected_version:
    raise SystemExit(f"host version mismatch: {host.get('version')} != {expected_version}")

download_url = host.get("downloadUrl")
if not download_url:
    download_url = f"/edge-updates/installers/{channel}/{host.get('version')}/installer-artifact.json"

print(urljoin(base_url, download_url))
print(
    f"host_version={host.get('version')} plugin_version_count={len(plugin_versions)} "
    f"plugins={','.join(module_id for module_id, _ in plugin_versions)}"
    + (
        f" expected_plugin={expected_plugin_module_id}:{expected_plugin_version}"
        if expected_plugin_module_id
        else ""
    ),
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
EXPECTED_PLUGIN_MODULE_ID="$EXPECTED_PLUGIN_MODULE_ID" \
EXPECTED_PLUGIN_VERSION="$EXPECTED_PLUGIN_VERSION" \
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
expected_plugin_module_id = os.environ.get("EXPECTED_PLUGIN_MODULE_ID", "").strip()
expected_plugin_version = os.environ.get("EXPECTED_PLUGIN_VERSION", "").strip()

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

if expected_plugin_module_id:
    artifact_modules = [
        module
        for module in artifact.get("modules") or []
        if module.get("moduleId") == expected_plugin_module_id
    ]
    if not artifact_modules:
        raise SystemExit(f"expected plugin module not found in installer artifact: {expected_plugin_module_id}")
    if not any(module.get("version") == expected_plugin_version for module in artifact_modules):
        actual_versions = ",".join(
            str(module.get("version"))
            for module in artifact_modules
            if module.get("version")
        )
        raise SystemExit(
            f"expected plugin version mismatch in installer artifact for {expected_plugin_module_id}: "
            f"{actual_versions or '<none>'} != {expected_plugin_version}"
        )

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

check_get_download() {
  url=$1
  expected_size=$2
  label=$3
  result=$(curl -fsSL --output /dev/null --write-out '%{http_code} %{size_download}' "$url")
  status=$(printf '%s' "$result" | awk '{print $1}')
  actual_size=$(printf '%s' "$result" | awk '{print $2}')
  if [ "$status" != "200" ]; then
    printf '%s GET returned HTTP %s.\n' "$label" "$status" >&2
    exit 1
  fi
  case "$actual_size" in
    ''|*[!0-9]*)
      printf '%s GET returned an invalid byte count.\n' "$label" >&2
      exit 1
      ;;
  esac
  if ! awk -v value="$actual_size" 'BEGIN { exit !(value > 0 && value <= 1099511627776) }'; then
    printf '%s GET returned no bytes.\n' "$label" >&2
    exit 1
  fi
  if [ -n "$expected_size" ] && [ "$expected_size" != "None" ] && [ "$actual_size" != "$expected_size" ]; then
    printf '%s download size mismatch: %s != %s\n' "$label" "$actual_size" "$expected_size" >&2
    exit 1
  fi
}

printf 'Checking installer stub URL: %s\n' "$STUB_URL"
check_head_content_length "$STUB_URL" "$STUB_SIZE" "installer-stub"
check_get_download "$STUB_URL" "$STUB_SIZE" "installer-stub"

velopack_base_url="${BASE_URL%/}/edge-updates/velopack/$CHANNEL"
velopack_releases_file="$tmp_dir/velopack-RELEASES"
velopack_releases_json_file="$tmp_dir/velopack-releases.json"
velopack_assets_json_file="$tmp_dir/velopack-assets.json"

printf 'Checking Velopack RELEASES: %s\n' "$velopack_base_url/RELEASES"
curl -fsS "$velopack_base_url/RELEASES" -o "$velopack_releases_file"
if [ ! -s "$velopack_releases_file" ]; then
  printf 'Velopack RELEASES is empty.\n' >&2
  exit 1
fi

printf 'Checking Velopack releases manifest: %s\n' "$velopack_base_url/releases.$CHANNEL.json"
curl -fsS "$velopack_base_url/releases.$CHANNEL.json" -o "$velopack_releases_json_file"

printf 'Checking Velopack assets manifest: %s\n' "$velopack_base_url/assets.$CHANNEL.json"
curl -fsS "$velopack_base_url/assets.$CHANNEL.json" -o "$velopack_assets_json_file"

velopack_nupkg_url=$(
  VELOPACK_BASE_URL="$velopack_base_url/" \
  VELOPACK_RELEASES_FILE="$velopack_releases_file" \
  VELOPACK_RELEASES_JSON_FILE="$velopack_releases_json_file" \
  VELOPACK_ASSETS_JSON_FILE="$velopack_assets_json_file" \
  python3 - <<'PY'
import json
import os
import re
from urllib.parse import urljoin

base_url = os.environ["VELOPACK_BASE_URL"]
releases_file = os.environ["VELOPACK_RELEASES_FILE"]
json_files = [
    os.environ["VELOPACK_RELEASES_JSON_FILE"],
    os.environ["VELOPACK_ASSETS_JSON_FILE"],
]

def normalize(candidate: str) -> str:
    candidate = candidate.strip().strip('"').strip("'")
    if not candidate:
        return ""
    if candidate.startswith("http://") or candidate.startswith("https://"):
        return candidate
    return urljoin(base_url, candidate.lstrip("/"))

def from_json(value):
    if isinstance(value, str):
        if value.lower().endswith(".nupkg"):
            yield value
        return
    if isinstance(value, list):
        for item in value:
            yield from from_json(item)
        return
    if isinstance(value, dict):
        for item in value.values():
            yield from from_json(item)

with open(releases_file, encoding="utf-8") as handle:
    for token in re.findall(r"[A-Za-z0-9._~:/?#\[\]@!$&'()*+,;=%-]+\.nupkg", handle.read()):
        url = normalize(token)
        if url:
            print(url)
            raise SystemExit(0)

for path in json_files:
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    for candidate in from_json(data):
        url = normalize(candidate)
        if url:
            print(url)
            raise SystemExit(0)

raise SystemExit("No Velopack .nupkg URL found in RELEASES or channel manifests.")
PY
)

printf 'Checking Velopack package URL: %s\n' "$velopack_nupkg_url"
check_head_content_length "$velopack_nupkg_url" "" "velopack-nupkg"
check_get_download "$velopack_nupkg_url" "" "velopack-nupkg"

printf 'Edge installer catalog verification passed.\n'
