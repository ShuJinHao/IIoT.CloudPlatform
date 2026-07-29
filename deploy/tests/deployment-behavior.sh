#!/usr/bin/env bash
set -euo pipefail

TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$TEST_DIR/.." && pwd)"
REPO_ROOT="$(cd "$DEPLOY_DIR/.." && pwd)"
LOCAL_RELEASE="$DEPLOY_DIR/scripts/local-release.sh"
EXPECTED_SHA=1111111111111111111111111111111111111111
OTHER_SHA=2222222222222222222222222222222222222222
PLAN_DIGEST=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
PROFILE_DIGEST=dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd
OCI_DIGEST=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
SUPPORT_DIGEST=cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc
PASS_COUNT=0

ROOT_TMP="$(mktemp -d "${TMPDIR:-/tmp}/cloud-deploy-behavior.XXXXXX")"
ROOT_TMP="$(cd "$ROOT_TMP" && pwd -P)"
trap 'rm -rf "$ROOT_TMP"' EXIT HUP INT TERM

make_plan() {
  local path="$1"
  local sha="$2"
  local services="$3"
  cat >"$path" <<EOF
{"schemaVersion":1,"target":"Cloud","fullSha":"$sha","services":["$services"],"all":false,"profileDigest":"$PROFILE_DIGEST"}
EOF
  shasum -a 256 "$path" | awk '{print $1}'
}

MISMATCH_PLAN="$ROOT_TMP/mismatch-plan.json"
MISMATCH_PLAN_DIGEST="$(make_plan "$MISMATCH_PLAN" "$OTHER_SHA" httpapi)"
HTTPAPI_PLAN="$ROOT_TMP/httpapi-plan.json"
HTTPAPI_PLAN_DIGEST="$(make_plan "$HTTPAPI_PLAN" "$EXPECTED_SHA" httpapi)"

pass() {
  PASS_COUNT=$((PASS_COUNT + 1))
  printf 'ok %s - %s\n' "$PASS_COUNT" "$1"
}

expect_failure() {
  local label="$1"
  local expected="$2"
  shift 2
  local output_file="$ROOT_TMP/output-$PASS_COUNT.log"
  if "$@" >"$output_file" 2>&1; then
    printf 'Expected failure: %s\n' "$label" >&2
    sed -n '1,160p' "$output_file" >&2
    exit 1
  fi
  if ! grep -Fq "$expected" "$output_file"; then
    printf 'Failure did not contain expected text: %s\n' "$expected" >&2
    sed -n '1,160p' "$output_file" >&2
    exit 1
  fi
  pass "$label"
}

FAKE_BIN="$ROOT_TMP/bin"
mkdir -p "$FAKE_BIN"
cat >"$FAKE_BIN/git" <<EOF
#!/usr/bin/env bash
case "\$*" in
  *"rev-parse HEAD"*) printf '%s\\n' "$EXPECTED_SHA" ;;
  *"rev-parse --verify refs/remotes/origin/main"*) printf '%s\\n' "\${FAKE_REMOTE_SHA:-$EXPECTED_SHA}" ;;
  *"status --porcelain"*) exit 0 ;;
  *"status --short"*) exit 0 ;;
  *" fetch "*) exit 0 ;;
  *"branch -r --contains"*) printf '%s\\n' '  origin/main' ;;
  *) exit 0 ;;
esac
EOF
cat >"$FAKE_BIN/docker" <<'EOF'
#!/usr/bin/env bash
if [ "${1:-}" = buildx ] && [ "${2:-}" = version ]; then
  exit 0
fi
if [ "${1:-}" = buildx ] && [ "${2:-}" = build ]; then
  if [ "${FAKE_BUILD_FAIL:-0}" = 1 ]; then
    exit 23
  fi
  metadata_file=
  while [ "$#" -gt 0 ]; do
    if [ "$1" = --metadata-file ]; then
      shift
      metadata_file=${1:-}
      break
    fi
    shift
  done
  printf '{"containerimage.digest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}\n' >"$metadata_file"
  exit 0
fi
if [ "${1:-}" = compose ] && [ "${2:-}" = version ]; then
  exit 0
fi
case " $* " in
  *" ps -q iiot-httpapi "*) printf 'container-httpapi\n'; exit 0 ;;
esac
if [ "${1:-}" = inspect ] && [ "${*: -1}" = container-httpapi ]; then
  printf 'image-httpapi\n'
  exit 0
fi
if [ "${1:-}" = image ] && [ "${2:-}" = inspect ] && [ "${*: -1}" = image-httpapi ]; then
  running_digest=${FAKE_RUNNING_OCI_DIGEST:-sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb}
  printf '["harbor.test:5000/iiot/iiot-httpapi@%s"]\n' "$running_digest"
  exit 0
fi
if [ "${1:-}" = image ] && [ "${2:-}" = inspect ]; then
  printf '["harbor.test:5000/iiot/iiot-httpapi@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]\n'
  exit 0
fi
case " $* " in
  *" up -d "*) [ "${FAKE_COMPOSE_UP_FAIL:-0}" != 1 ] || exit 55 ;;
esac
exit 0
EOF
cat >"$FAKE_BIN/ssh" <<'EOF'
#!/usr/bin/env bash
printf 'ssh-called\n' >>"${FAKE_CALL_LOG:?}"
exit 99
EOF
chmod +x "$FAKE_BIN/git" "$FAKE_BIN/docker" "$FAKE_BIN/ssh"

expect_failure \
  'formal local release rejects a missing workspace marker' \
  'missing IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1' \
  env PATH="$FAKE_BIN:$PATH" bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host

expect_failure \
  'formal local release rejects an expected SHA mismatch before build' \
  'does not match the approved release plan' \
  env PATH="$FAKE_BIN:$PATH" \
    IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
    IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=11111111-1111-1111-1111-111111111111 \
    IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$OTHER_SHA" \
    IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$MISMATCH_PLAN_DIGEST" \
    IIOT_WORKSPACE_DEPLOY_PLAN_FILE="$MISMATCH_PLAN" \
    IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$PROFILE_DIGEST" \
    bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host

expect_failure \
  'formal local release rejects a candidate that is no longer the freshly fetched remote tip' \
  'is no longer the freshly fetched origin/main tip' \
  env PATH="$FAKE_BIN:$PATH" FAKE_REMOTE_SHA="$OTHER_SHA" \
    CLOUD_RELEASE_SNAPSHOT_ACTIVE=1 CLOUD_RELEASE_SOURCE_SHA="$EXPECTED_SHA" CLOUD_RELEASE_SOURCE_BRANCH=main \
    IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
    IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=11111111-1111-1111-1111-111111111112 \
    IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
    IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$HTTPAPI_PLAN_DIGEST" \
    IIOT_WORKSPACE_DEPLOY_PLAN_FILE="$HTTPAPI_PLAN" \
    IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$PROFILE_DIGEST" \
    bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host

CALL_LOG="$ROOT_TMP/calls.log"
: >"$CALL_LOG"
expect_failure \
  'image build failure occurs before remote support installation' \
  'Cloud image build failed' \
  env PATH="$FAKE_BIN:$PATH" \
    FAKE_BUILD_FAIL=1 FAKE_CALL_LOG="$CALL_LOG" \
    REGISTRY=harbor.test:5000 HARBOR_PROJECT=iiot \
    CLOUD_RELEASE_SNAPSHOT_ACTIVE=1 CLOUD_RELEASE_SOURCE_SHA="$EXPECTED_SHA" CLOUD_RELEASE_SOURCE_BRANCH=main \
    DEPLOY_ARTIFACT_DIR="$ROOT_TMP/build-failure-run" \
    IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
    IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=22222222-2222-2222-2222-222222222222 \
    IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
    IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$HTTPAPI_PLAN_DIGEST" \
    IIOT_WORKSPACE_DEPLOY_PLAN_FILE="$HTTPAPI_PLAN" \
    IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$PROFILE_DIGEST" \
    bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host
if [ -s "$CALL_LOG" ]; then
  printf 'SSH was called even though image build failed.\n' >&2
  exit 1
fi
pass 'build failure leaves remote support untouched'

LOCAL_STALE_LOCK_FIXTURE="$ROOT_TMP/local-stale-lock"
LOCAL_STALE_REMOTE="$LOCAL_STALE_LOCK_FIXTURE/remote-deploy"
LOCAL_STALE_BIN="$LOCAL_STALE_LOCK_FIXTURE/bin"
mkdir -p "$LOCAL_STALE_REMOTE/scripts" "$LOCAL_STALE_REMOTE/nginx" "$LOCAL_STALE_BIN"
: >"$LOCAL_STALE_REMOTE/.env"
sed \
  -e "s#^CLOUD_RELEASE_LOCK_FILE_DEFAULT=.*#CLOUD_RELEASE_LOCK_FILE_DEFAULT=\"$LOCAL_STALE_REMOTE/.cloud-release.lock\"#" \
  -e "s#^POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT=.*#POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT=\"$LOCAL_STALE_REMOTE/.post-release-cleanup.lock\"#" \
  -e "s#^CLOUD_CONFIG_LOCK_FILE_DEFAULT=.*#CLOUD_CONFIG_LOCK_FILE_DEFAULT=\"$LOCAL_STALE_REMOTE/.cloud-config.lock\"#" \
  "$DEPLOY_DIR/scripts/release-common.sh" \
  >"$LOCAL_STALE_REMOTE/scripts/release-common.sh"
mkdir -p "$LOCAL_STALE_REMOTE/.cloud-release.lock.d"
printf '99999999\n' >"$LOCAL_STALE_REMOTE/.cloud-release.lock.d/pid"
printf 'old-invocation\n' >"$LOCAL_STALE_REMOTE/.cloud-release.lock.d/invocation-id"
printf 'old-evidence-must-survive\n' >"$LOCAL_STALE_REMOTE/.cloud-release.lock.d/sentinel"
cp "$FAKE_BIN/git" "$FAKE_BIN/docker" "$LOCAL_STALE_BIN/"
cat >"$LOCAL_STALE_BIN/ssh" <<'EOF'
#!/usr/bin/env bash
remote_command=${!#}
case "$remote_command" in
  *'sh -s'*) eval "$remote_command" ;;
  *) printf 'unexpected mutating ssh call\n' >&2; exit 97 ;;
esac
EOF
chmod +x "$LOCAL_STALE_BIN/"*
set +e
env PATH="$LOCAL_STALE_BIN:$PATH" \
  REGISTRY=harbor.test:5000 HARBOR_PROJECT=iiot \
  CLOUD_RELEASE_SNAPSHOT_ACTIVE=1 CLOUD_RELEASE_SOURCE_SHA="$EXPECTED_SHA" CLOUD_RELEASE_SOURCE_BRANCH=main \
  DEPLOY_ARTIFACT_DIR="$LOCAL_STALE_LOCK_FIXTURE/artifacts" \
  REMOTE_DEPLOY_DIR="$LOCAL_STALE_REMOTE" \
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=local-stale-lock-check \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$HTTPAPI_PLAN_DIGEST" \
  IIOT_WORKSPACE_DEPLOY_PLAN_FILE="$HTTPAPI_PLAN" \
  IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$PROFILE_DIGEST" \
  bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host \
  >"$LOCAL_STALE_LOCK_FIXTURE/output.log" 2>&1
local_stale_status=$?
set -e
[ "$local_stale_status" -eq 75 ]
grep -qx old-evidence-must-survive "$LOCAL_STALE_REMOTE/.cloud-release.lock.d/sentinel"
[ -d "$LOCAL_STALE_REMOTE/.cloud-release.lock.d" ]
[ ! -d "$LOCAL_STALE_REMOTE/.support-staging" ]
grep -Fq 'read-only and found non-absent state: status=stale' "$LOCAL_STALE_LOCK_FIXTURE/output.log"

