# IIoT Cloud Operations

三项目上传部署统一入口见 [上传部署总览](../../docs/上传部署总览.md)。

Cloud 日常应用发布使用工作区 `pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud`，自动 push Git、读取生产 SHA 并只选择受影响镜像；`Deploy.ps1 -Target Cloud -Services ...` 是其内部执行器与恢复入口。稳定 Runner 一次安装后不会在应用发布中被替换；日常事务只做必要数据库备份、选中镜像 pull、`--no-deps` 应用更新、健康检查和失败回滚。本文后续旧 `current-release`、support transaction 和 cleanup 说明继续用于基础设施维护与灾备诊断。

> Current status (2026-07-10): the workspace contract, fresh remote-tip/expected-SHA binding, per-invocation manifests, build-before-support ordering, parent-owned transaction lock, promotion-proof cleanup-only recovery, bounded installer/release process groups, read-only local lock precheck, secret-free dotenv categories, bounded decimal inputs, a strict shared operator-config lock, operator `.env` / release-image state separation, atomic release-state replacement, OCI/config digest verification, healthy no-op, and durable old-transaction evidence passed 33/33 isolated fake behavior tests. `DeploymentGuardTests` passed 20/20 and the `~Deploy` gate passed 39/39. No network, Harbor, SSH, or real production deploy/history/cleanup/health closure ran in this round; this guide is a target contract, not production-acceptance evidence.

## Scope

This document defines the minimum operations baseline for the current single-machine production starter.
It does not add TLS, multi-machine failover, external notifications, or a DLQ management UI.

Operational data ownership stays explicit:

- PostgreSQL is the authoritative business data store and must be backed up.
- Redis is treated as cache and can be rebuilt.
- Seq is useful for troubleshooting but is not required for database recovery.
- RabbitMQ queue state is not covered by the same backup guarantee as PostgreSQL.

## Release State Directory

Application release state is stored only on the server under `deploy/releases/`:

```text
releases/
  routine-current.env
  routine-history/
    <invocation-id>.env
    <invocation-id>.failed.env
  routine-incoming/
  current-release.env
  current-release.summary.md
  current-images.env
  previous-release.env
  staged-release.env
  staged-images.env
  history/
    <timestamp>-<release-id>.env
    <timestamp>-<release-id>.summary.md
```

Each release record keeps only:

- the 5 application image coordinates
- `DEPLOY_RELEASE_ID`
- `DEPLOY_GIT_SHA`
- `DEPLOY_TRIGGERED_BY`
- `DEPLOYED_AT_UTC`
- `PRE_DEPLOY_BACKUP_FILE`
- a markdown summary with deployed services and git changes

It does not duplicate database passwords or other runtime secrets.

Routine non-root contract:

- `deploy/releases/` and `deploy/releases/history/` must stay readable, writable, and searchable by the standard deploy user.
- Existing `current-release.env`, `previous-release.env`, `staged-release.env`, `current-release.summary.md`, `current-images.env`, and `staged-images.env` must stay readable and writable by the standard deploy user.
- If a root emergency path touched release-state files, restore owner/mode before the next routine deploy.

Operator configuration updates must use the shared strict lock:

```sh
expected_env_sha256=$(sha256sum .env | awk '{print $1}')
./scripts/update-deploy-env.sh /secure/operator/cloud.env "$expected_env_sha256"
```

The candidate is parsed before replacement, the current digest is rechecked after the lock is acquired, and replacement is atomic. Exit `75` means either the strict config lock already exists or the expected digest is stale. Do not delete the lock directory automatically: inspect its `pid`, `process-start`, `purpose`, `release`, `phase`, `script`, and `created-at` evidence first. Direct writes to `.env` cannot participate in mutual exclusion and are therefore unsupported.

## Internal Health Probe

The internal application health probe for this batch is:

- `GET /internal/healthz`

Behavior:

- It verifies `nginx -> iiot-gateway -> iiot-httpapi`.
- It returns `200` only when HttpApi is responsive and PostgreSQL is reachable.
- It does not include Redis, RabbitMQ, or DataWorker health in the result.

