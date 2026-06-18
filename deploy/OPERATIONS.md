# IIoT Cloud Operations

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
  previous-release.env
  staged-release.env
  history/
    <timestamp>-<release-id>.env
```

Each release record keeps only:

- the 5 application image coordinates
- `DEPLOY_RELEASE_ID`
- `DEPLOY_GIT_SHA`
- `DEPLOY_TRIGGERED_BY`
- `DEPLOYED_AT_UTC`
- `PRE_DEPLOY_BACKUP_FILE`

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

The standard sequence is:

1. Push or merge to `main`.
2. `cloud-image` runs on `iiot-linux-prod`, builds only affected application images when path filters can narrow the change, and pushes them to Harbor with `sha-${GITHUB_SHA}`. Shared code, build configuration, or manual dispatch builds all application images.
3. Trigger `cloud-deploy` manually with the matching `release_tag = sha-*`; leave `services` empty for a full release, or set comma-separated service names for an incremental release.
4. `cloud-deploy` runs on the same non-root runner, syncs `deploy/`, writes `DEPLOY_ENV_FILE`, logs in to Harbor, and calls `deploy-release.sh`.

The runner must not run as root. See `RUNNER.md` for the required `github-runner` user, Docker group, labels, and server reachability checks.

Application images and infrastructure images must already exist in Harbor before deploy. Docker Hub is not a production dependency source; `pre-deploy-check.sh` rejects Docker Hub shorthand infrastructure image values.

Manual release from the server is an emergency fallback. Log in to Harbor on the private deployment server first:

```sh
docker login <OCI_REGISTRY> --username <OCI_REGISTRY_USERNAME>
```

Then use the single release entrypoint:

```sh
DEPLOY_GIT_SHA=<git-sha> DEPLOY_TRIGGERED_BY=manual ./scripts/deploy-release.sh sha-0123456789abcdef
```

For an incremental release, pass only the services that were rebuilt:

```sh
DEPLOY_GIT_SHA=<git-sha> DEPLOY_TRIGGERED_BY=manual ./scripts/deploy-release.sh sha-0123456789abcdef --services httpapi,gateway
```

Release rules are fixed:

- Standard production version input is `release_tag = sha-*`.
- First deployment or a post-clean rebuild may not have `current-release.env` yet.
- `latest` is not a standard production application version in this batch.

Release flow is fixed:

1. `pre-deploy-check.sh`
2. `postgres-backup.sh`
3. write `staged-release.env`
4. rewrite the selected application image coordinates in `.env` (`--services` empty means all five)
5. `docker compose pull` selected application services from Harbor
6. keep infrastructure available
7. run `iiot-migration` only when migration is part of the selected service set
8. start selected application containers
9. `post-deploy-check.sh`
10. rotate `current` / `previous` and append `history`

Release success order is fixed:

1. `GET /internal/healthz` returns `200`
2. `./scripts/post-deploy-check.sh` returns `0`
3. `./scripts/ops-check.sh` returns `0`
4. `deploy/releases/current-release.env` points at the new `DEPLOY_RELEASE_ID`

## Edge Update Package Distribution

EdgeClient Velopack update packages are served by the existing `nginx-gateway` at:

```text
<PUBLIC_BASE_URL>/edge-updates/velopack/{channel}/
```

Server-side package directory:

```text
/srv/iiot/edge-updates/velopack/{channel}
```

The path defaults to `/srv/iiot/edge-updates`, can be overridden by `EDGE_UPDATES_DIR` in the production `.env` file, and is mounted read-only into `nginx-gateway`.

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
4. Set the client `launcher.update.json` `Source` value to `<PUBLIC_BASE_URL>/edge-updates/velopack/{channel}/`.
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
