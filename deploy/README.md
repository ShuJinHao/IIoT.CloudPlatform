# IIoT Cloud Production Deploy

## Scope

This folder is the hand-written production template for `IIoT.CloudPlatform`.
It replaces `aspirate-output` as the source of truth for single-machine production deployment.

Current production assumptions:

- Single public IP
- HTTP only
- No public domain
- No production TLS certificate yet

`HTTPS`, `HSTS`, multi-replica rollout, and pseudo-high-availability are intentionally excluded from this batch.

Current rollout target is a single-machine production starter, not a multi-replica topology.
`iiot-gateway` keeps one upstream `httpapi` destination in this batch on purpose.

## Topology

Runtime topology stays fixed:

- `nginx-gateway -> iiot-gateway -> iiot-httpapi`
- `iiot-dataworker` consumes RabbitMQ queues asynchronously
- `iiot-migration` is a one-shot migration/bootstrap job
- `seq` is the first centralized log entrypoint

External exposure:

- `80/tcp` for the product entrypoint
- `127.0.0.1:5341` for Seq
- `127.0.0.1:15672` for RabbitMQ management

## Development

Development still uses Aspire:

```powershell
dotnet run --project src/hosts/IIoT.AppHost/IIoT.AppHost.csproj
```

Frontend development still uses Vite:

```powershell
cd src/ui/iiot-web
npm install
npm run dev
```

## Single Machine Production

1. Copy `deploy/.env.example` to a real `.env`.
2. Replace every placeholder secret and set the application image repositories for the real registry namespace.
3. Keep `PUBLIC_BASE_URL` as an origin only, without a trailing slash.
4. Validate the template before the first rollout:

```powershell
docker compose --env-file deploy/.env.example -f deploy/docker-compose.prod.yml config -q
```

5. Use a `release_tag` produced by `cloud-image`. The only standard production version format in this batch is `sha-*`.
6. First deployment, or a post-clean rebuild, may not have `deploy/releases/current-release.env` yet. That is normal for this stage.
7. Keep the release scripts executable on the server:

```powershell
chmod +x deploy/scripts/*.sh
```

8. Manual release uses the standard release entrypoint:

```powershell
$env:DEPLOY_GIT_SHA = "<git-sha>"
$env:DEPLOY_TRIGGERED_BY = "manual"
bash deploy/scripts/deploy-release.sh sha-0123456789abcdef
```

9. GitHub release uses the `cloud-deploy` workflow with the same `release_tag`.

What the standard release entrypoint does:

- runs `pre-deploy-check.sh`
- forces a fresh PostgreSQL backup
- rewrites only the 5 application image coordinates in `.env`
- runs `iiot-migration`
- starts the application containers
- runs `post-deploy-check.sh`
- writes `deploy/releases/current-release.env`, `previous-release.env`, `staged-release.env`, and `history/*`

`/internal/healthz` remains the production readiness probe for this batch.
It verifies `nginx -> iiot-gateway -> iiot-httpapi` and PostgreSQL connectivity.
The nginx template only allows localhost access to this route. It is not a public anonymous health endpoint.

## Standard Rollback

Use the standard rollback entrypoint for application-only rollback:

```powershell
bash deploy/scripts/rollback-release.sh
```

You can also target an explicit history record:

```powershell
bash deploy/scripts/rollback-release.sh releases/history/20260421T083000Z-sha-0123456789abcdef.env
```

Rollback rules for this batch:

- Only the 5 application images are rolled back.
- The rollback entrypoint does not run database downgrade logic.
- The rollback entrypoint does not call the database restore flow automatically.
- If a schema-changing release cannot pass health checks after application rollback, transfer to the existing database recovery flow or clear and rebuild the database in the current pre-launch environment.

The operator workflow for release, rollback, backup, restore, and post-deploy checks is documented in [OPERATIONS.md](./OPERATIONS.md).

## Recurring Backup And Verify

This batch ships cron templates only. `cloud-deploy` does not modify system cron automatically.

Standard operator steps:

1. Replace `/srv/iiot-cloud/deploy` in `deploy/cron/iiot-backup.cron.example` and `deploy/cron/iiot-backup-verify.cron.example` with the real deploy directory on the server.
2. Append both template entries to the deployment user's crontab.
3. Keep the default cadence unless a later ops decision explicitly changes it:
   - daily backup at `02:30`
   - weekly restore verification at `03:30` every Sunday
