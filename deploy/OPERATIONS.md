# IIoT Cloud Operations

三项目上传部署统一入口见 [上传部署总览](../../docs/上传部署总览.md)。

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
  current-release.env
  current-release.summary.md
  previous-release.env
  staged-release.env
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

## Internal Health Probe

Production readiness for this batch is:

- `GET /internal/healthz`

Behavior:

- It verifies `nginx -> iiot-gateway -> iiot-httpapi`.
- It returns `200` only when HttpApi is responsive and PostgreSQL is reachable.
- It does not include Redis, RabbitMQ, or DataWorker health in the result.

Access policy:

- The nginx template only allows `127.0.0.1` and `::1`.
- Any non-local source is denied.
- This route is for server-local rollout and manual operations only.

Example:

```sh
curl --silent --show-error --output /dev/null --write-out '%{http_code}\n' http://127.0.0.1:80/internal/healthz
```

## Standard Release

Standard production release is driven by GitHub Actions on the intranet self-hosted runner labeled `iiot-linux-prod`.

Routine deployment red lines:

- Do not manually run `cloud-image` via `workflow_dispatch` for routine deployment. Manual `cloud-image` bypasses path narrowing and builds every application image.
- Use the push-triggered `cloud-image` run, then copy the exact `Deploy services input` from its Step Summary or `cloud-built-service-*` artifact into `cloud-deploy.services`.
- Leave `cloud-deploy.services` empty only for first deployment, an explicitly approved full release, or emergency recovery.

The standard sequence is:

1. Push or merge to `main`.
2. `cloud-ci` runs the fast gate by default: restore/build, ServiceLayer tests, ConfigurationGuard tests, web build, and compose config. Full EndToEnd is manual via `workflow_dispatch`.
3. The push-triggered `cloud-image` run executes on `iiot-linux-prod`, builds only affected application images when path filters can narrow the change, and pushes them to Harbor with `sha-${GITHUB_SHA}`. Shared code or build configuration changes currently build all application images. The workflow uploads `cloud-built-service-*` artifacts and prints the exact `Deploy services input`.
4. Trigger `cloud-deploy` manually with the matching `release_tag = sha-*` and copy the built-services value into `services` for an incremental release. Do not leave `services` empty unless this is a first deployment, an explicitly approved full release, or emergency recovery.
5. `cloud-deploy` runs on the same non-root runner, syncs `deploy/`, writes `DEPLOY_ENV_FILE`, overwrites the server `.env` `SEED_ADMIN_PASSWORD` from the dedicated GitHub secret, logs in to Harbor, and calls `deploy-release.sh`.

Harbor application repositories keep only the current production `sha-*` tag. `cloud-image` must delete old application `sha-*` tags after a successful push; the Harbor robot must have tag delete permission. Tag deletion only removes references, so Harbor Garbage Collection must run after tag deletion to reclaim disk.

The runner must not run as root. See `RUNNER.md` for the required `github-runner` user, Docker group, labels, and server reachability checks.

Application images and infrastructure images must already exist in Harbor before deploy. Docker Hub is not a production dependency source; `pre-deploy-check.sh` rejects Docker Hub shorthand infrastructure image values.

Manual release from the server is an emergency fallback. Log in to Harbor on the private deployment server first:

```sh
docker login <OCI_REGISTRY> --username <OCI_REGISTRY_USERNAME>
```

For a full emergency release, use the single release entrypoint without `--services`:

```sh
DEPLOY_GIT_SHA=<git-sha> DEPLOY_TRIGGERED_BY=manual ./scripts/deploy-release.sh sha-0123456789abcdef
```

For an incremental release, pass only the services that were rebuilt. For example, frontend-only deployment must use `--services web`:

```sh
DEPLOY_GIT_SHA=<git-sha> DEPLOY_TRIGGERED_BY=manual ./scripts/deploy-release.sh sha-0123456789abcdef --services web
```

Incremental release requires an existing `current-release.env`; first deployment must be full. The script preserves image coordinates for services not included in `--services` by reading the current release manifest before rewriting selected services.

Release rules are fixed:

- Standard production version input is `release_tag = sha-*`.
- First deployment or a post-clean rebuild may not have `current-release.env` yet.
- `latest` is not a standard production application version in this batch.

Release flow is fixed:

1. `pre-deploy-check.sh`
2. `postgres-backup.sh`
3. write `staged-release.env`
4. rewrite the selected application image coordinates in `.env` (`--services` empty means all five; incremental keeps unselected images from `current-release.env`)
5. `docker compose pull` selected application services from Harbor
6. keep infrastructure available
7. run `iiot-migration` only when migration is part of the selected service set
8. start selected application containers
9. `post-deploy-check.sh`
10. run post-release cleanup: remove Docker/BuildKit build cache, report and clean Docker-managed images separately from containerd-managed content, remove only local old application images that are not referenced by current containers, delete old Harbor application tags, and run or confirm Harbor GC
11. rotate `current` / `previous`, write `current-release.summary.md`, and append `history`

`pre-deploy-check.sh` runs the runtime parts of `ops-check.sh` with `REQUIRE_BACKUP=0` because the release sequence creates a fresh PostgreSQL backup in the next step before any container update. Normal operator runs of `./scripts/ops-check.sh` keep the default `REQUIRE_BACKUP=1` and still fail when the latest backup file, checksum, or freshness policy is not valid.

Release success order is fixed:

1. `GET /internal/healthz` returns `200`
2. `./scripts/post-deploy-check.sh` returns `0`
3. `./scripts/ops-check.sh` returns `0`
4. `deploy/releases/current-release.env` points at the new `DEPLOY_RELEASE_ID`
5. `deploy/releases/current-release.summary.md` records deployed services and git changes
6. the release summary records before/after disk usage and cleanup results

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
curl -I http://127.0.0.1:80/edge-updates/velopack/stable/RELEASES
curl -I http://127.0.0.1:80/edge-updates/velopack/stable/<package-name>.nupkg
curl -I http://127.0.0.1:80/edge-updates/
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