rm -rf "$LOCAL_STALE_REMOTE/.cloud-release.lock.d"
mkdir -p "$LOCAL_STALE_REMOTE/.post-release-cleanup.lock.d"
printf 'malformed-evidence-must-survive\n' >"$LOCAL_STALE_REMOTE/.post-release-cleanup.lock.d/sentinel"
set +e
env PATH="$LOCAL_STALE_BIN:$PATH" \
  REGISTRY=harbor.test:5000 HARBOR_PROJECT=iiot \
  CLOUD_RELEASE_SNAPSHOT_ACTIVE=1 CLOUD_RELEASE_SOURCE_SHA="$EXPECTED_SHA" CLOUD_RELEASE_SOURCE_BRANCH=main \
  DEPLOY_ARTIFACT_DIR="$LOCAL_STALE_LOCK_FIXTURE/artifacts-malformed" \
  REMOTE_DEPLOY_DIR="$LOCAL_STALE_REMOTE" \
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=local-malformed-lock-check \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$HTTPAPI_PLAN_DIGEST" \
  IIOT_WORKSPACE_DEPLOY_PLAN_FILE="$HTTPAPI_PLAN" \
  IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$PROFILE_DIGEST" \
  bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host \
  >"$LOCAL_STALE_LOCK_FIXTURE/malformed.log" 2>&1
local_malformed_status=$?
set -e
[ "$local_malformed_status" -eq 75 ]
grep -qx malformed-evidence-must-survive "$LOCAL_STALE_REMOTE/.post-release-cleanup.lock.d/sentinel"
[ -d "$LOCAL_STALE_REMOTE/.post-release-cleanup.lock.d" ]
[ ! -d "$LOCAL_STALE_REMOTE/.support-staging" ]

rm -rf "$LOCAL_STALE_REMOTE/.post-release-cleanup.lock.d"
mkdir -p "$LOCAL_STALE_REMOTE/.cloud-config.lock.d"
printf '99999999\n' >"$LOCAL_STALE_REMOTE/.cloud-config.lock.d/pid"
printf 'stale-config-evidence-must-survive\n' >"$LOCAL_STALE_REMOTE/.cloud-config.lock.d/sentinel"
set +e
env PATH="$LOCAL_STALE_BIN:$PATH" \
  REGISTRY=harbor.test:5000 HARBOR_PROJECT=iiot \
  CLOUD_RELEASE_SNAPSHOT_ACTIVE=1 CLOUD_RELEASE_SOURCE_SHA="$EXPECTED_SHA" CLOUD_RELEASE_SOURCE_BRANCH=main \
  DEPLOY_ARTIFACT_DIR="$LOCAL_STALE_LOCK_FIXTURE/artifacts-config-lock" \
  REMOTE_DEPLOY_DIR="$LOCAL_STALE_REMOTE" \
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=local-stale-config-lock-check \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$HTTPAPI_PLAN_DIGEST" \
  IIOT_WORKSPACE_DEPLOY_PLAN_FILE="$HTTPAPI_PLAN" \
  IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$PROFILE_DIGEST" \
  bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host \
  >"$LOCAL_STALE_LOCK_FIXTURE/config-lock.log" 2>&1
local_config_lock_status=$?
set -e
[ "$local_config_lock_status" -eq 75 ]
grep -qx stale-config-evidence-must-survive "$LOCAL_STALE_REMOTE/.cloud-config.lock.d/sentinel"
[ -d "$LOCAL_STALE_REMOTE/.cloud-config.lock.d" ]
[ ! -d "$LOCAL_STALE_REMOTE/.support-staging" ]
pass 'formal local precheck preserves release, cleanup, and strict config lock evidence without staging support'

extract_support_installer() {
  local destination="$1"
  awk '
    index($0, "cat > \"$package_dir/$SUPPORT_INSTALLER_NAME\" <<") { capture=1; next }
    capture && $0 == "EOF" { exit }
    capture { print }
  ' "$LOCAL_RELEASE" >"$destination"
  chmod +x "$destination"
  sh -n "$destination"
}

prepare_support_install_fixture() {
  local fixture="$1"
  local preflight_exit="$2"
  local deploy_root="$fixture/deploy"
  local staging="$fixture/staging"
  local relative_path
  local digest

  mkdir -p "$deploy_root/scripts" "$deploy_root/releases/history" "$deploy_root/certs" "$deploy_root/backups" "$staging/scripts"
  : >"$deploy_root/.env"
  printf 'old-release-common\n' >"$deploy_root/scripts/release-common.sh"
  printf 'old-precheck\n' >"$deploy_root/scripts/pre-deploy-check.sh"
  printf 'old-compose\n' >"$deploy_root/docker-compose.prod.yml"
  : >"$deploy_root/.cloud-support-manifest.sha256"
  for relative_path in scripts/release-common.sh scripts/pre-deploy-check.sh docker-compose.prod.yml; do
    digest="$(shasum -a 256 "$deploy_root/$relative_path" | awk '{print $1}')"
    printf '%s  %s\n' "$digest" "$relative_path" >>"$deploy_root/.cloud-support-manifest.sha256"
  done

  printf 'new-release-common\n' >"$staging/scripts/release-common.sh"
  cat >"$staging/scripts/pre-deploy-check.sh" <<EOF
#!/bin/sh
exit $preflight_exit
EOF
  printf 'new-compose\n' >"$staging/docker-compose.prod.yml"
  printf 'must-never-remain-installed\n' >"$staging/unsupported.txt"
  printf '%s\n' \
    scripts/release-common.sh \
    unsupported.txt \
    scripts/pre-deploy-check.sh \
    docker-compose.prod.yml \
    >"$staging/.cloud-support-allowlist.txt"
  : >"$staging/.cloud-support-manifest.sha256"
  while IFS= read -r relative_path; do
    digest="$(shasum -a 256 "$staging/$relative_path" | awk '{print $1}')"
    printf '%s  %s\n' "$digest" "$relative_path" >>"$staging/.cloud-support-manifest.sha256"
  done <"$staging/.cloud-support-allowlist.txt"
}

SUPPORT_INSTALLER="$ROOT_TMP/install-cloud-support.sh"
extract_support_installer "$SUPPORT_INSTALLER"

PREFLIGHT_INSTALL_FIXTURE="$ROOT_TMP/support-preflight-failure"
prepare_support_install_fixture "$PREFLIGHT_INSTALL_FIXTURE" 41
set +e
env PATH="$FAKE_BIN:$PATH" sh "$SUPPORT_INSTALLER" \
  "$PREFLIGHT_INSTALL_FIXTURE/deploy" \
  "$PREFLIGHT_INSTALL_FIXTURE/staging" \
  preflight-failure \
  "sha-$EXPECTED_SHA" \
  "$(shasum -a 256 "$PREFLIGHT_INSTALL_FIXTURE/staging/.cloud-support-manifest.sha256" | awk '{print $1}')" \
  "$$" \
  "$PREFLIGHT_INSTALL_FIXTURE/release.lock" \
  >"$PREFLIGHT_INSTALL_FIXTURE/output.log" 2>&1
preflight_install_status=$?
set -e
if [ "$preflight_install_status" -ne 41 ]; then
  printf 'Candidate support preflight did not preserve its failure status: %s\n' "$preflight_install_status" >&2
  sed -n '1,160p' "$PREFLIGHT_INSTALL_FIXTURE/output.log" >&2
  exit 1
fi
grep -qx old-release-common "$PREFLIGHT_INSTALL_FIXTURE/deploy/scripts/release-common.sh"
[ ! -e "$PREFLIGHT_INSTALL_FIXTURE/deploy/releases/support-recovery/preflight-failure" ]
[ ! -e "$PREFLIGHT_INSTALL_FIXTURE/staging/.env" ]

MID_INSTALL_FIXTURE="$ROOT_TMP/support-mid-install-failure"
prepare_support_install_fixture "$MID_INSTALL_FIXTURE" 0
set +e
env PATH="$FAKE_BIN:$PATH" sh "$SUPPORT_INSTALLER" \
  "$MID_INSTALL_FIXTURE/deploy" \
  "$MID_INSTALL_FIXTURE/staging" \
  mid-install-failure \
  "sha-$EXPECTED_SHA" \
  "$(shasum -a 256 "$MID_INSTALL_FIXTURE/staging/.cloud-support-manifest.sha256" | awk '{print $1}')" \
  "$$" \
  "$MID_INSTALL_FIXTURE/release.lock" \
  >"$MID_INSTALL_FIXTURE/output.log" 2>&1
mid_install_status=$?
set -e
if [ "$mid_install_status" -ne 73 ]; then
  printf 'Mid-install support failure did not preserve its failure status: %s\n' "$mid_install_status" >&2
  sed -n '1,160p' "$MID_INSTALL_FIXTURE/output.log" >&2
  exit 1
fi
grep -qx old-release-common "$MID_INSTALL_FIXTURE/deploy/scripts/release-common.sh"
[ ! -e "$MID_INSTALL_FIXTURE/deploy/unsupported.txt" ]
grep -qx restored "$MID_INSTALL_FIXTURE/deploy/releases/support-recovery/mid-install-failure/restore-result"
(cd "$MID_INSTALL_FIXTURE/deploy" && sha256sum -c .cloud-support-manifest.sha256 >/dev/null)
pass 'candidate support preflight is non-mutating and mid-install failure restores the old support set'

MALICIOUS_MANIFEST_FIXTURE="$ROOT_TMP/support-malicious-old-manifest"
prepare_support_install_fixture "$MALICIOUS_MANIFEST_FIXTURE" 0
printf 'outside-must-survive\n' >"$MALICIOUS_MANIFEST_FIXTURE/outside.txt"
printf '%s  %s\n' \
  "$(shasum -a 256 "$MALICIOUS_MANIFEST_FIXTURE/outside.txt" | awk '{print $1}')" \
  '../outside.txt' \
  >>"$MALICIOUS_MANIFEST_FIXTURE/deploy/.cloud-support-manifest.sha256"
set +e
env PATH="$FAKE_BIN:$PATH" sh "$SUPPORT_INSTALLER" \
  "$MALICIOUS_MANIFEST_FIXTURE/deploy" \
  "$MALICIOUS_MANIFEST_FIXTURE/staging" \
  malicious-old-manifest \
  "sha-$EXPECTED_SHA" \
  "$(shasum -a 256 "$MALICIOUS_MANIFEST_FIXTURE/staging/.cloud-support-manifest.sha256" | awk '{print $1}')" \
  "$$" \
  "$MALICIOUS_MANIFEST_FIXTURE/release.lock" \
  >"$MALICIOUS_MANIFEST_FIXTURE/output.log" 2>&1
malicious_manifest_status=$?
set -e
[ "$malicious_manifest_status" -eq 73 ]
grep -qx outside-must-survive "$MALICIOUS_MANIFEST_FIXTURE/outside.txt"
[ ! -e "$MALICIOUS_MANIFEST_FIXTURE/deploy/releases/support-recovery/malicious-old-manifest" ]

SYMLINK_TARGET_FIXTURE="$ROOT_TMP/support-symlink-target"
prepare_support_install_fixture "$SYMLINK_TARGET_FIXTURE" 0
printf 'external-target-must-survive\n' >"$SYMLINK_TARGET_FIXTURE/external-target.txt"
rm "$SYMLINK_TARGET_FIXTURE/deploy/scripts/release-common.sh"
ln -s "$SYMLINK_TARGET_FIXTURE/external-target.txt" \
  "$SYMLINK_TARGET_FIXTURE/deploy/scripts/release-common.sh"
set +e
env PATH="$FAKE_BIN:$PATH" sh "$SUPPORT_INSTALLER" \
  "$SYMLINK_TARGET_FIXTURE/deploy" \
  "$SYMLINK_TARGET_FIXTURE/staging" \
  symlink-target \
  "sha-$EXPECTED_SHA" \
  "$(shasum -a 256 "$SYMLINK_TARGET_FIXTURE/staging/.cloud-support-manifest.sha256" | awk '{print $1}')" \
  "$$" \
  "$SYMLINK_TARGET_FIXTURE/release.lock" \
  >"$SYMLINK_TARGET_FIXTURE/output.log" 2>&1
symlink_target_status=$?
set -e
[ "$symlink_target_status" -eq 73 ]
grep -qx external-target-must-survive "$SYMLINK_TARGET_FIXTURE/external-target.txt"
[ ! -e "$SYMLINK_TARGET_FIXTURE/deploy/releases/support-recovery/symlink-target" ]
pass 'old manifest traversal and symlinked support targets are rejected without external mutation'

