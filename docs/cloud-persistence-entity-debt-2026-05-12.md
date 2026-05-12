# Cloud Persistence Entity Debt Closeout - 2026-05-12

## Scope

- Project: `IIoT.CloudPlatform` only.
- Branch: local `cloud/persistence-entity-debt`.
- Areas changed: EF aggregate repository update behavior, refresh-token persistence model guardrails, and production service test coverage.

## Implementation

- `EfRepository.Update` now rejects detached aggregate updates instead of attaching the instance and marking every mapped property as modified.
- `RefreshTokenSession` remains an infrastructure persistence row rather than inheriting `BaseEntity<Guid>`, so refresh-token rotation stays outside the aggregate repository and domain-event pipeline.
- Added a real `IIoT.ProductionService.Tests` project with command behavior tests for device profile updates and recipe version upgrades.

## Verification

- `dotnet restore src\tests\IIoT.ProductionService.Tests\IIoT.ProductionService.Tests.csproj`: passed.
- `dotnet test src\tests\IIoT.ProductionService.Tests\IIoT.ProductionService.Tests.csproj --no-restore`: passed, 4 tests.
- `dotnet test src\tests\IIoT.ServiceLayer.Tests\IIoT.ServiceLayer.Tests.csproj --no-restore`: passed, 166 tests.
- `dotnet build src\hosts\IIoT.AppHost\IIoT.AppHost.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `git diff --check`: passed.

## Residual Risk

- Detached aggregate mutation is now an explicit error. Existing cloud command paths load aggregates through EF tracking queries before calling `Update`, so login and refresh-token behavior is not changed.
- The remote branch `origin/cloud/persistence-entity-debt` was not found; this work was applied on a local branch created from the current Cloud baseline.
