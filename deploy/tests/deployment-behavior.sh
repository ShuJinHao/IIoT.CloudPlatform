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
  *"rev-parse --verify refs/remotes/origin/main"*) printf '%s\\n' "$EXPECTED_SHA" ;;
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
  printf '["harbor.test:5000/iiot/iiot-httpapi@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]\n'
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
  mkdir -p "$fixture/deploy/scripts" "$fixture/deploy/releases/history" "$fixture/deploy/backups/postgres" "$fixture/lock.d" "$fixture/staging"
  cp "$DEPLOY_DIR/scripts/release-common.sh" "$fixture/deploy/scripts/release-common.sh"
  cp "$DEPLOY_DIR/scripts/deploy-release.sh" "$fixture/deploy/scripts/deploy-release.sh"
  : >"$fixture/deploy/docker-compose.prod.yml"
  : >"$fixture/deploy/.env"
  cat >"$fixture/deploy/scripts/pre-deploy-check.sh" <<'EOF'
#!/bin/sh
printf 'precheck\n' >>"$TEST_CALL_LOG"
EOF
  cat >"$fixture/deploy/scripts/post-deploy-check.sh" <<'EOF'
#!/bin/sh
printf 'postcheck\n' >>"$TEST_CALL_LOG"
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
DEPLOY_SERVICES=iiot-httpapi
DEPLOY_CLEANUP_STATUS=$cleanup_status
DEPLOY_PHASE=runtime-healthy-cleanup-$cleanup_status
IIOT_HTTPAPI_IMAGE=harbor.test:5000/iiot/iiot-httpapi:sha-$EXPECTED_SHA
IIOT_GATEWAY_IMAGE=harbor.test:5000/iiot/iiot-gateway:sha-$EXPECTED_SHA
IIOT_DATAWORKER_IMAGE=harbor.test:5000/iiot/iiot-dataworker:sha-$EXPECTED_SHA
IIOT_MIGRATION_IMAGE=harbor.test:5000/iiot/iiot-migrationworkapp:sha-$EXPECTED_SHA
IIOT_WEB_IMAGE=harbor.test:5000/iiot/iiot-web:sha-$EXPECTED_SHA
EOF
  support_backup="$fixture/deploy/releases/support-recovery/44444444-4444-4444-4444-444444444444"
  mkdir -p "$support_backup"
  cat >"$support_backup/restore-support.sh" <<'EOF'
#!/bin/sh
printf 'called\n' > "$2/restore-called"
exit 0
EOF
  chmod +x "$support_backup/restore-support.sh"
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
        DEPLOY_IMAGE_MANIFEST="$FIXTURE/staging/image.env" \
        DEPLOY_IMAGE_MANIFEST_SHA256="$IMAGE_DIGEST" \
        DEPLOY_IMAGE_CONTENT_SHA256="$IMAGE_CONTENT_DIGEST" \
        EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256="$SUPPORT_DIGEST" \
        "$FIXTURE/deploy/scripts/deploy-release.sh" "sha-$EXPECTED_SHA" --services httpapi
    ' >/dev/null
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

printf 'Cloud deployment behavior tests passed: %s\n' "$PASS_COUNT"