prepare_workspace_transaction_fixture() {
  local fixture="$1"
  local mode="$2"
  local stage_name="${3:-transaction}"
  local deploy_root="$fixture/deploy"
  local staging="$deploy_root/.support-staging/$stage_name"
  mkdir -p "$deploy_root/releases/history" "$deploy_root/scripts" "$staging/scripts"
  : >"$deploy_root/.env"
  : >"$staging/.cloud-image-manifest.env"
  cp "$DEPLOY_DIR/scripts/release-common.sh" "$staging/scripts/release-common.sh"
  cp "$DEPLOY_DIR/scripts/workspace-release-transaction.sh" "$staging/scripts/workspace-release-transaction.sh"
  chmod +x "$staging/scripts/workspace-release-transaction.sh"
  case "$mode" in
    restore-failure)
      cat >"$staging/.install-cloud-support.sh" <<'EOF'
#!/bin/sh
set -eu
backup_dir="$1/releases/support-recovery/$3"
mkdir -p "$backup_dir"
cat >"$backup_dir/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
printf 'attempt\n' >>"$2/restore-attempts"
exit 86
RESTORE_EOF
chmod +x "$backup_dir/restore-support.sh"
sh "$backup_dir/restore-support.sh" "$1" "$backup_dir" || exit 86
EOF
      ;;
    handoff-failure)
      cat >"$staging/.install-cloud-support.sh" <<'EOF'
#!/bin/sh
set -eu
backup_dir="$1/releases/support-recovery/$3"
mkdir -p "$backup_dir" "$1/scripts"
cat >"$backup_dir/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
printf 'restored\n' >"$2/restore-result"
exit 0
RESTORE_EOF
chmod +x "$backup_dir/restore-support.sh"
cat >"$1/scripts/deploy-release.sh" <<'DEPLOY_EOF'
#!/bin/sh
exit 0
DEPLOY_EOF
chmod 644 "$1/scripts/deploy-release.sh"
printf 'installed\n' >"$backup_dir/install-result"
EOF
      ;;
    parent-kill)
      cat >"$staging/.install-cloud-support.sh" <<'EOF'
#!/bin/sh
set -eu
backup_dir="$1/releases/support-recovery/$3"
mkdir -p "$backup_dir"
cat >"$backup_dir/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
printf 'restored\n' >"$2/restore-result"
exit 0
RESTORE_EOF
chmod +x "$backup_dir/restore-support.sh"
printf 'mutated\n' >"$backup_dir/support-mutation-started"
transaction_marker="$1/releases/transactions/$3.env"
wait_attempt=0
until grep -qx 'DEPLOY_TRANSACTION_PHASE=support-installer-running' "$transaction_marker" 2>/dev/null \
  && grep -qx "DEPLOY_TRANSACTION_PARENT_PID=$6" "$transaction_marker" 2>/dev/null; do
  wait_attempt=$((wait_attempt + 1))
  if [ "$wait_attempt" -ge 200 ]; then
    printf 'Parent never recorded support-installer-running before the kill fixture.\n' >&2
    exit 87
  fi
  sleep 0.01
done
kill -KILL "$6"
exit 0
EOF
      ;;
    promoted-parent-kill)
      cat >"$staging/.install-cloud-support.sh" <<'EOF'
#!/bin/sh
set -eu
backup_dir="$1/releases/support-recovery/$3"
mkdir -p "$backup_dir" "$1/scripts" "$1/releases"
printf 'old-support\n' >"$backup_dir/old-support"
cat >"$backup_dir/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
cp "$2/old-support" "$1/support-version"
printf 'restored\n' >"$2/restore-result"
RESTORE_EOF
chmod +x "$backup_dir/restore-support.sh"
cp "$2/scripts/release-common.sh" "$1/scripts/release-common.sh"
printf 'new-support\n' >"$1/support-version"
cat >"$1/scripts/deploy-release.sh" <<'DEPLOY_EOF'
#!/usr/bin/env bash
set -euo pipefail
deploy_dir="$(pwd -P)"
DEPLOY_DIR="$deploy_dir"
export DEPLOY_DIR
. "$deploy_dir/scripts/release-common.sh"
printf 'started\n' >"$DEPLOY_RELEASE_HANDOFF_MARKER"
{
  printf 'DEPLOY_INVOCATION_ID=%s\n' "$IIOT_WORKSPACE_DEPLOY_INVOCATION_ID"
  printf 'DEPLOY_EXPECTED_SHA=%s\n' "$IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA"
  printf 'DEPLOY_PLAN_DIGEST=%s\n' "$IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST"
  printf 'DEPLOY_PHASE=runtime-healthy-cleanup-pending\n'
  printf 'DEPLOY_CLEANUP_STATUS=pending\n'
  printf 'IIOT_HTTPAPI_IMAGE=test/httpapi\n'
  printf 'IIOT_GATEWAY_IMAGE=test/gateway\n'
  printf 'IIOT_DATAWORKER_IMAGE=test/dataworker\n'
  printf 'IIOT_MIGRATION_IMAGE=test/migration\n'
  printf 'IIOT_WEB_IMAGE=test/web\n'
} | atomic_write_file "$deploy_dir/releases/current-release.env" 600
{
  printf 'IIOT_HTTPAPI_IMAGE=test/httpapi\n'
  printf 'IIOT_GATEWAY_IMAGE=test/gateway\n'
  printf 'IIOT_DATAWORKER_IMAGE=test/dataworker\n'
  printf 'IIOT_MIGRATION_IMAGE=test/migration\n'
  printf 'IIOT_WEB_IMAGE=test/web\n'
} | atomic_write_file "$deploy_dir/releases/current-images.env" 600
replace_env_value "$DEPLOY_TRANSACTION_MARKER" DEPLOY_TRANSACTION_PHASE runtime-promoted
printf 'promotion-proof-written\n' >"$DEPLOY_SUPPORT_BACKUP_DIR/promotion-proof-written"
kill -KILL "$DEPLOY_RELEASE_LOCK_OWNER_PID"
sleep 1
DEPLOY_EOF
chmod 755 "$1/scripts/deploy-release.sh"
printf 'installed\n' >"$backup_dir/install-result"
EOF
      ;;
    installer-hang)
      cat >"$staging/.install-cloud-support.sh" <<'EOF'
#!/bin/sh
set -eu
backup_dir="$1/releases/support-recovery/$3"
mkdir -p "$backup_dir"
cat >"$backup_dir/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
printf 'restored\n' >"$2/restore-result"
RESTORE_EOF
chmod +x "$backup_dir/restore-support.sh"
(
  trap '' TERM INT HUP
  while :; do sleep 1; done
) &
printf '%s\n' "$!" >"$backup_dir/installer-grandchild.pid"
printf '%s\n' "$$" >"$backup_dir/installer.pid"
trap '' TERM INT HUP
while :; do sleep 1; done
EOF
      ;;
    child-kill|child-term|child-grandchild|child-leader-exit)
      cat >"$staging/.install-cloud-support.sh" <<EOF
#!/bin/sh
set -eu
backup_dir="\$1/releases/support-recovery/\$3"
mkdir -p "\$backup_dir" "\$1/scripts"
cat >"\$backup_dir/restore-support.sh" <<'RESTORE_EOF'
#!/bin/sh
printf 'restored\\n' >"\$2/restore-result"
exit 0
RESTORE_EOF
chmod +x "\$backup_dir/restore-support.sh"
cat >"\$1/scripts/deploy-release.sh" <<'DEPLOY_EOF'
#!/usr/bin/env bash
set -euo pipefail
printf 'started\\n' >"\$DEPLOY_RELEASE_HANDOFF_MARKER"
printf 'ready\\n' >"\$DEPLOY_SUPPORT_BACKUP_DIR/child-ready"
env | LC_ALL=C sort >"\$DEPLOY_SUPPORT_BACKUP_DIR/child-environment"
if [ "$mode" = child-kill ]; then
  kill -KILL "\$\$"
fi
if [ "$mode" = child-leader-exit ]; then
  (
    trap '' TERM INT HUP
    while :; do sleep 1; done
  ) &
  printf '%s\\n' "\$!" >"\$DEPLOY_SUPPORT_BACKUP_DIR/release-grandchild.pid"
  printf '%s\\n' "\$\$" >"\$DEPLOY_SUPPORT_BACKUP_DIR/release.pid"
  exit 55
fi
if [ "$mode" = child-grandchild ]; then
  (
    trap '' TERM INT HUP
    while :; do sleep 1; done
  ) &
  printf '%s\\n' "\$!" >"\$DEPLOY_SUPPORT_BACKUP_DIR/release-grandchild.pid"
  printf '%s\\n' "\$\$" >"\$DEPLOY_SUPPORT_BACKUP_DIR/release.pid"
  trap '' TERM INT HUP
else
  trap 'printf "term\\n" >"\$DEPLOY_SUPPORT_BACKUP_DIR/child-term"; exit 143' TERM
fi
while :; do sleep 1; done
DEPLOY_EOF
chmod 755 "\$1/scripts/deploy-release.sh"
printf 'installed\\n' >"\$backup_dir/install-result"
EOF
      ;;
    *)
      printf 'Unknown transaction fixture mode: %s\n' "$mode" >&2
      exit 1
      ;;
  esac
  chmod +x "$staging/.install-cloud-support.sh"
  printf '%s\n' "$staging"
}

run_workspace_transaction_fixture() {
  local fixture="$1"
  local staging="$2"
  local invocation_id="${3:-transaction-test}"
  env PATH="$FAKE_BIN:$PATH" DEPLOY_RELEASE_LOCK_FILE="$fixture/release.lock" \
    bash "$staging/scripts/workspace-release-transaction.sh" \
      "$fixture/deploy" \
      "$staging" \
      "$invocation_id" \
      "sha-$EXPECTED_SHA" \
      "$EXPECTED_SHA" \
      "$PLAN_DIGEST" \
      "$SUPPORT_DIGEST" \
      "$PLAN_DIGEST" \
      "$PROFILE_DIGEST" \
      httpapi
}

RESTORE_FAILURE_FIXTURE="$ROOT_TMP/transaction-restore-failure"
RESTORE_FAILURE_STAGING="$(prepare_workspace_transaction_fixture "$RESTORE_FAILURE_FIXTURE" restore-failure)"
set +e
run_workspace_transaction_fixture "$RESTORE_FAILURE_FIXTURE" "$RESTORE_FAILURE_STAGING" \
  >"$RESTORE_FAILURE_FIXTURE/output.log" 2>&1
restore_failure_status=$?
set -e
if [ "$restore_failure_status" -ne 86 ]; then
  printf 'Unconfirmed support restore did not return dedicated status 86: %s\n' "$restore_failure_status" >&2
  sed -n '1,200p' "$RESTORE_FAILURE_FIXTURE/output.log" >&2
  exit 1
fi
grep -qx 'DEPLOY_PHASE=blocked-support-install-restore' \
  "$RESTORE_FAILURE_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx 'DEPLOY_SUPPORT_RESTORE_STATUS=failed' \
  "$RESTORE_FAILURE_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx blocked-support-install-restore "$RESTORE_FAILURE_FIXTURE/release.lock.d/phase"
[ -d "$RESTORE_FAILURE_FIXTURE/deploy/releases/support-recovery/transaction-test" ]
[ -d "$RESTORE_FAILURE_STAGING" ]
[ "$(wc -l <"$RESTORE_FAILURE_FIXTURE/deploy/releases/support-recovery/transaction-test/restore-attempts" | tr -d ' ')" -ge 2 ]
set +e
run_workspace_transaction_fixture "$RESTORE_FAILURE_FIXTURE" "$RESTORE_FAILURE_STAGING" \
  >"$RESTORE_FAILURE_FIXTURE/retry.log" 2>&1
blocked_retry_status=$?
set -e
[ "$blocked_retry_status" -eq 78 ]
[ -d "$RESTORE_FAILURE_FIXTURE/release.lock.d" ]
pass 'unconfirmed installer restore writes durable blocked evidence and preserves lock, staging, and recovery backup'

HANDOFF_FAILURE_FIXTURE="$ROOT_TMP/transaction-handoff-failure"
HANDOFF_FAILURE_STAGING="$(prepare_workspace_transaction_fixture "$HANDOFF_FAILURE_FIXTURE" handoff-failure)"
set +e
run_workspace_transaction_fixture "$HANDOFF_FAILURE_FIXTURE" "$HANDOFF_FAILURE_STAGING" \
  >"$HANDOFF_FAILURE_FIXTURE/output.log" 2>&1