`/internal/healthz` is necessary but is not sufficient production release acceptance. A release selecting `iiot-httpapi`, `iiot-gateway`, `iiot-web`, or all services must also pass the fail-closed Edge installer catalog and Velopack static-download verification performed by `deploy-release.sh`; `skipped` is a failure for that service scope. Final acceptance also requires the workspace deploy summary, remote release/history, cleanup result, selected-service runtime state, and target-specific post-deploy checks to close successfully.

Access policy:

- The nginx template only allows `127.0.0.1` and `::1`.
- Any non-local source is denied.
- This route is for server-local rollout and manual operations only.

Example:

```sh
gateway_http_port=$(sed -n 's/^GATEWAY_HTTP_PORT=//p' .env | tail -n 1)
gateway_http_port=${gateway_http_port:-80}
curl --silent --show-error --output /dev/null --write-out '%{http_code}\n' "http://127.0.0.1:${gateway_http_port}/internal/healthz"
```

## Standard Release

Routine application release is driven from the operator workstation: run `pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud`; it validates and pushes Git, reads the production baseline, computes the affected service closure, builds the fetched remote tip, pushes immutable images to Harbor, then sends one digest-bound request to the stable server runner. `RemoteTransport=Auto` uses SSH when its TCP endpoint is reachable and otherwise dispatches `cloud-routine-request.yml` to the production self-hosted runner; both transports consume the same request and stable runner. The older workspace transaction remains for deployment-infrastructure maintenance.

Routine deployment red lines:

- Do not wait for `cloud-image` or `cloud-deploy` for routine deployment. Those workflows are emergency-only and require confirmation inputs.
- Use `pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud`. `Deploy.ps1 -Target Cloud -Services ...` is the internal executor and explicit recovery path; `-All` is not a routine fallback. The project-local `local-release.sh` is a legacy maintenance implementation, not a second operator entrypoint.
- If a local build, Harbor operation, or SSH deploy exceeds its timeout, stop and diagnose; do not keep watching until a GitHub job or shell command times out.
- Do not invoke the project-local script directly to work around an AI permission prompt. The routine entrypoint exposes stable phases and one SSH transaction.
- Before retrying an interrupted or failed release, inspect the remote current release, container images, managed release/cleanup/config locks, and cleanup summary. A running target SHA must not be blindly redeployed.
- Operator `.env` updates are supported only through `scripts/update-deploy-env.sh <candidate-env-file> <expected-current-sha256>`. Deployment, rollback, and that updater hold the same strict config lock; any existing config lock fails closed with `75` and is never removed by stale-lock automation.
- Deployment and rollback never replace `.env`. Release-owned application images are atomically promoted through `releases/staged-images.env` to `releases/current-images.env`, which Docker Compose reads after the operator `.env` as an override layer. Direct `.env` editing bypasses the lock and is unsupported.

The standard sequence is:

1. Push or merge to `main`.
2. `cloud-ci` runs the fast gate by default: restore/build, ServiceLayer tests, ConfigurationGuard tests, deploy script syntax checks, web build, and compose config. Full EndToEnd is manual via `workflow_dispatch`.
3. Run `pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud` from the workspace root. It pushes the committed main branch, reads production state, computes affected services, then invokes the detached immutable build and stable runner request.
4. `local-release.sh` packages and syntax-checks only the allowlisted support files locally, then `build-and-push.sh` builds the selected `sha-<git-sha>` images. Every invocation owns its services file and image manifest; the manifest binds invocation, plan, tag, services, image references, and OCI digests.
5. Only after all image builds succeed does `local-release.sh` stage support plus the run-bound image manifest remotely. Before lock acquisition, `workspace-release-transaction.sh` scans incomplete transaction markers, lock metadata, staging, and recovery directories. Any orphan state after SIGKILL, OOM, or reboot is converted to durable blocked evidence and returns `78`; it is never silently removed as a stale lock. The parent writes an atomic transaction marker before any support mutation.
6. The parent owns the invocation/plan-bound lock and launches both installer and release child in isolated sessions/process groups through `env -i`. HUP/INT/TERM is sent to the whole group, followed by bounded TERM grace and KILL escalation; restore and lock release are prohibited until no descendant remains. Once current state and marker prove promotion, cleanup must never restore old support. A parent kill after the promotion marker leaves the new support/current/runtime consistent, and the next matching invocation performs transaction cleanup only.
7. Support installation preflights the staged candidate before mutation, backs up the union of new and previous allowlists including missing-file state, and restores the previous manifest atomically on failure. Old manifest traversal, protected paths, and symlinked targets are rejected. `.env`, `certs/`, `releases/`, and `backups/` remain excluded from support synchronization.
8. `deploy-release.sh` requires the parent-owned transaction marker/lock and verifies OCI digests. Deploy and rollback acquire the strict config lock before reading `.env`, keep it through rollout and final release-image promotion, and never overwrite operator configuration. A supported competing updater loses the lock with `75`; a detected unsupported direct edit also aborts without being overwritten. Release-owned images and current/previous/staged manifests, summaries, history, and backup/verify state use same-directory temporary files plus atomic rename. Dotenv parse errors contain only file, line, and a fixed category, never the offending key/value/token.

Harbor application repositories keep only the current production `sha-*` tag. Local image build must not remove the current production tag before deploy health checks pass. Post-release cleanup deletes old application `sha-*` tags and Harbor Garbage Collection reclaims disk.

The self-hosted runner must not run as root if emergency workflows are used. See `RUNNER.md` for the required `github-runner` user, `iiot-linux-prod` label, Docker group, and server reachability checks.

Application images and infrastructure images must already exist in Harbor before deploy. Docker Hub is not a production dependency source; `pre-deploy-check.sh` rejects Docker Hub shorthand infrastructure image values.

Direct server-side invocation is no longer a release entrypoint, including emergencies. The legacy implementation file `./scripts/deploy-release.sh` requires the workspace-generated invocation contract, run-bound image manifest, OCI digests, and pre-acquired transaction lock; manually reconstructing those values is prohibited. Restore workstation/Runner access and resume through the workspace entrypoint. The legacy GitHub deploy workflow is not compatible with this contract and must not be used until it is separately migrated and revalidated.

Incremental release requires an existing `current-release.env`; first deployment must be full. The script preserves image coordinates for services not included in `--services` by reading the current release manifest before rewriting selected services.

Release rules are fixed:

- Standard production version input is `release_tag = sha-*`.
- First deployment or a post-clean rebuild may not have `current-release.env` yet.
- `latest` is not a standard production application version in this batch.

Release flow is fixed:

1. acquire the managed Cloud release lock, then run `pre-deploy-check.sh`; live/initializing release or cleanup locks fail immediately, while only proven stale locks are removed
2. `postgres-backup.sh`
3. write `staged-release.env`
4. construct a private rollout env from operator `.env` plus selected application image coordinates (`--services` empty means all five; incremental keeps unselected images from `current-release.env`); never replace operator `.env`
5. `docker compose pull` selected application services from Harbor
6. keep infrastructure available
7. run `iiot-migration` only when migration is part of the selected service set
8. start selected application containers, and start or restart `nginx-gateway` when the selected release affects browser traffic
9. `post-deploy-check.sh`
10. atomically promote `staged-images.env` to `current-images.env`, promote the healthy runtime to `current`, rotate `previous`, and write the base current/history state
11. stream post-release cleanup live: remove Docker/BuildKit build cache, report and clean Docker-managed images separately from containerd-managed content, remove only local old application images that are not referenced by current containers, delete old Harbor application tags, and run or confirm Harbor GC
12. append cleanup results to `current-release.summary.md` and the matching history summary, then release the managed Cloud release lock

If runtime promotion succeeds but cleanup fails, the command exits non-zero and the summary explicitly records a healthy runtime with failed cleanup. This is a partial-success state: inspect current release, containers and locks, then recover cleanup; do not repeat the full release merely because the wrapper returned non-zero.