4. Confirm the deployment user can access Docker and run the scripts manually before enabling the cron entries.
5. Verify the installed crontab includes both entries.

## Configuration Ownership

Use `deploy/.env.example` only as a template. Real production values should come from one of these sources:

- CI secret `DEPLOY_ENV_FILE`, which is written to the server as `.env` during `cloud-deploy`
- an operator-managed `.env` on the target server for manual rollout

Values that must be replaced by secrets or environment-specific settings:

- application image repositories for `IIOT_HTTPAPI_IMAGE`, `IIOT_GATEWAY_IMAGE`, `IIOT_DATAWORKER_IMAGE`, `IIOT_MIGRATION_IMAGE`, and `IIOT_WEB_IMAGE`
- `PUBLIC_BASE_URL`
- `PG_PASSWORD`
- `RABBITMQ_DEFAULT_PASS`
- `JWTSETTINGS__SECRET`
- `SEQ_ADMIN_PASSWORD`
- `SEED_ADMIN_PASSWORD`
- `SEQ_API_KEY` if Seq ingestion is protected

Values that are template defaults and usually stay unchanged for the single-machine starter:

- `IIOT_NGINX_IMAGE`
- `IIOT_POSTGRES_IMAGE`
- `IIOT_REDIS_IMAGE`
- `IIOT_RABBITMQ_IMAGE`
- `IIOT_SEQ_IMAGE`
- `GATEWAY_HTTP_PORT`
- `SEQ_HOST_PORT`
- `RABBITMQ_MANAGEMENT_PORT`
- `FORWARDED_HEADERS_ENABLED`
- `FORWARDED_HEADERS_FORWARDLIMIT`
- `FORWARDED_HEADERS_KNOWNNETWORKS__0`
- `FORWARDED_HEADERS_KNOWNNETWORKS__1`
- `BACKUP_RETENTION_DAYS`
- `BACKUP_MAX_AGE_HOURS`
- `BACKUP_VERIFY_MAX_AGE_DAYS`
- local Vite development CORS origins

Optional values that stay empty unless the operator explicitly needs them:

- `Infrastructure__EventBus__EndpointPrefix`
- `SEQ_API_KEY`

Release version ownership stays explicit:

- `DEPLOY_ENV_FILE` provides the registry/repository coordinates and secrets.
- `release_tag` comes from `cloud-image` and is passed separately to `cloud-deploy`.
- `latest` is not a standard production application version in this batch.

## Gateway Notes

The nginx gateway keeps the existing route surfaces:

- `/api/v1/human/*`
- `/api/v1/edge/*`
- `/api/v1/bootstrap/*`

Refresh endpoints included in this template:

- `POST /api/v1/human/identity/refresh`
- `POST /api/v1/bootstrap/edge-refresh`

The deprecated aliases are still proxied by the gateway through the existing YARP configuration.

## Logs And Message Replay

Seq is the current log aggregation baseline. Application services write both:

- local rolling files under `/app/logs`
- centralized events to Seq
- gateway access and error logs to container stdout/stderr

DataWorker queues are explicit and stable:

- `iiot-pass-station-batches`
- `iiot-device-logs`
- `iiot-hourly-capacities`

Failure policy:

- Immediate retry: 3 attempts with incremental backoff
- Terminal consumer failure: RabbitMQ queue `<queue>_error`
- Skipped or unmatched message: RabbitMQ queue `<queue>_skipped`

Replay guidance:

1. Fix the root cause first.
2. Inspect the original queue and the paired `_error` or `_skipped` queue in RabbitMQ management.
3. Move messages back to the original queue in controlled batches.
4. Watch Seq during replay and stop if the same failure pattern returns.

For backup, restore, health checking, and the standard manual handling order, use [OPERATIONS.md](./OPERATIONS.md).

## Alias Retirement Gate

Do not remove the deprecated aliases until both conditions are true:

- 14 consecutive days with zero alias hits
- no active consumer or deployed client still depends on the alias

## Later Hardening

Future public-network hardening can cover:

- domain and certificate setup
- TLS termination
- `HTTPS` redirect policy
- `HSTS`