handoff_failure_status=$?
set -e
if [ "$handoff_failure_status" -ne 126 ]; then
  printf 'Non-executable deploy-release handoff did not return status 126: %s\n' "$handoff_failure_status" >&2
  sed -n '1,200p' "$HANDOFF_FAILURE_FIXTURE/output.log" >&2
  exit 1
fi
grep -qx 'DEPLOY_PHASE=blocked-deploy-release-start' \
  "$HANDOFF_FAILURE_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx 'DEPLOY_SUPPORT_RESTORE_STATUS=restored' \
  "$HANDOFF_FAILURE_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx restored \
  "$HANDOFF_FAILURE_FIXTURE/deploy/releases/support-recovery/transaction-test/restore-result"
[ -d "$HANDOFF_FAILURE_FIXTURE/deploy/releases/support-recovery/transaction-test" ]
[ ! -d "$HANDOFF_FAILURE_STAGING" ]
[ ! -d "$HANDOFF_FAILURE_FIXTURE/release.lock.d" ]
pass 'transaction parent restores support and blocks retry when deploy-release cannot start'

PARENT_KILL_FIXTURE="$ROOT_TMP/transaction-parent-kill"
PARENT_KILL_STAGING="$(prepare_workspace_transaction_fixture "$PARENT_KILL_FIXTURE" parent-kill first)"
set +e
run_workspace_transaction_fixture "$PARENT_KILL_FIXTURE" "$PARENT_KILL_STAGING" parent-killed \
  >"$PARENT_KILL_FIXTURE/output.log" 2>&1
parent_kill_status=$?
set -e
if [ "$parent_kill_status" -ne 137 ]; then
  printf 'Support-install parent-kill fixture returned unexpected status: %s\n' "$parent_kill_status" >&2
  sed -n '1,200p' "$PARENT_KILL_FIXTURE/output.log" >&2
  exit 1
fi
parent_kill_marker="$PARENT_KILL_FIXTURE/deploy/releases/transactions/parent-killed.env"
if ! grep -qx 'DEPLOY_TRANSACTION_PHASE=support-installer-running' "$parent_kill_marker"; then
  printf 'Support-install parent-kill fixture did not preserve the synchronized running phase.\n' >&2
  sed -n '1,200p' "$parent_kill_marker" >&2 || true
  sed -n '1,200p' "$PARENT_KILL_FIXTURE/output.log" >&2
  exit 1
fi
[ -d "$PARENT_KILL_FIXTURE/release.lock.d" ]
[ -d "$PARENT_KILL_FIXTURE/deploy/releases/support-recovery/parent-killed" ]
[ -d "$PARENT_KILL_STAGING" ]

PARENT_KILL_RETRY_STAGING="$(prepare_workspace_transaction_fixture "$PARENT_KILL_FIXTURE" handoff-failure retry)"
set +e
run_workspace_transaction_fixture "$PARENT_KILL_FIXTURE" "$PARENT_KILL_RETRY_STAGING" retry-after-reboot \
  >"$PARENT_KILL_FIXTURE/retry.log" 2>&1
parent_kill_retry_status=$?
set -e
[ "$parent_kill_retry_status" -eq 78 ]
grep -qx 'DEPLOY_PHASE=blocked-orphaned-durable-transaction' \
  "$PARENT_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx 'DEPLOY_INVOCATION_ID=parent-killed' \
  "$PARENT_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx "DEPLOY_ORPHAN_MARKER_PATH=$PARENT_KILL_FIXTURE/deploy/releases/transactions/parent-killed.env" \
  "$PARENT_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx "DEPLOY_SUPPORT_BACKUP_DIR=$PARENT_KILL_FIXTURE/deploy/releases/support-recovery/parent-killed" \
  "$PARENT_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx "DEPLOY_SUPPORT_STAGING_DIR=$PARENT_KILL_STAGING" \
  "$PARENT_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx "DEPLOY_RELEASE_LOCK_FILE=$PARENT_KILL_FIXTURE/release.lock" \
  "$PARENT_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
[ -d "$PARENT_KILL_FIXTURE/release.lock.d" ]
[ -d "$PARENT_KILL_STAGING" ]
pass 'support-install parent kill leaves durable state that reboot scan blocks without stale cleanup'

PROMOTED_KILL_FIXTURE="$ROOT_TMP/transaction-promoted-parent-kill"
PROMOTED_KILL_STAGING="$(prepare_workspace_transaction_fixture "$PROMOTED_KILL_FIXTURE" promoted-parent-kill promoted)"
set +e
run_workspace_transaction_fixture "$PROMOTED_KILL_FIXTURE" "$PROMOTED_KILL_STAGING" promoted-before-parent-kill \
  >"$PROMOTED_KILL_FIXTURE/output.log" 2>&1
promoted_kill_status=$?
set -e
if [ "$promoted_kill_status" -ne 137 ]; then
  printf 'Promoted parent-kill fixture returned unexpected status: %s\n' "$promoted_kill_status" >&2
  sed -n '1,240p' "$PROMOTED_KILL_FIXTURE/output.log" >&2
  exit 1
fi
grep -qx new-support "$PROMOTED_KILL_FIXTURE/deploy/support-version"
grep -qx 'DEPLOY_TRANSACTION_PHASE=runtime-promoted' \
  "$PROMOTED_KILL_FIXTURE/deploy/releases/transactions/promoted-before-parent-kill.env"
grep -qx 'DEPLOY_INVOCATION_ID=promoted-before-parent-kill' \
  "$PROMOTED_KILL_FIXTURE/deploy/releases/current-release.env"
[ -d "$PROMOTED_KILL_FIXTURE/deploy/releases/support-recovery/promoted-before-parent-kill" ]
[ ! -e "$PROMOTED_KILL_FIXTURE/deploy/releases/support-recovery/promoted-before-parent-kill/restore-result" ]

PROMOTED_KILL_RETRY_STAGING="$(prepare_workspace_transaction_fixture "$PROMOTED_KILL_FIXTURE" handoff-failure promoted-retry)"
set +e
run_workspace_transaction_fixture "$PROMOTED_KILL_FIXTURE" "$PROMOTED_KILL_RETRY_STAGING" promoted-retry \
  >"$PROMOTED_KILL_FIXTURE/retry.log" 2>&1
promoted_kill_retry_status=$?
set -e
if [ "$promoted_kill_retry_status" -ne 0 ]; then
  sed -n '1,240p' "$PROMOTED_KILL_FIXTURE/retry.log" >&2
  exit 1
fi
grep -qx new-support "$PROMOTED_KILL_FIXTURE/deploy/support-version"
[ ! -d "$PROMOTED_KILL_FIXTURE/deploy/releases/support-recovery/promoted-before-parent-kill" ]
[ ! -d "$PROMOTED_KILL_FIXTURE/release.lock.d" ]
[ ! -e "$PROMOTED_KILL_FIXTURE/deploy/releases/transactions/promoted-before-parent-kill.env" ]
[ ! -d "$PROMOTED_KILL_RETRY_STAGING" ]
grep -Fq 'cleanup-only mode; no support install or rollout was repeated' \
  "$PROMOTED_KILL_FIXTURE/retry.log"
if find "$PROMOTED_KILL_FIXTURE/deploy/releases/history" -type f -name '*transaction-promoted-before-parent-kill.env' \
  -exec grep -l 'DEPLOY_TRANSACTION_PHASE=runtime-promoted' {} + | grep -q .; then
  :
else
  printf 'Promoted transaction marker was not archived during cleanup-only recovery.\n' >&2
  exit 1
fi
pass 'promotion proof prevents support restore and the next run performs transaction cleanup only'

CHILD_KILL_FIXTURE="$ROOT_TMP/transaction-child-kill"
CHILD_KILL_STAGING="$(prepare_workspace_transaction_fixture "$CHILD_KILL_FIXTURE" child-kill)"
set +e
FAKE_ATTACKER_CONTROL=must-not-reach-child \
  run_workspace_transaction_fixture "$CHILD_KILL_FIXTURE" "$CHILD_KILL_STAGING" child-killed \
  >"$CHILD_KILL_FIXTURE/output.log" 2>&1
child_kill_status=$?
set -e
[ "$child_kill_status" -eq 137 ]
grep -qx 'DEPLOY_PHASE=blocked-child-rollout-unproven' \
  "$CHILD_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx 'DEPLOY_SUPPORT_RESTORE_STATUS=restored' \
  "$CHILD_KILL_FIXTURE/deploy/releases/deploy-blocked.env"
[ -d "$CHILD_KILL_FIXTURE/deploy/releases/support-recovery/child-killed" ]
[ ! -d "$CHILD_KILL_FIXTURE/release.lock.d" ]
if grep -Eq '^(FAKE_ATTACKER_CONTROL|BASH_ENV|ENV|CDPATH|IFS)=' \
  "$CHILD_KILL_FIXTURE/deploy/releases/support-recovery/child-killed/child-environment"; then
  printf 'Transaction child inherited a forbidden control variable.\n' >&2
  exit 1
fi
grep -qx 'PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin' \
  "$CHILD_KILL_FIXTURE/deploy/releases/support-recovery/child-killed/child-environment"
pass 'release child kill-9 without atomic promotion proof restores support and persists blocked evidence'

PARENT_TERM_FIXTURE="$ROOT_TMP/transaction-parent-term"
PARENT_TERM_STAGING="$(prepare_workspace_transaction_fixture "$PARENT_TERM_FIXTURE" child-term)"
set +e
run_workspace_transaction_fixture "$PARENT_TERM_FIXTURE" "$PARENT_TERM_STAGING" parent-term \
  >"$PARENT_TERM_FIXTURE/output.log" 2>&1 &
parent_term_job=$!
set -e
term_wait_attempt=0
while [ ! -f "$PARENT_TERM_FIXTURE/deploy/releases/support-recovery/parent-term/child-ready" ] \
  && [ "$term_wait_attempt" -lt 100 ]; do
  sleep 0.05
  term_wait_attempt=$((term_wait_attempt + 1))
done
[ -f "$PARENT_TERM_FIXTURE/deploy/releases/support-recovery/parent-term/child-ready" ]
parent_term_pid=$(sed -n 's/^DEPLOY_TRANSACTION_PARENT_PID=//p' \
  "$PARENT_TERM_FIXTURE/deploy/releases/transactions/parent-term.env")
kill -TERM "$parent_term_pid"
set +e
wait "$parent_term_job"
parent_term_status=$?
set -e
case "$parent_term_status" in 0|143) ;; *) exit 1 ;; esac
if kill -0 "$parent_term_pid" 2>/dev/null; then
  printf 'Transaction parent remained alive after TERM handling.\n' >&2
  exit 1
fi
if [ ! -f "$PARENT_TERM_FIXTURE/deploy/releases/support-recovery/parent-term/child-term" ]; then
  sed -n '1,200p' "$PARENT_TERM_FIXTURE/output.log" >&2
  find "$PARENT_TERM_FIXTURE/deploy/releases/support-recovery/parent-term" -maxdepth 1 -print >&2 || true
  exit 1
fi
grep -qx term "$PARENT_TERM_FIXTURE/deploy/releases/support-recovery/parent-term/child-term"
grep -qx 'DEPLOY_PHASE=blocked-parent-term-before-promotion' \
  "$PARENT_TERM_FIXTURE/deploy/releases/deploy-blocked.env"
[ ! -d "$PARENT_TERM_FIXTURE/release.lock.d" ]
pass 'transaction parent TERM is forwarded to the release child and waited before recovery'

INSTALLER_HANG_FIXTURE="$ROOT_TMP/transaction-installer-hang"
INSTALLER_HANG_STAGING="$(prepare_workspace_transaction_fixture "$INSTALLER_HANG_FIXTURE" installer-hang)"
set +e
run_workspace_transaction_fixture "$INSTALLER_HANG_FIXTURE" "$INSTALLER_HANG_STAGING" installer-hang \
  >"$INSTALLER_HANG_FIXTURE/output.log" 2>&1 &