`pre-deploy-check.sh` runs the runtime parts of `ops-check.sh` with `REQUIRE_BACKUP=0` because the release sequence creates a fresh PostgreSQL backup in the next step before any container update. It also uses `REQUIRE_DATAWORKER_HEALTHCHECK=${PRE_DEPLOY_REQUIRE_DATAWORKER_HEALTHCHECK:-0}` for the pre-update current-release check so an older running DataWorker image without the new Docker healthcheck cannot block upgrading to the fixed image. Normal operator runs of `./scripts/ops-check.sh` keep the defaults `REQUIRE_BACKUP=1` and `REQUIRE_DATAWORKER_HEALTHCHECK=1`, and still fail when the latest backup file, checksum, freshness policy, DataWorker Docker healthcheck definition, or DataWorker health status is not valid.

`pre-deploy-check.sh` is also the HTTP-only compensation-control gate. It fails fast when runtime secrets still use template values or are too short, old bootstrap secret disabling variables are present, OIDC HTTP values are not loopback/RFC1918 IPv4, Edge upload rate-limit variables are invalid or exceed 12000/minute, application or infrastructure image values still point at documentation example registries, application images do not include an explicit Harbor registry, infrastructure images point at Docker Hub, the deploy command runs as root without an explicit break-glass flag, compose config is invalid, the release-state files under `deploy/releases/` are not writable by the standard non-root deploy user, the OIDC signing certificate directory is not writable, or disk usage is at/above the routine release block threshold. Its summary must continue to print `preflight_transport_baseline=http-only` and `preflight_compensation_controls=...` so operators can see that HTTP is intentional and compensated rather than accidentally insecure. When `current-release.env` exists, the summary includes `healthz-http-local ops-check-runtime`; on a clean first deployment, it must instead include `runtime-check-skipped-no-current-release` because there is no previous runtime to probe.

The local build, third-party image mirror, local SSH release, and Edge installer catalog verification scripts also reject `.example` / `internal.example` documentation domains. Replace every example registry, SSH target, AICopilot challenge URL, and catalog base URL with the real intranet values before running them.

Release success order is fixed:

1. `GET /internal/healthz` returns `200`
2. `./scripts/post-deploy-check.sh` returns `0`
3. `./scripts/ops-check.sh` returns `0`
4. `deploy/releases/current-release.env` points at the new `DEPLOY_RELEASE_ID`
5. `deploy/releases/current-release.summary.md` records deployed services and git changes
6. the release summary records before/after disk usage and cleanup results

When closing the non-root container acceptance gate or validating an Edge installer/plugin release, first upload the real Edge installer bundle and plugin package through the approved Cloud release APIs, then run `POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG=1 ./scripts/post-deploy-check.sh`. Set `POST_DEPLOY_EDGE_EXPECTED_VERSION` to the uploaded host version and set `POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID` plus `POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION` to the uploaded plugin module/version when plugin upload is part of the acceptance evidence. The gate verifies public catalog, installer artifact, installer stub, Velopack `RELEASES`, channel manifests, one referenced `.nupkg`, and the expected plugin module/version when those variables are provided. The default post-deploy smoke verifies `/`, `/internal/healthz`, OIDC discovery, JWKS, the DataWorker Docker healthcheck, and `ops-check.sh`, then prints skip lines for the OIDC token gate and Edge catalog gate so a Cloud-only deployment is not blocked by absent production credentials or an absent Edge release.

OIDC token issuance can be checked only with a real authorization-code flow. To include it in post-deploy acceptance, first complete Cloud OIDC login/challenge from the real AICopilot client, capture the real one-time authorization code and matching PKCE verifier, then pass them through temporary 0600 files:

```bash
umask 077
authorization_code_file=$(mktemp)
code_verifier_file=$(mktemp)
read -rsp 'OIDC authorization code: ' oidc_code; printf '\n'
read -rsp 'OIDC PKCE verifier: ' oidc_verifier; printf '\n'
printf '%s' "$oidc_code" > "$authorization_code_file"
printf '%s' "$oidc_verifier" > "$code_verifier_file"
unset oidc_code oidc_verifier
POST_DEPLOY_VERIFY_OIDC_TOKEN=1 \
POST_DEPLOY_OIDC_REDIRECT_URI=http://<aicopilot-private-ip>:82/api/identity/cloud-oidc/callback \
POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE="$authorization_code_file" \
POST_DEPLOY_OIDC_CODE_VERIFIER_FILE="$code_verifier_file" \
./scripts/post-deploy-check.sh
rm -f "$authorization_code_file" "$code_verifier_file"
```

`POST_DEPLOY_OIDC_CLIENT_ID` defaults to `aicopilot`. The script posts `grant_type=authorization_code` to `/connect/token` and validates that the response contains a bearer `access_token`. It never fabricates user credentials, password grant, client credentials grant, or token values. The code/verifier files must be readable regular files, non-empty, and private to the current user; group/other permissions are rejected. The code/verifier must not be copied into logs, docs, shell history, process environment, or curl process arguments.

Disk guardrails are fixed:

- `/data` at 80% usage requires an operator warning and a disk usage summary.
- `/data` at 85% usage requires cleanup before any routine release continues.
- `/data` at 90% usage blocks non-emergency releases.
- Do not run broad destructive cleanup such as `docker system prune -a --volumes`.

Docker and containerd cleanup are separate. Docker prune commands do not cover all containerd snapshots/content; containerd cleanup must first confirm namespace, image ref, snapshot lease, and running container references. If the reference state is unclear, the release summary must report the usage and skip destructive containerd removal.

Fast rollback no longer assumes old Cloud application images remain on the server or in Harbor. Rollback to an older git sha requires rebuilding or re-pulling that target sha and deploying it through the standard release entrypoint.

## Cloud 管理员登录排查

生产 Cloud 首部署管理员工号固定为 `101650`，对应 `.env` 键为 `SEED_ADMIN_NO=101650`。管理员密码只允许来自 GitHub secret `SEED_ADMIN_PASSWORD`，不放入 `DEPLOY_ENV_FILE`、仓库文档或日志。

部署规则固定：

- `iiot-migration` 只在数据库不存在任何 `Admin` 用户时创建首个管理员。
- 一旦数据库已有 `Admin` 用户，后续部署只会跳过播种，不会修改管理员密码。
- 不允许 CI、AI、脚本或临时排障流程自动随机化、猜测管理员密码；只有 `cloud-admin-repair` 可以按显式确认把 `101650` 重置为 `SEED_ADMIN_PASSWORD`。

登录失败时先区分凭据来源：

- Cloud Web 登录使用 Cloud 数据库里的身份账号和 GitHub secret `SEED_ADMIN_PASSWORD`。
- Edge Launcher 本地样例账号不是 Cloud 密码来源，不能用 `launcher.accounts.sample.json` 反推 Cloud 密码。
- GitHub Actions 日志会把 secret 值打码为 `***`，不能从日志反读密码。

只读排查顺序：

1. 查看最近 `cloud-deploy` 日志中 `iiot-migration` 的播种输出。
   - `事务提交成功！账号 [...] 及员工业务数据已完整播种` 表示首个管理员刚被创建，工号来自当时的 `SEED_ADMIN_NO`。
   - `检测到已存在的管理员账号，跳过播种逻辑` 表示本次部署没有改密码。
2. 在服务器本机只读查询当前管理员工号，确认是不是 `101650`。
3. 如果工号不是 `101650`，按当前首部署约定修正为 `101650`。
4. 如果工号是 `101650` 但密码无法登录，只能在获得操作者明确确认后重置密码；不得猜测密码或尝试弱密码。

重置边界：

- 重置必须限定到 Cloud 管理员账号 `101650`。
- 重置前必须确认不会改人员、设备、配方、生产数据、Edge bootstrap secret 或 AICopilot 账号。
- 重置后必须立即用 `POST /api/v1/human/identity/login` 验证登录成功。

## Edge Update Package Distribution

