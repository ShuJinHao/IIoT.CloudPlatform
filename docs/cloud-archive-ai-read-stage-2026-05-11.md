# Cloud archive and AiRead stage record - 2026-05-11

## Completed business scope

- Cloud remains the device identity source, Edge upload archive endpoint, and AiRead read-only API provider.
- No EdgeClient or AICopilot code was changed.
- No MES API, DTO, permission, worker, menu, or business responsibility was added.

## Implementation

- Added stable HTTP problem `code` semantics for invalid token, device scope forbid, invalid payload, unknown pass-station type, unsupported schema version, payload-too-large, device-not-found, and server-error responses.
- Changed Edge upload accept responses to expose `accepted` or `duplicate_accepted` while preserving existing upload URLs and request bodies.
- Added optional pass-station `schemaVersion` and `processType` intake validation. Current supported schema version is `1`; `processType`, when supplied, must match route `typeKey`.
- Added AiRead recipe-version summary under `/api/v1/ai/read/recipes/versions`, protected by `AiRead.Recipe`, returning only recipe id, device id, process id, name, version, and status.
- Removed the unused device process-change domain/cache branch so manual device maintenance remains limited to device name changes.
- Kept Dapper write-model persistence behind existing outbox/consumer flow; HTTP handlers still validate, register idempotency, enqueue integration events, and invalidate caches where applicable.

## Verification

- `dotnet test IIoT.CloudPlatform/src/tests/IIoT.ServiceLayer.Tests`
  - Passed: 142, Failed: 0, Skipped: 0.
- `dotnet build IIoT.CloudPlatform/src/tests/IIoT.EndToEndTests --no-restore`
  - Passed build with 0 warnings and 0 errors.
- `dotnet test IIoT.CloudPlatform/src/tests/IIoT.EndToEndTests --no-build --filter "FullyQualifiedName~ConfigurationGuardTests|FullyQualifiedName~AiReadServiceAccount_ShouldReadFiveReadOnlySurfaces|FullyQualifiedName~AiReadDevices_ShouldRequireAiServiceAccountAndPermission|FullyQualifiedName~DeviceLogs_DuplicateConsume_ShouldPersistOneRow"`
  - Passed: 52, Failed: 0, Skipped: 0.
- `rg -n "\bMES\b|\bMes\b" IIoT.CloudPlatform/src --glob '!**/bin/**' --glob '!**/obj/**'`
  - No source matches.
- Cloud source process-change identifier scan
  - No matches.

## Remaining risk

- Full unfiltered `dotnet test IIoT.CloudPlatform/src/tests/IIoT.EndToEndTests` was attempted twice and timed out before completion in the local environment. Targeted end-to-end coverage for this stage passed.
- No database migration was added; the change uses existing tables and existing pass-station/event schema with additive contract fields.