installer_hang_job=$!
set -e
installer_hang_wait=0
while [ ! -f "$INSTALLER_HANG_FIXTURE/deploy/releases/support-recovery/installer-hang/installer-grandchild.pid" ] \
  && [ "$installer_hang_wait" -lt 100 ]; do
  sleep 0.05
  installer_hang_wait=$((installer_hang_wait + 1))
done
[ -f "$INSTALLER_HANG_FIXTURE/deploy/releases/support-recovery/installer-hang/installer-grandchild.pid" ]
installer_hang_pid=$(sed -n '1p' "$INSTALLER_HANG_FIXTURE/deploy/releases/support-recovery/installer-hang/installer.pid")
installer_hang_grandchild=$(sed -n '1p' "$INSTALLER_HANG_FIXTURE/deploy/releases/support-recovery/installer-hang/installer-grandchild.pid")
installer_hang_parent=$(sed -n 's/^DEPLOY_TRANSACTION_PARENT_PID=//p' \
  "$INSTALLER_HANG_FIXTURE/deploy/releases/transactions/installer-hang.env")
kill -TERM "$installer_hang_parent"
set +e
wait "$installer_hang_job"
installer_hang_status=$?
set -e
case "$installer_hang_status" in 0|143) ;; *) exit 1 ;; esac
if kill -0 "$installer_hang_pid" 2>/dev/null || kill -0 "$installer_hang_grandchild" 2>/dev/null; then
  printf 'Installer process group descendants survived bounded TERM/KILL convergence.\n' >&2
  exit 1
fi
grep -qx restored "$INSTALLER_HANG_FIXTURE/deploy/releases/support-recovery/installer-hang/restore-result"
[ ! -d "$INSTALLER_HANG_FIXTURE/release.lock.d" ]
pass 'hanging installer process group is bounded, killed with descendants, then restored'

RELEASE_GRANDCHILD_FIXTURE="$ROOT_TMP/transaction-release-grandchild"
RELEASE_GRANDCHILD_STAGING="$(prepare_workspace_transaction_fixture "$RELEASE_GRANDCHILD_FIXTURE" child-grandchild)"
set +e
run_workspace_transaction_fixture "$RELEASE_GRANDCHILD_FIXTURE" "$RELEASE_GRANDCHILD_STAGING" release-grandchild \
  >"$RELEASE_GRANDCHILD_FIXTURE/output.log" 2>&1 &
release_grandchild_job=$!
set -e
release_grandchild_wait=0
while [ ! -f "$RELEASE_GRANDCHILD_FIXTURE/deploy/releases/support-recovery/release-grandchild/release-grandchild.pid" ] \
  && [ "$release_grandchild_wait" -lt 100 ]; do
  sleep 0.05
  release_grandchild_wait=$((release_grandchild_wait + 1))
done
[ -f "$RELEASE_GRANDCHILD_FIXTURE/deploy/releases/support-recovery/release-grandchild/release-grandchild.pid" ]
release_child_pid=$(sed -n '1p' "$RELEASE_GRANDCHILD_FIXTURE/deploy/releases/support-recovery/release-grandchild/release.pid")
release_grandchild_pid=$(sed -n '1p' "$RELEASE_GRANDCHILD_FIXTURE/deploy/releases/support-recovery/release-grandchild/release-grandchild.pid")
release_parent_pid=$(sed -n 's/^DEPLOY_TRANSACTION_PARENT_PID=//p' \
  "$RELEASE_GRANDCHILD_FIXTURE/deploy/releases/transactions/release-grandchild.env")
kill -TERM "$release_parent_pid"
set +e
wait "$release_grandchild_job"
release_grandchild_status=$?
set -e
case "$release_grandchild_status" in 0|143) ;; *) exit 1 ;; esac
if kill -0 "$release_child_pid" 2>/dev/null || kill -0 "$release_grandchild_pid" 2>/dev/null; then
  printf 'Release process group descendants survived bounded TERM/KILL convergence.\n' >&2
  exit 1
fi
grep -qx restored "$RELEASE_GRANDCHILD_FIXTURE/deploy/releases/support-recovery/release-grandchild/restore-result"
[ ! -d "$RELEASE_GRANDCHILD_FIXTURE/release.lock.d" ]
pass 'TERM-ignoring release child and grandchild are killed before restore and lock release'

LEADER_EXIT_FIXTURE="$ROOT_TMP/transaction-leader-exit-grandchild"
LEADER_EXIT_STAGING="$(prepare_workspace_transaction_fixture "$LEADER_EXIT_FIXTURE" child-leader-exit)"
set +e
run_workspace_transaction_fixture "$LEADER_EXIT_FIXTURE" "$LEADER_EXIT_STAGING" leader-exit-grandchild \
  >"$LEADER_EXIT_FIXTURE/output.log" 2>&1
leader_exit_status=$?
set -e
[ "$leader_exit_status" -eq 55 ]
leader_exit_pid=$(sed -n '1p' "$LEADER_EXIT_FIXTURE/deploy/releases/support-recovery/leader-exit-grandchild/release.pid")
leader_exit_grandchild=$(sed -n '1p' "$LEADER_EXIT_FIXTURE/deploy/releases/support-recovery/leader-exit-grandchild/release-grandchild.pid")
if kill -0 "$leader_exit_pid" 2>/dev/null || kill -0 "$leader_exit_grandchild" 2>/dev/null; then
  printf 'Grandchild survived after its process-group leader exited.\n' >&2
  exit 1
fi
grep -qx restored "$LEADER_EXIT_FIXTURE/deploy/releases/support-recovery/leader-exit-grandchild/restore-result"
[ ! -d "$LEADER_EXIT_FIXTURE/release.lock.d" ]
grep -Fq 'stopping release process group' "$LEADER_EXIT_FIXTURE/output.log"
pass 'leader exit still drains the surviving process group before restore and lock release'

DOTENV_GUARD_FIXTURE="$ROOT_TMP/dotenv-guard"
mkdir -p "$DOTENV_GUARD_FIXTURE/deploy/scripts"
cp "$DEPLOY_DIR/scripts/release-common.sh" "$DOTENV_GUARD_FIXTURE/deploy/scripts/release-common.sh"
printf 'unchanged\n' >"$DOTENV_GUARD_FIXTURE/victim"
dotenv_secret_value=dotenv-secret-value-must-never-leak-91
dotenv_secret_in_key=dotenv-secret-inside-invalid-key-37
dotenv_case=0
while IFS= read -r malicious_line; do
  dotenv_case=$((dotenv_case + 1))
  dotenv_file="$DOTENV_GUARD_FIXTURE/deploy/case-$dotenv_case.env"
  printf '%s\n' "$malicious_line" >"$dotenv_file"
  set +e
  DEPLOY_DIR="$DOTENV_GUARD_FIXTURE/deploy" sh -c \
    '. "$1"; load_dotenv "$2"; printf changed >"$3"' \
    sh "$DOTENV_GUARD_FIXTURE/deploy/scripts/release-common.sh" "$dotenv_file" "$DOTENV_GUARD_FIXTURE/victim" \
    >"$DOTENV_GUARD_FIXTURE/case-$dotenv_case.log" 2>&1
  dotenv_status=$?
  set -e
  [ "$dotenv_status" -eq 64 ]
  grep -qx unchanged "$DOTENV_GUARD_FIXTURE/victim"
  grep -Eq "^Dotenv parse error: file=.* line=1 category=(invalid-key|forbidden-control-key|malformed-entry)$" \
    "$DOTENV_GUARD_FIXTURE/case-$dotenv_case.log"
  if grep -Eq 'key=|ultra-secret-value|dotenv-secret-value-must-never-leak-91|dotenv-secret-inside-invalid-key-37' \
    "$DOTENV_GUARD_FIXTURE/case-$dotenv_case.log"; then
    printf 'Malformed dotenv diagnostics leaked a value for case %s.\n' "$dotenv_case" >&2
    exit 1
  fi
  rm -f "$dotenv_file"
done <<EOF
PATH=\$(printf pwned >"$DOTENV_GUARD_FIXTURE/victim")
BASH_ENV=ultra-secret-value
ENV=ultra-secret-value
IFS=ultra-secret-value
CDPATH=ultra-secret-value
DEPLOY_RELEASE_LOCK_FILE=ultra-secret-value
IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA=ultra-secret-value
BAD-KEY=ultra-secret-value
BROKEN_KEY ultra-secret-value
$dotenv_secret_value
ILLEGAL-$dotenv_secret_in_key=value
EOF
if grep -R -F 'ultra-secret-value' "$DOTENV_GUARD_FIXTURE" \
  || grep -R -F "$dotenv_secret_value" "$DOTENV_GUARD_FIXTURE" \
  || grep -R -F "$dotenv_secret_in_key" "$DOTENV_GUARD_FIXTURE"; then
  printf 'Dotenv secret material leaked into output or durable test state.\n' >&2
  exit 1
fi
pass 'dotenv parser rejects shell/deploy controls and malformed entries without evaluating or leaking values'

NUMERIC_GUARD_FIXTURE="$ROOT_TMP/numeric-guard"
mkdir -p "$NUMERIC_GUARD_FIXTURE/deploy/scripts"
cp "$DEPLOY_DIR/scripts/release-common.sh" "$NUMERIC_GUARD_FIXTURE/deploy/scripts/release-common.sh"
cp "$DEPLOY_DIR/scripts/postgres-backup.sh" "$NUMERIC_GUARD_FIXTURE/deploy/scripts/postgres-backup.sh"
cp "$DEPLOY_DIR/scripts/export-client-release-history.sh" "$NUMERIC_GUARD_FIXTURE/deploy/scripts/export-client-release-history.sh"
chmod +x \
  "$NUMERIC_GUARD_FIXTURE/deploy/scripts/postgres-backup.sh" \
  "$NUMERIC_GUARD_FIXTURE/deploy/scripts/export-client-release-history.sh"
printf 'unchanged\n' >"$NUMERIC_GUARD_FIXTURE/victim"
for numeric_value in \
  -1 \
  999999999999999999999999999999999999 \
  "1;printf pwned >$NUMERIC_GUARD_FIXTURE/victim"; do
  set +e
  DEPLOY_DIR="$NUMERIC_GUARD_FIXTURE/deploy" sh -c \
    '. "$1"; require_decimal_range TEST_NUMERIC "$2" 1 300' \
    sh "$NUMERIC_GUARD_FIXTURE/deploy/scripts/release-common.sh" "$numeric_value" \
    >"$NUMERIC_GUARD_FIXTURE/range.log" 2>&1
  numeric_status=$?
  set -e
  [ "$numeric_status" -eq 64 ]
  grep -qx unchanged "$NUMERIC_GUARD_FIXTURE/victim"
done
cat >"$NUMERIC_GUARD_FIXTURE/deploy/.env" <<EOF
POSTGRES_READY_ATTEMPTS=1;printf pwned >$NUMERIC_GUARD_FIXTURE/victim
BACKUP_RETENTION_DAYS=14
EOF
set +e
env PATH="$FAKE_BIN:$PATH" sh "$NUMERIC_GUARD_FIXTURE/deploy/scripts/postgres-backup.sh" \
  >"$NUMERIC_GUARD_FIXTURE/postgres.log" 2>&1
postgres_numeric_status=$?
set -e
[ "$postgres_numeric_status" -eq 64 ]
grep -qx unchanged "$NUMERIC_GUARD_FIXTURE/victim"
set +e
env PATH="$FAKE_BIN:$PATH" sh "$NUMERIC_GUARD_FIXTURE/deploy/scripts/export-client-release-history.sh" \
  >"$NUMERIC_GUARD_FIXTURE/export.log" 2>&1
export_numeric_status=$?
set -e
[ "$export_numeric_status" -eq 64 ]
grep -qx unchanged "$NUMERIC_GUARD_FIXTURE/victim"
pass 'formal numeric guards reject payload, negative, and oversized values before arithmetic or find'

ATOMIC_STATE_FIXTURE="$ROOT_TMP/atomic-state"
mkdir -p "$ATOMIC_STATE_FIXTURE"
printf 'old-complete-state\n' >"$ATOMIC_STATE_FIXTURE/current-release.env"
mkfifo "$ATOMIC_STATE_FIXTURE/input.fifo"
DEPLOY_DIR="$ATOMIC_STATE_FIXTURE" sh -c \
  '. "$1"; atomic_write_file "$2" 600 <"$3"' \
  sh "$DEPLOY_DIR/scripts/release-common.sh" \
  "$ATOMIC_STATE_FIXTURE/current-release.env" \
  "$ATOMIC_STATE_FIXTURE/input.fifo" \
  >"$ATOMIC_STATE_FIXTURE/writer.log" 2>&1 &
