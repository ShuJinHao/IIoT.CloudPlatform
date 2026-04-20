# IIoT Cloud Production Deploy

## Scope

This folder is the hand-written production template for `IIoT.CloudPlatform`.
It replaces `aspirate-output` as the source of truth for single-machine production deployment.

Current production assumptions:

- Single public IP
- HTTP only
- No public domain
- No production TLS certificate yet

`HTTPS` and `HSTS` are intentionally excluded from this batch. They must be delivered in a later dedicated hardening window.

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
2. Replace all placeholder secrets.
3. Keep `PUBLIC_BASE_URL` as an origin only, without a trailing slash.
4. Validate the template before the first rollout:

```powershell
docker compose --env-file deploy/.env.example -f deploy/docker-compose.prod.yml config
```

5. Pull or build the images referenced in `.env`.
6. Start infrastructure:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.prod.yml up -d postgres redis-cache rabbitmq seq
```

7. Run the migration/bootstrap job:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.prod.yml run --rm iiot-migration
```

8. Start application services:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.prod.yml up -d iiot-httpapi iiot-gateway iiot-dataworker iiot-web nginx-gateway
```

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

DataWorker queues are explicit and stable:

- `iiot-pass-station-injection`
- `iiot-pass-station-stacking`
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

## Alias Retirement Gate

Do not remove the deprecated aliases until both conditions are true:

- 14 consecutive days with zero alias hits
- no active consumer or deployed client still depends on the alias

## Next Batch

The next public-network hardening batch must cover:

- domain and certificate setup
- TLS termination
- `HTTPS` redirect policy
- `HSTS`