EdgeClient Velopack update packages are served by the existing `nginx-gateway` at:

```text
<PUBLIC_BASE_URL>/edge-updates/velopack/stable/
```

Server-side package directory, determined by `EDGE_UPDATES_DIR`:

```text
/data/iiot-platform/edge-client/edge-updates/velopack/stable
```

The current production value is `EDGE_UPDATES_DIR=/data/iiot-platform/edge-client/edge-updates`, and the directory is mounted read-only into `nginx-gateway`.

Package contents are operator-managed:

- `RELEASES`
- `*.nupkg`
- optional `releases.*.json` files when produced by the packaging flow

The HttpApi production configuration must set `EdgeInstallerArtifacts__VelopackReleasesBaseUrl` to the channel parent directory:

```text
<PUBLIC_BASE_URL>/edge-updates/velopack
```

Publishing order:

1. Build the EdgeClient Velopack package set with the approved client release process.
2. Copy `RELEASES`, `*.nupkg`, and any generated `releases.*.json` files into the effective channel update directory.
3. Keep file ownership readable by the Docker daemon and the nginx container.
4. Set the client `launcher.update.json` `Source` value to `<PUBLIC_BASE_URL>/edge-updates/velopack/stable/`.
5. Verify the published files before asking clients to check for updates.

Validation examples:

```sh
gateway_http_port=$(sed -n 's/^GATEWAY_HTTP_PORT=//p' .env | tail -n 1)
gateway_http_port=${gateway_http_port:-80}
curl -I "http://127.0.0.1:${gateway_http_port}/edge-updates/velopack/stable/RELEASES"
curl -I "http://127.0.0.1:${gateway_http_port}/edge-updates/velopack/stable/<package-name>.nupkg"
curl -I "http://127.0.0.1:${gateway_http_port}/edge-updates/"
```

Expected results:

- `RELEASES` returns `200` and `Cache-Control: no-cache`.
- `*.nupkg` returns `200` and can be downloaded.
- `/edge-updates/` returns `403` or `404`; directory listing must not be exposed.

## Standard Backup

Run the PostgreSQL backup script from the deploy directory:

```sh
./scripts/postgres-backup.sh
```

Behavior:

- Starts `postgres` if needed.
- Executes `pg_dump -Fc` inside the container.
- Writes a timestamped dump file under `backups/postgres/`.
- Writes a same-name `.sha256` file next to the dump.
- Updates `backups/postgres/latest-successful-backup.txt` after the backup, checksum, and retention cleanup all succeed.
- Deletes dump/checksum pairs older than `BACKUP_RETENTION_DAYS`.

Backup directory structure is fixed:

```text
backups/postgres/
  iiot-db-<timestamp>.dump
  iiot-db-<timestamp>.dump.sha256
  latest-successful-backup.txt
  latest-successful-verify.txt
```

Checksum rule:

- Each `.sha256` file is generated with `sha256sum`.
- Restore and restore verification both require the dump file and the same-name checksum file to match before continuing.

Default cadence:

- Daily backup at `02:30`
- Weekly restore verification at `03:30` every Sunday

Cron templates are shipped in `deploy/cron/` and remain operator-managed.

## Standard Application Rollback

Use the standard rollback entrypoint:

```sh
./scripts/rollback-release.sh
```

You can also target a specific history record:

```sh
./scripts/rollback-release.sh releases/history/20260421T083000Z-sha-0123456789abcdef.env
```

Rollback rules are fixed:

- It only rolls back the 5 application images.
- Target images must be available; otherwise rebuild or re-pull the target git sha first.
- It does not run database downgrade logic.
- It does not call the database restore flow automatically.
- It still requires `post-deploy-check.sh` to return `0`.

If a schema-changing release cannot pass health checks after application rollback:

1. Stop treating it as a fast rollback case.
2. Transfer to the existing database recovery flow using the release-time backup.
3. In the current pre-launch environment, clean and rebuild the database only if that is the chosen operator path.

## Standard Restore

Restore from a specific dump file:

```sh
./scripts/postgres-restore.sh ./backups/postgres/iiot-db-20260420103000.dump
```