atomic_writer_parent=$!
(
  printf 'partial-new-state\n'
  sleep 2
  printf 'must-not-promote\n'
) >"$ATOMIC_STATE_FIXTURE/input.fifo" &
atomic_writer_producer=$!
atomic_wait_attempt=0
atomic_writer_child=""
while [ -z "$atomic_writer_child" ] && [ "$atomic_wait_attempt" -lt 100 ]; do
  atomic_writer_child=$(pgrep -P "$atomic_writer_parent" | head -n 1 || true)
  sleep 0.05
  atomic_wait_attempt=$((atomic_wait_attempt + 1))
done
[ -n "$atomic_writer_child" ]
kill -TERM "$atomic_writer_child"
set +e
wait "$atomic_writer_parent"
atomic_writer_status=$?
set -e
kill -TERM "$atomic_writer_producer" 2>/dev/null || true
wait "$atomic_writer_producer" 2>/dev/null || true
[ "$atomic_writer_status" -eq 143 ]
grep -qx old-complete-state "$ATOMIC_STATE_FIXTURE/current-release.env"
if find "$ATOMIC_STATE_FIXTURE" -maxdepth 1 -name '.current-release.env.tmp.*' -print | grep -q .; then
  printf 'Interrupted atomic state writer left a temporary state file.\n' >&2
  exit 1
fi
pass 'TERM during same-directory atomic state write preserves the complete prior state'

RUN_ONE="$ROOT_TMP/run-one"
RUN_TWO="$ROOT_TMP/run-two"
env PATH="$FAKE_BIN:$PATH" FAKE_CALL_LOG="$CALL_LOG" \
  REGISTRY=harbor.test:5000 HARBOR_PROJECT=iiot DEPLOY_ARTIFACT_DIR="$RUN_ONE" \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=run-one \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
  bash "$LOCAL_RELEASE" --services httpapi --ssh-target fake@host --dry-run >/dev/null
env PATH="$FAKE_BIN:$PATH" FAKE_CALL_LOG="$CALL_LOG" \
  REGISTRY=harbor.test:5000 HARBOR_PROJECT=iiot DEPLOY_ARTIFACT_DIR="$RUN_TWO" \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=run-two \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
  bash "$LOCAL_RELEASE" --services dataworker --ssh-target fake@host --dry-run >/dev/null
grep -qx 'CLOUD_DEPLOY_INVOCATION_ID=run-one' "$RUN_ONE/cloud-images.env"
grep -qx 'CLOUD_DEPLOY_SERVICES=httpapi' "$RUN_ONE/cloud-images.env"
grep -qx 'CLOUD_DEPLOY_INVOCATION_ID=run-two' "$RUN_TWO/cloud-images.env"
grep -qx 'CLOUD_DEPLOY_SERVICES=dataworker' "$RUN_TWO/cloud-images.env"
pass 'services and image manifests are isolated by invocation'

expect_failure \
  'remote release rejects release-tag and expected-SHA mismatch' \
  'release tag, expected SHA and approved plan do not match' \
  env PATH="$FAKE_BIN:$PATH" \
    IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
    IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=33333333-3333-3333-3333-333333333333 \
    IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
    IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
    DEPLOY_GIT_SHA="$EXPECTED_SHA" \
    "$DEPLOY_DIR/scripts/deploy-release.sh" "sha-$OTHER_SHA" --services httpapi

expect_failure \
  'remote release rejects DEPLOY_GIT_SHA and expected-SHA mismatch' \
  'DEPLOY_GIT_SHA, release tag and expected SHA do not match' \
  env PATH="$FAKE_BIN:$PATH" \
    IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
    IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=33333333-3333-3333-3333-333333333333 \
    IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
    IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
    DEPLOY_GIT_SHA="$OTHER_SHA" \
    "$DEPLOY_DIR/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi

prepare_remote_fixture() {
  local fixture="$1"
  local cleanup_status="$2"
  local config_digest
  mkdir -p "$fixture/deploy/scripts" "$fixture/deploy/releases/history" "$fixture/deploy/backups/postgres" "$fixture/lock.d" "$fixture/staging"
  cp "$DEPLOY_DIR/scripts/release-common.sh" "$fixture/deploy/scripts/release-common.sh"
  cp "$DEPLOY_DIR/scripts/deploy-release.sh" "$fixture/deploy/scripts/deploy-release.sh"
  sed \
    -e "s#^CLOUD_RELEASE_LOCK_FILE_DEFAULT=.*#CLOUD_RELEASE_LOCK_FILE_DEFAULT=\"$fixture/deploy/.cloud-release.lock\"#" \
    -e "s#^POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT=.*#POST_RELEASE_CLEANUP_LOCK_FILE_DEFAULT=\"$fixture/deploy/.post-release-cleanup.lock\"#" \
    -e "s#^CLOUD_CONFIG_LOCK_FILE_DEFAULT=.*#CLOUD_CONFIG_LOCK_FILE_DEFAULT=\"$fixture/deploy/.cloud-config.lock\"#" \
    "$fixture/deploy/scripts/release-common.sh" \
    >"$fixture/deploy/scripts/release-common.sh.lock-paths"
  mv "$fixture/deploy/scripts/release-common.sh.lock-paths" "$fixture/deploy/scripts/release-common.sh"
  : >"$fixture/deploy/docker-compose.prod.yml"
  : >"$fixture/deploy/.env"
  config_digest="$(DEPLOY_DIR="$fixture/deploy" sh -c \
    '. "$1"; canonical_cloud_config_sha256 "$2"' \
    sh "$fixture/deploy/scripts/release-common.sh" "$fixture/deploy/.env")"
  cat >"$fixture/deploy/scripts/pre-deploy-check.sh" <<'EOF'
#!/bin/sh
printf 'precheck\n' >>"$TEST_CALL_LOG"
EOF
  cat >"$fixture/deploy/scripts/post-deploy-check.sh" <<'EOF'
#!/bin/sh
printf 'postcheck\n' >>"$TEST_CALL_LOG"
if [ "${FAKE_MUTATE_ENV_DURING_POSTCHECK:-0}" = 1 ]; then
  fixture_deploy_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
  printf 'CONCURRENT_OPERATOR_CHANGE=preserve-me\n' >>"$fixture_deploy_dir/.env"
fi
EOF
  cat >"$fixture/deploy/scripts/post-release-cleanup.sh" <<'EOF'
#!/bin/sh
printf 'cleanup\n' >>"$TEST_CALL_LOG"
EOF
  cat >"$fixture/deploy/scripts/postgres-backup.sh" <<'EOF'
#!/bin/sh
printf 'backup\n' >>"$TEST_CALL_LOG"
[ "${FAKE_BACKUP_SUCCESS:-0}" = 1 ] || exit 90
fixture_deploy_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
mkdir -p "$fixture_deploy_dir/backups/postgres"
printf 'fake-backup\n' > "$fixture_deploy_dir/backups/postgres/fake.dump"
printf '%s\n' "$fixture_deploy_dir/backups/postgres/fake.dump" > "$fixture_deploy_dir/backups/postgres/latest-successful-backup.txt"
exit 0
EOF
  chmod +x "$fixture/deploy/scripts/"*.sh
  cat >"$fixture/staging/image.env" <<EOF
CLOUD_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444
CLOUD_DEPLOY_EXPECTED_SHA=$EXPECTED_SHA
CLOUD_DEPLOY_PLAN_DIGEST=$PLAN_DIGEST
CLOUD_DEPLOY_RELEASE_TAG=sha-$EXPECTED_SHA
CLOUD_DEPLOY_SERVICES=httpapi
IIOT_HTTPAPI_IMAGE=harbor.test:5000/iiot/iiot-httpapi:sha-$EXPECTED_SHA
IIOT_HTTPAPI_IMAGE_DIGEST=$OCI_DIGEST
EOF
  image_manifest_digest=$(shasum -a 256 "$fixture/staging/image.env" | awk '{print $1}')
  image_content_digest=$(grep -E '^(CLOUD_DEPLOY_(EXPECTED_SHA|PLAN_DIGEST|RELEASE_TAG|SERVICES)|IIOT_(HTTPAPI|GATEWAY|DATAWORKER|MIGRATION|WEB)_IMAGE(_DIGEST)?)=' "$fixture/staging/image.env" | LC_ALL=C sort | shasum -a 256 | awk '{print $1}')
  cat >"$fixture/deploy/releases/current-release.env" <<EOF
DEPLOY_RELEASE_ID=sha-$EXPECTED_SHA
DEPLOY_GIT_SHA=$EXPECTED_SHA
DEPLOY_PLAN_DIGEST=$PLAN_DIGEST
DEPLOY_SUPPORT_MANIFEST_SHA256=$SUPPORT_DIGEST
DEPLOY_IMAGE_MANIFEST_SHA256=$image_manifest_digest
DEPLOY_IMAGE_CONTENT_SHA256=$image_content_digest
DEPLOY_CONFIG_SHA256=$config_digest
DEPLOY_SERVICES=iiot-httpapi
DEPLOY_CLEANUP_STATUS=$cleanup_status
DEPLOY_PHASE=runtime-healthy-cleanup-$cleanup_status
IIOT_HTTPAPI_IMAGE=harbor.test:5000/iiot/iiot-httpapi:sha-$EXPECTED_SHA
IIOT_GATEWAY_IMAGE=harbor.test:5000/iiot/iiot-gateway:sha-$EXPECTED_SHA
IIOT_DATAWORKER_IMAGE=harbor.test:5000/iiot/iiot-dataworker:sha-$EXPECTED_SHA
IIOT_MIGRATION_IMAGE=harbor.test:5000/iiot/iiot-migrationworkapp:sha-$EXPECTED_SHA
IIOT_WEB_IMAGE=harbor.test:5000/iiot/iiot-web:sha-$EXPECTED_SHA
EOF
  grep -E '^IIOT_(HTTPAPI|GATEWAY|DATAWORKER|MIGRATION|WEB)_IMAGE=' \
    "$fixture/deploy/releases/current-release.env" \
    >"$fixture/deploy/releases/current-images.env"
  support_backup="$fixture/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444"
  mkdir -p "$support_backup"
  cat >"$support_backup/restore-support.sh" <<'EOF'
#!/bin/sh
printf 'called\n' > "$2/restore-called"
exit 0
EOF
  chmod +x "$support_backup/restore-support.sh"
  mkdir -p "$fixture/deploy/releases/transactions"
  cat >"$fixture/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" <<EOF
DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444
DEPLOY_EXPECTED_SHA=$EXPECTED_SHA
DEPLOY_PLAN_DIGEST=$PLAN_DIGEST
DEPLOY_TRANSACTION_PHASE=child-running
EOF
  printf '%s\n' "$$" >"$fixture/lock.d/pid"
  printf '%s\n' "sha-$EXPECTED_SHA" >"$fixture/lock.d/release"
  printf '%s\n' '44444444-4444-4444-4444-444444444444' >"$fixture/lock.d/invocation-id"
  printf '%s\n' "$PLAN_DIGEST" >"$fixture/lock.d/plan-digest"
  printf '%s\n' preflight >"$fixture/lock.d/phase"
  printf '%s\n' deploy-release >"$fixture/lock.d/script"
  printf '%s\n' cloud-release >"$fixture/lock.d/purpose"
  date -u +'%Y-%m-%dT%H:%M:%SZ' >"$fixture/lock.d/created-at"
  printf '%s %s\n' "$image_manifest_digest" "$image_content_digest"
}

run_noop_fixture() {
  local fixture="$1"
  local cleanup_status="$2"
  local image_manifest_digest
  local image_content_digest
  local call_log="$fixture/calls.log"
  read -r image_manifest_digest image_content_digest <<<"$(prepare_remote_fixture "$fixture" "$cleanup_status")"
  : >"$call_log"
  # The wrapper shell creates lock metadata with its own PID and exec preserves it.
  env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$call_log" \
    FIXTURE="$fixture" EXPECTED_SHA="$EXPECTED_SHA" PLAN_DIGEST="$PLAN_DIGEST" \
    SUPPORT_DIGEST="$SUPPORT_DIGEST" IMAGE_DIGEST="$image_manifest_digest" IMAGE_CONTENT_DIGEST="$image_content_digest" \
    sh -c '
      printf "%s\n" "$$" >"$FIXTURE/lock.d/pid"
      exec env \
        IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
        IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
        IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
        IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
        DEPLOY_GIT_SHA="$EXPECTED_SHA" \
        DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
        DEPLOY_RELEASE_LOCK_FILE="$FIXTURE/lock" \
        DEPLOY_SUPPORT_STAGING_DIR="$FIXTURE/staging" \
        DEPLOY_SUPPORT_BACKUP_DIR="$FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
        DEPLOY_TRANSACTION_MARKER="$FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
        DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
        DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
        DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
        EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
      "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
    ' >/dev/null
  [ ! -d "$fixture/deploy/.cloud-config.lock.d" ]
  if grep -qx backup "$call_log"; then
    printf 'No-op path unexpectedly executed a database backup.\n' >&2
    exit 1
  fi
}

NOOP_FIXTURE="$ROOT_TMP/noop"
run_noop_fixture "$NOOP_FIXTURE" complete
grep -qx postcheck "$NOOP_FIXTURE/calls.log"
if grep -qx cleanup "$NOOP_FIXTURE/calls.log"; then
  printf 'Healthy no-op unexpectedly reran cleanup.\n' >&2
  exit 1
fi
pass 'same SHA and plan use the healthy no-op path'

PARTIAL_FIXTURE="$ROOT_TMP/partial"
run_noop_fixture "$PARTIAL_FIXTURE" partial
grep -qx cleanup "$PARTIAL_FIXTURE/calls.log"
grep -qx 'DEPLOY_CLEANUP_STATUS=complete' "$PARTIAL_FIXTURE/deploy/releases/current-release.env"
pass 'partial cleanup resumes cleanup without rerunning rollout or backup'

PENDING_FIXTURE="$ROOT_TMP/pending"
run_noop_fixture "$PENDING_FIXTURE" pending
grep -qx cleanup "$PENDING_FIXTURE/calls.log"
grep -qx 'DEPLOY_CLEANUP_STATUS=complete' "$PENDING_FIXTURE/deploy/releases/current-release.env"
pass 'pending cleanup resumes cleanup only without rerunning rollout or backup'

CONFIG_DRIFT_FIXTURE="$ROOT_TMP/config-drift"
read -r config_image_manifest_digest config_image_content_digest \
  <<<"$(prepare_remote_fixture "$CONFIG_DRIFT_FIXTURE" complete)"
cat >>"$CONFIG_DRIFT_FIXTURE/deploy/.env" <<'EOF'
GATEWAY_HTTP_PORT=18080
DATAWORKER_BATCH_SIZE=37
OIDC_CLIENT_SECRET=never-print-this-secret
EOF
: >"$CONFIG_DRIFT_FIXTURE/calls.log"
env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$CONFIG_DRIFT_FIXTURE/calls.log" \
  FAKE_BACKUP_SUCCESS=1 \
  FIXTURE="$CONFIG_DRIFT_FIXTURE" EXPECTED_SHA="$EXPECTED_SHA" PLAN_DIGEST="$PLAN_DIGEST" \
  SUPPORT_DIGEST="$SUPPORT_DIGEST" IMAGE_DIGEST="$config_image_manifest_digest" \
  IMAGE_CONTENT_DIGEST="$config_image_content_digest" \
  sh -c '
    printf "%s\n" "$$" >"$FIXTURE/lock.d/pid"
    exec env \
      IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
      IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
      IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
      IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
      DEPLOY_GIT_SHA="$EXPECTED_SHA" \
      DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
      DEPLOY_RELEASE_LOCK_FILE="$FIXTURE/lock" \
      DEPLOY_SUPPORT_STAGING_DIR="$FIXTURE/staging" \
      DEPLOY_SUPPORT_BACKUP_DIR="$FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
      DEPLOY_TRANSACTION_MARKER="$FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
      DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
      DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
      DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
      EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
      "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
  ' >"$CONFIG_DRIFT_FIXTURE/output.log" 2>&1
grep -qx backup "$CONFIG_DRIFT_FIXTURE/calls.log"
expected_config_digest="$(DEPLOY_DIR="$CONFIG_DRIFT_FIXTURE/deploy" sh -c \
  '. "$1"; canonical_cloud_config_sha256 "$2"' \
  sh "$CONFIG_DRIFT_FIXTURE/deploy/scripts/release-common.sh" "$CONFIG_DRIFT_FIXTURE/deploy/.env")"
grep -qx "DEPLOY_CONFIG_SHA256=$expected_config_digest" \
  "$CONFIG_DRIFT_FIXTURE/deploy/releases/current-release.env"
if grep -Fq 'never-print-this-secret' "$CONFIG_DRIFT_FIXTURE/output.log"; then
  printf 'Canonical Cloud configuration hashing leaked a secret value.\n' >&2
  exit 1
fi
pass 'secret, port, and worker configuration drift bypasses no-op without exposing configuration values'

ENV_RACE_FIXTURE="$ROOT_TMP/env-race"
read -r env_race_image_digest env_race_content_digest \
  <<<"$(prepare_remote_fixture "$ENV_RACE_FIXTURE" complete)"
awk '
  /^DEPLOY_IMAGE_CONTENT_SHA256=/ {
    print "DEPLOY_IMAGE_CONTENT_SHA256=0000000000000000000000000000000000000000000000000000000000000000"
    next
  }
  { print }
' "$ENV_RACE_FIXTURE/deploy/releases/current-release.env" \
  >"$ENV_RACE_FIXTURE/deploy/releases/current-release.env.new"
mv "$ENV_RACE_FIXTURE/deploy/releases/current-release.env.new" \
  "$ENV_RACE_FIXTURE/deploy/releases/current-release.env"
: >"$ENV_RACE_FIXTURE/calls.log"
set +e
env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$ENV_RACE_FIXTURE/calls.log" \
  FAKE_BACKUP_SUCCESS=1 FAKE_MUTATE_ENV_DURING_POSTCHECK=1 \
  FIXTURE="$ENV_RACE_FIXTURE" EXPECTED_SHA="$EXPECTED_SHA" PLAN_DIGEST="$PLAN_DIGEST" \
  SUPPORT_DIGEST="$SUPPORT_DIGEST" IMAGE_DIGEST="$env_race_image_digest" \
  IMAGE_CONTENT_DIGEST="$env_race_content_digest" \
  sh -c '
    printf "%s\n" "$$" >"$FIXTURE/lock.d/pid"
    exec env \
      IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
      IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
      IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
      IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
      DEPLOY_GIT_SHA="$EXPECTED_SHA" \
      DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
      DEPLOY_RELEASE_LOCK_FILE="$FIXTURE/lock" \
      DEPLOY_SUPPORT_STAGING_DIR="$FIXTURE/staging" \
      DEPLOY_SUPPORT_BACKUP_DIR="$FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
      DEPLOY_TRANSACTION_MARKER="$FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
      DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
      DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
      DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
      EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
      "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
  ' >"$ENV_RACE_FIXTURE/output.log" 2>&1
env_race_status=$?
set -e
[ "$env_race_status" -eq 75 ]
grep -qx 'CONCURRENT_OPERATOR_CHANGE=preserve-me' "$ENV_RACE_FIXTURE/deploy/.env"
grep -qx 'DEPLOY_CLEANUP_STATUS=complete' "$ENV_RACE_FIXTURE/deploy/releases/current-release.env"
if find "$ENV_RACE_FIXTURE/deploy" -type f -name '*.tmp.*' -print | grep -q .; then
  printf 'Concurrent .env promotion failure left an atomic temporary file.\n' >&2
  exit 1
fi
pass 'promotion revalidates the original env digest and preserves concurrent operator changes'

CONFIG_LOCK_RACE_FIXTURE="$ROOT_TMP/config-lock-race"
read -r config_lock_race_image_digest config_lock_race_content_digest \
  <<<"$(prepare_remote_fixture "$CONFIG_LOCK_RACE_FIXTURE" complete)"
cp "$DEPLOY_DIR/scripts/update-deploy-env.sh" "$CONFIG_LOCK_RACE_FIXTURE/deploy/scripts/update-deploy-env.sh"
chmod +x "$CONFIG_LOCK_RACE_FIXTURE/deploy/scripts/update-deploy-env.sh"
awk '
  /^DEPLOY_IMAGE_CONTENT_SHA256=/ {
    print "DEPLOY_IMAGE_CONTENT_SHA256=0000000000000000000000000000000000000000000000000000000000000000"
    next
  }
  { print }
' "$CONFIG_LOCK_RACE_FIXTURE/deploy/releases/current-release.env" \
  >"$CONFIG_LOCK_RACE_FIXTURE/deploy/releases/current-release.env.new"
mv "$CONFIG_LOCK_RACE_FIXTURE/deploy/releases/current-release.env.new" \
  "$CONFIG_LOCK_RACE_FIXTURE/deploy/releases/current-release.env"
cat >"$CONFIG_LOCK_RACE_FIXTURE/deploy/competitor.env" <<'EOF'
SUPPORTED_COMPETITOR_VALUE=must-not-win
EOF
cat >"$CONFIG_LOCK_RACE_FIXTURE/config-lock-hook.sh" <<'EOF'
#!/bin/sh
set +e
"$1/scripts/update-deploy-env.sh" "$1/competitor.env" "$2" \
  >"$1/competitor.log" 2>&1
competitor_status=$?
set -e
printf '%s\n' "$competitor_status" >"$1/competitor.status"
[ "$competitor_status" -eq 75 ]
EOF
chmod +x "$CONFIG_LOCK_RACE_FIXTURE/config-lock-hook.sh"
config_lock_original_env_sha=$(shasum -a 256 "$CONFIG_LOCK_RACE_FIXTURE/deploy/.env" | awk '{print $1}')
: >"$CONFIG_LOCK_RACE_FIXTURE/calls.log"
set +e
env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$CONFIG_LOCK_RACE_FIXTURE/calls.log" \
  FAKE_BACKUP_SUCCESS=1 \
  FIXTURE="$CONFIG_LOCK_RACE_FIXTURE" EXPECTED_SHA="$EXPECTED_SHA" PLAN_DIGEST="$PLAN_DIGEST" \
  SUPPORT_DIGEST="$SUPPORT_DIGEST" IMAGE_DIGEST="$config_lock_race_image_digest" \
  IMAGE_CONTENT_DIGEST="$config_lock_race_content_digest" \
  sh -c '
    printf "%s\n" "$$" >"$FIXTURE/lock.d/pid"
    exec env \
      DEPLOY_CONFIG_LOCK_TEST_MODE=enabled \
      DEPLOY_CONFIG_LOCK_TEST_HOOK="$FIXTURE/config-lock-hook.sh" \
      IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
      IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
      IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
      IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
      DEPLOY_GIT_SHA="$EXPECTED_SHA" \
      DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
      DEPLOY_RELEASE_LOCK_FILE="$FIXTURE/lock" \
      DEPLOY_SUPPORT_STAGING_DIR="$FIXTURE/staging" \
      DEPLOY_SUPPORT_BACKUP_DIR="$FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
      DEPLOY_TRANSACTION_MARKER="$FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
      DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
      DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
      DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
      EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
      "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
  ' >"$CONFIG_LOCK_RACE_FIXTURE/output.log" 2>&1
config_lock_race_status=$?
set -e
[ "$config_lock_race_status" -eq 0 ]
grep -qx 75 "$CONFIG_LOCK_RACE_FIXTURE/deploy/competitor.status"
[ "$(shasum -a 256 "$CONFIG_LOCK_RACE_FIXTURE/deploy/.env" | awk '{print $1}')" = "$config_lock_original_env_sha" ]
if grep -Fq 'SUPPORTED_COMPETITOR_VALUE=must-not-win' "$CONFIG_LOCK_RACE_FIXTURE/deploy/.env"; then
  printf 'Competing supported config update overwrote operator configuration while deploy held the lock.\n' >&2
  exit 1