Restore flow is fixed:

1. Keep `postgres`, `redis-cache`, `rabbitmq`, and `seq` available.
2. Verify the dump checksum before stopping application traffic.
3. Stop `nginx-gateway`, `iiot-web`, `iiot-gateway`, `iiot-httpapi`, and `iiot-dataworker`.
4. Restore the dump into `iiot-db` with `pg_restore --clean --if-exists --no-owner --no-privileges`.
5. Re-run `iiot-migration`.
6. Start the application containers again.
7. Run `ops-check.sh`.

Restore acceptance order is fixed:

1. `GET /internal/healthz` must return `200`.
2. `./scripts/ops-check.sh` must return `0`.
3. `GET /` must return `200`.
4. Only after all three checks pass can the restore window be considered complete.

## Standard Restore Verification

Use the non-disruptive restore verification entrypoint:

```sh
./scripts/postgres-verify-backup.sh
```

You can also pass an explicit dump path:

```sh
./scripts/postgres-verify-backup.sh ./backups/postgres/iiot-db-20260420103000.dump
```

Behavior:

- Uses the latest dump by default.
- Verifies the dump checksum before continuing.
- Creates a temporary database named `iiot-restore-verify-<timestamp>`.
- Restores the dump into that database.
- Runs fixed SQL smoke checks against `devices`, `employees`, `recipes`, `outbox_messages`, and `__EFMigrationsHistory`.
- Drops the temporary database after the checks succeed.
- Updates `backups/postgres/latest-successful-verify.txt` only after the temporary database has been removed successfully.

## Standard Operations Check

Use the single operations check entrypoint:

```sh
./scripts/ops-check.sh
```

It checks:

- key container running state
- `GET /internal/healthz`
- `outbox_messages` backlog
- current business queues and paired `_error` / `_skipped` queues
- latest successful backup age
- latest successful restore verification age

Fresh clean deployments may not have `latest-successful-verify.txt` yet. In that case the script prints a warning and still returns `0` when all runtime health, backup, Outbox, and queue checks are clean. To make restore verification freshness a hard gate, run with `REQUIRE_BACKUP_VERIFY=1`.

Business queue scope is fixed:

- `iiot-pass-station-batches`
- `iiot-device-logs`
- `iiot-hourly-capacities`

If `Infrastructure__EventBus__EndpointPrefix` is set in `.env`, the script prepends that prefix to the queue names before checking depth.

Output fields always include:

- `latest_backup_age_hours`
- `latest_backup_verified_age_days`
- `latest_backup_file`

Exit codes:

- `0`: service is available, Outbox backlog is `0`, `_error` / `_skipped` queue depth is `0`, latest backup freshness is within policy, and restore verification is either fresh or only warning because `REQUIRE_BACKUP_VERIFY` is not set
- `1`: `GET /internal/healthz` failed or a required container is not running
- `2`: service is available, but Outbox backlog is non-zero, an `_error` / `_skipped` queue has pending messages, no successful backup exists, the latest backup is older than `BACKUP_MAX_AGE_HOURS`, or restore verification is missing/older than `BACKUP_VERIFY_MAX_AGE_DAYS` while `REQUIRE_BACKUP_VERIFY=1`

## Manual Handling Order

When `ops-check.sh` returns `2`, use this order:

1. Confirm `GET /internal/healthz` is still `200`.
2. Inspect `latest_backup_age_hours`, `latest_backup_verified_age_days`, and `latest_backup_file` first, then check whether the signal also comes from Outbox backlog, `_error`, or `_skipped`.
3. Inspect Seq and container logs to identify the current root cause.
4. Fix the root cause before replaying anything.
5. Replay from RabbitMQ in controlled batches only.
6. Do not replay while `/internal/healthz` is failing, while the root cause is still unknown, during an unfinished database restore, or after the same failure pattern has already reappeared in the first replay batch.
7. Stop replay immediately if the same failure pattern returns.
8. Re-run `./scripts/ops-check.sh` and keep the system in `0` before ending the operation window.