fi
grep -qx "IIOT_HTTPAPI_IMAGE=harbor.test:5000/iiot/iiot-httpapi:sha-$EXPECTED_SHA" \
  "$CONFIG_LOCK_RACE_FIXTURE/deploy/releases/current-images.env"
[ ! -d "$CONFIG_LOCK_RACE_FIXTURE/deploy/.cloud-config.lock.d" ]
grep -Fq 'Strict managed lock already exists and is never auto-removed' \
  "$CONFIG_LOCK_RACE_FIXTURE/deploy/competitor.log"
pass 'final-hash competing supported config update loses the strict lock without losing operator values'

ROLLBACK_CONFIG_LOCK_FIXTURE="$ROOT_TMP/rollback-config-lock"
mkdir -p "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts" "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/releases/history"
sed \
  -e "s#^CLOUD_RELEASE_LOCK_FILE_DEFAULT=.*#CLOUD_RELEASE_LOCK_FILE_DEFAULT=\"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.cloud-release.lock\"#" \
  -e "s#^CLOUD_CONFIG_LOCK_FILE_DEFAULT=.*#CLOUD_CONFIG_LOCK_FILE_DEFAULT=\"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.cloud-config.lock\"#" \
  "$DEPLOY_DIR/scripts/release-common.sh" \
  >"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts/release-common.sh"
cp "$DEPLOY_DIR/scripts/rollback-release.sh" "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts/rollback-release.sh"
cp "$DEPLOY_DIR/scripts/update-deploy-env.sh" "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts/update-deploy-env.sh"
cat >"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts/post-deploy-check.sh" <<'EOF'
#!/bin/sh
exit 0
EOF
chmod +x "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts/"*.sh
printf 'ORIGINAL_ENV=before-rollback\n' >"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.env"
rollback_original_env_sha=$(shasum -a 256 "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.env" | awk '{print $1}')
cat >"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/releases/current-release.env" <<EOF
DEPLOY_RELEASE_ID=sha-$EXPECTED_SHA
EOF
cat >"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/releases/target.env" <<EOF
DEPLOY_RELEASE_ID=sha-$OTHER_SHA
DEPLOY_GIT_SHA=$OTHER_SHA
IIOT_HTTPAPI_IMAGE=harbor.test:5000/iiot/iiot-httpapi:sha-$OTHER_SHA
IIOT_GATEWAY_IMAGE=harbor.test:5000/iiot/iiot-gateway:sha-$OTHER_SHA
IIOT_DATAWORKER_IMAGE=harbor.test:5000/iiot/iiot-dataworker:sha-$OTHER_SHA
IIOT_MIGRATION_IMAGE=harbor.test:5000/iiot/iiot-migrationworkapp:sha-$OTHER_SHA
IIOT_WEB_IMAGE=harbor.test:5000/iiot/iiot-web:sha-$OTHER_SHA
EOF
cat >"$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/competitor.env" <<'EOF'
ROLLBACK_SUPPORTED_COMPETITOR_VALUE=must-not-win
EOF
cat >"$ROLLBACK_CONFIG_LOCK_FIXTURE/config-lock-hook.sh" <<'EOF'
#!/bin/sh
set +e
"$1/scripts/update-deploy-env.sh" "$1/competitor.env" "$2" \
  >"$1/competitor.log" 2>&1
competitor_status=$?
set -e
printf '%s\n' "$competitor_status" >"$1/competitor.status"
[ "$competitor_status" -eq 75 ]
EOF
chmod +x "$ROLLBACK_CONFIG_LOCK_FIXTURE/config-lock-hook.sh"
set +e
env PATH="$FAKE_BIN:$PATH" \
  DEPLOY_CONFIG_LOCK_TEST_MODE=enabled \
  DEPLOY_CONFIG_LOCK_TEST_HOOK="$ROLLBACK_CONFIG_LOCK_FIXTURE/config-lock-hook.sh" \
  sh "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/scripts/rollback-release.sh" \
    "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/releases/target.env" \
  >"$ROLLBACK_CONFIG_LOCK_FIXTURE/output.log" 2>&1
rollback_config_lock_status=$?
set -e
[ "$rollback_config_lock_status" -eq 0 ]
grep -qx 75 "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/competitor.status"
[ "$(shasum -a 256 "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.env" | awk '{print $1}')" = "$rollback_original_env_sha" ]
if grep -Fq 'ROLLBACK_SUPPORTED_COMPETITOR_VALUE=must-not-win' "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.env"; then
  printf 'Competing supported config update overwrote operator configuration while rollback held the lock.\n' >&2
  exit 1
fi
grep -qx "DEPLOY_RELEASE_ID=sha-$OTHER_SHA" "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/releases/current-release.env"
grep -qx "IIOT_HTTPAPI_IMAGE=harbor.test:5000/iiot/iiot-httpapi:sha-$OTHER_SHA" \
  "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/releases/current-images.env"
[ ! -d "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.cloud-release.lock.d" ]
[ ! -d "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/.cloud-config.lock.d" ]
grep -Fq 'Strict managed lock already exists and is never auto-removed' \
  "$ROLLBACK_CONFIG_LOCK_FIXTURE/deploy/competitor.log"
pass 'rollback holds the strict config lock and promotes release images without overwriting operator values'

OCI_DRIFT_FIXTURE="$ROOT_TMP/runtime-oci-drift"
read -r oci_image_manifest_digest oci_image_content_digest \
  <<<"$(prepare_remote_fixture "$OCI_DRIFT_FIXTURE" complete)"
: >"$OCI_DRIFT_FIXTURE/calls.log"
env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$OCI_DRIFT_FIXTURE/calls.log" \
  FAKE_BACKUP_SUCCESS=1 \
  FAKE_RUNNING_OCI_DIGEST=sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee \
  FIXTURE="$OCI_DRIFT_FIXTURE" EXPECTED_SHA="$EXPECTED_SHA" PLAN_DIGEST="$PLAN_DIGEST" \
  SUPPORT_DIGEST="$SUPPORT_DIGEST" IMAGE_DIGEST="$oci_image_manifest_digest" \
  IMAGE_CONTENT_DIGEST="$oci_image_content_digest" \
  sh -c '
    printf "%s\n" "$$" >"$FIXTURE/lock.d/pid"
    exec env \
      IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
      IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
      IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
      IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
      DEPLOY_GIT_SHA="$EXPECTED_SHA" \
      DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
      DEPLOY_RELEASE_LOCK_FILE="$FIXTURE/lock" \
      DEPLOY_SUPPORT_STAGING_DIR="$FIXTURE/staging" \
      DEPLOY_SUPPORT_BACKUP_DIR="$FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
      DEPLOY_TRANSACTION_MARKER="$FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
      DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
      DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
      DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
      EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
      "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
  ' >"$OCI_DRIFT_FIXTURE/output.log" 2>&1
grep -qx backup "$OCI_DRIFT_FIXTURE/calls.log"
grep -qx 'DEPLOY_CLEANUP_STATUS=complete' "$OCI_DRIFT_FIXTURE/deploy/releases/current-release.env"
pass 'running-container OCI digest drift bypasses no-op and performs a normal rollout'

ROLLOUT_FAILURE_FIXTURE="$ROOT_TMP/rollout-failure"
read -r rollout_image_manifest_digest rollout_image_content_digest \
  <<<"$(prepare_remote_fixture "$ROLLOUT_FAILURE_FIXTURE" complete)"
awk '
  /^DEPLOY_IMAGE_CONTENT_SHA256=/ {
    print "DEPLOY_IMAGE_CONTENT_SHA256=0000000000000000000000000000000000000000000000000000000000000000"
    next
  }
  { print }
' "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/current-release.env" \
  >"$ROLLOUT_FAILURE_FIXTURE/deploy/releases/current-release.env.new"
mv "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/current-release.env.new" \
  "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/current-release.env"
: >"$ROLLOUT_FAILURE_FIXTURE/calls.log"
set +e
env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$ROLLOUT_FAILURE_FIXTURE/calls.log" \
  FAKE_BACKUP_SUCCESS=1 FAKE_COMPOSE_UP_FAIL=1 \
  FIXTURE="$ROLLOUT_FAILURE_FIXTURE" EXPECTED_SHA="$EXPECTED_SHA" PLAN_DIGEST="$PLAN_DIGEST" \
  SUPPORT_DIGEST="$SUPPORT_DIGEST" IMAGE_DIGEST="$rollout_image_manifest_digest" \
  IMAGE_CONTENT_DIGEST="$rollout_image_content_digest" \
  sh -c '
    printf "%s\n" "$$" >"$FIXTURE/lock.d/pid"
    exec env \
      IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
      IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
      IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
      IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
      DEPLOY_GIT_SHA="$EXPECTED_SHA" \
      DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
      DEPLOY_RELEASE_LOCK_FILE="$FIXTURE/lock" \
      DEPLOY_SUPPORT_STAGING_DIR="$FIXTURE/staging" \
      DEPLOY_SUPPORT_BACKUP_DIR="$FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
      DEPLOY_TRANSACTION_MARKER="$FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
      DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
      DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
      DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
      EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
      "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
  ' >"$ROLLOUT_FAILURE_FIXTURE/output.log" 2>&1
rollout_failure_status=$?
set -e
if [ "$rollout_failure_status" -ne 55 ]; then
  printf 'Partial rollout failure did not preserve the compose failure status: %s\n' "$rollout_failure_status" >&2
  sed -n '1,200p' "$ROLLOUT_FAILURE_FIXTURE/output.log" >&2
  exit 1
fi
grep -qx 'DEPLOY_PHASE=blocked-partial-rollout-support-restored' \
  "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/deploy-blocked.env"
grep -qx 'DEPLOY_SUPPORT_RESTORE_STATUS=restored' \
  "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/deploy-blocked.env"
[ -f "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444/restore-called" ]
[ -d "$ROLLOUT_FAILURE_FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" ]
[ ! -d "$ROLLOUT_FAILURE_FIXTURE/lock.d" ]
pass 'partial rollout restores support, preserves durable recovery evidence, and releases only its own lock'

WRONG_LOCK_FIXTURE="$ROOT_TMP/wrong-lock-owner"
read -r wrong_lock_image_digest wrong_lock_content_digest \
  <<<"$(prepare_remote_fixture "$WRONG_LOCK_FIXTURE" complete)"
printf '999999\n' >"$WRONG_LOCK_FIXTURE/lock.d/pid"
set +e
env PATH="$FAKE_BIN:$PATH" TEST_CALL_LOG="$WRONG_LOCK_FIXTURE/calls.log" \
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=44444444-4444-4444-4444-444444444444 \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$EXPECTED_SHA" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$PLAN_DIGEST" \
  DEPLOY_GIT_SHA="$EXPECTED_SHA" \
  DEPLOY_RELEASE_LOCK_PREACQUIRED=1 \
  DEPLOY_RELEASE_LOCK_FILE="$WRONG_LOCK_FIXTURE/lock" \
  DEPLOY_SUPPORT_STAGING_DIR="$WRONG_LOCK_FIXTURE/staging" \
  DEPLOY_SUPPORT_BACKUP_DIR="$WRONG_LOCK_FIXTURE/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444" \
  DEPLOY_TRANSACTION_MARKER="$WRONG_LOCK_FIXTURE/deploy/releases/transactions/44444444-4444-4444-4444-444444444444.env" \
  DEPLOY_IMAGE_MANIFEST="$WRONG_LOCK_FIXTURE/staging/image.env" \
  DEPLOY_IMAGE_MANIFEST_SHA256="$wrong_lock_image_digest" \
  DEPLOY_IMAGE_CONTENT_SHA256="$wrong_lock_content_digest" \
  EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
  "$WRONG_LOCK_FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi \
  >"$WRONG_LOCK_FIXTURE/output.log" 2>&1
wrong_lock_status=$?
set -e
[ "$wrong_lock_status" -eq 64 ]
grep -Fq 'pre-acquired release lock is not owned by the remote transaction process' \
  "$WRONG_LOCK_FIXTURE/output.log"
[ -d "$WRONG_LOCK_FIXTURE/lock.d" ]
grep -qx 999999 "$WRONG_LOCK_FIXTURE/lock.d/pid"
pass 'wrong lock ownership is rejected without releasing another transaction lock'

printf 'Cloud deployment behavior tests passed: %s\n' "$PASS_COUNT"
