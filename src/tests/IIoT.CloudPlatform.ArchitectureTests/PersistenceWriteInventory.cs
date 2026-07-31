using System.Collections.Immutable;
using System.Xml.Linq;
using IIoT.CloudPlatform.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IIoT.CloudPlatform.ArchitectureTests;

internal static class PersistenceWriteInventory
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview);

    private static readonly ImmutableHashSet<string> FailClosedCandidateNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "SaveChanges",
            "SaveChangesAsync",
            "BeginTransaction",
            "BeginTransactionAsync",
            "Commit",
            "CommitAsync",
            "Rollback",
            "RollbackAsync",
            "ExecuteSqlRaw",
            "ExecuteSqlRawAsync",
            "ExecuteSqlInterpolated",
            "ExecuteSqlInterpolatedAsync",
            "ExecuteUpdate",
            "ExecuteUpdateAsync",
            "ExecuteDelete",
            "ExecuteDeleteAsync",
            "ExecuteNonQuery",
            "ExecuteNonQueryAsync",
            "ExecuteReader",
            "ExecuteReaderAsync",
            "ExecuteScalar",
            "ExecuteScalarAsync",
            "Migrate",
            "MigrateAsync",
            "EnsureCreated",
            "EnsureCreatedAsync",
            "EnsureDeleted",
            "EnsureDeletedAsync");

    // These are read-only allowlists: an unknown future Identity API is
    // deliberately treated as a write until its semantics are reviewed.
    private static readonly ImmutableHashSet<string> IdentityStoreReadOnlyMethodNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "CountCodesAsync",
            "FindByEmailAsync",
            "FindByIdAsync",
            "FindByLoginAsync",
            "FindByNameAsync",
            "FindByPasskeyIdAsync",
            "FindPasskeyAsync",
            "GetAccessFailedCountAsync",
            "GetAuthenticatorKeyAsync",
            "GetClaimsAsync",
            "GetEmailAsync",
            "GetEmailConfirmedAsync",
            "GetLockoutEnabledAsync",
            "GetLockoutEndDateAsync",
            "GetLoginsAsync",
            "GetNormalizedEmailAsync",
            "GetNormalizedRoleNameAsync",
            "GetNormalizedUserNameAsync",
            "GetPasskeysAsync",
            "GetPasswordHashAsync",
            "GetPhoneNumberAsync",
            "GetPhoneNumberConfirmedAsync",
            "GetRoleIdAsync",
            "GetRoleNameAsync",
            "GetRolesAsync",
            "GetSecurityStampAsync",
            "GetTokenAsync",
            "GetTwoFactorEnabledAsync",
            "GetUserIdAsync",
            "GetUserNameAsync",
            "GetUsersForClaimAsync",
            "GetUsersInRoleAsync",
            "HasPasswordAsync",
            "IsInRoleAsync");

    private static readonly ImmutableHashSet<string> IdentityManagerReadOnlyMethodNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "CountRecoveryCodesAsync",
            "CreateSecurityTokenAsync",
            "FindByEmailAsync",
            "FindByIdAsync",
            "FindByLoginAsync",
            "FindByNameAsync",
            "FindByPasskeyIdAsync",
            "GenerateChangeEmailTokenAsync",
            "GenerateChangePhoneNumberTokenAsync",
            "GenerateConcurrencyStampAsync",
            "GenerateEmailConfirmationTokenAsync",
            "GenerateNewAuthenticatorKey",
            "GeneratePasswordResetTokenAsync",
            "GenerateTwoFactorTokenAsync",
            "GenerateUserTokenAsync",
            "GetAccessFailedCountAsync",
            "GetAuthenticationTokenAsync",
            "GetAuthenticatorKeyAsync",
            "GetClaimsAsync",
            "GetEmailAsync",
            "GetLockoutEnabledAsync",
            "GetLockoutEndDateAsync",
            "GetLoginsAsync",
            "GetPasskeyAsync",
            "GetPasskeysAsync",
            "GetPhoneNumberAsync",
            "GetRoleIdAsync",
            "GetRoleNameAsync",
            "GetRolesAsync",
            "GetSecurityStampAsync",
            "GetTwoFactorEnabledAsync",
            "GetUserAsync",
            "GetUserId",
            "GetUserIdAsync",
            "GetUserName",
            "GetUserNameAsync",
            "GetUsersForClaimAsync",
            "GetUsersInRoleAsync",
            "GetValidTwoFactorProvidersAsync",
            "HasPasswordAsync",
            "IsEmailConfirmedAsync",
            "IsInRoleAsync",
            "IsLockedOutAsync",
            "IsPhoneNumberConfirmedAsync",
            "NormalizeEmail",
            "NormalizeKey",
            "NormalizeName",
            "RoleExistsAsync",
            "VerifyChangePhoneNumberTokenAsync",
            "VerifyTwoFactorTokenAsync",
            "VerifyUserTokenAsync");

    private static readonly PersistenceEvidence ArchitectureEvidence = new(
        "src/tests/IIoT.CloudPlatform.ArchitectureTests/PersistenceBoundaryArchitectureTests.cs",
        "ProductionPersistenceWriteEntrypoints_ShouldBeDynamicallyClassified");

    private static readonly IReadOnlyDictionary<string, PersistenceEvidence>
        ConcreteEvidenceBindings = new Dictionary<string, PersistenceEvidence>(
            StringComparer.Ordinal)
    {
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.RunEfMigrationsAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.EnsureDeviceCodeSchemaCompatibilityAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.GetNormalizedClientCodeConflictsAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.EnsureIdentitySchemaCompatibilityAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.IdentityAdminTablesExistAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.IdentityAuthorizationTablesExistAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.GetAdminLikeRoleConflictsAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.GetPermissionClaimConflictsAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.EnsureRecordSchemaCompatibilityAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.InitializeTimescaleDbAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.SeedOidcClientsAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs::DatabaseInitializationOrchestrator.RecordTablesExistAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
            "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.SeedAttemptAsync(IIoTDbContext,UserManager<ApplicationUser>,RoleManager<IdentityRole<Guid>>,IConfiguration,SeedRetryTarget,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.AcquireSingleAdminSeedLockAsync(IIoTDbContext,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.EnsureRolePermissionTemplatesAsync(RoleManager<IdentityRole<Guid>>,IReadOnlyDictionary<string, Guid>,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.EnsureRoleAsync(RoleManager<IdentityRole<Guid>>,string,Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.CreateFirstAdminAsync(IIoTDbContext,UserManager<ApplicationUser>,SeedAdminOptions,string,Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.RepairExistingAdminAsync(IIoTDbContext,UserManager<ApplicationUser>,ExistingAdminState,string,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs::SystemInitData.ResetPasswordAsync(UserManager<ApplicationUser>,ApplicationUser,string,string,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
            "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin"),
        ["src/infrastructure/IIoT.Dapper/Initializers/RecordSchemaInitializer.cs::RecordSchemaInitializer.InitializeAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/RecordSchemaInitializerPostgresTests.cs",
            "RecordSchemas_FirstAndWarmRun_ShouldConvergeToRequiredTables"),
        ["src/infrastructure/IIoT.Dapper/Production/Repositories/Capacities/HourlyCapacityRecordRepository.cs::HourlyCapacityRecordRepository.UpsertAsync(HourlyCapacityWriteModel,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/CapacityPersistencePostgresTests.cs",
            "UpsertAsync_LateSmallerSnapshotCannotReplaceCompletedClipCount"),
        ["src/infrastructure/IIoT.Dapper/Production/Repositories/DeviceLogs/DeviceLogRecordRepository.cs::DeviceLogRecordRepository.InsertBatchAsync(IReadOnlyCollection<DeviceLogWriteModel>,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/CapacityPersistencePostgresTests.cs",
            "PassStationAndDeviceLogWrites_ShouldRemainIdempotent"),
        ["src/infrastructure/IIoT.Dapper/Production/Repositories/PassStations/PassStationRecordRepository.cs::PassStationRecordRepository.InsertBatchAsync(IReadOnlyCollection<PassStationRecordWriteModel>,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/CapacityPersistencePostgresTests.cs",
            "PassStationAndDeviceLogWrites_ShouldRemainIdempotent"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Auditing/EfAuditTrailService.cs::EfAuditTrailService.WriteAttemptAsync(Guid,AuditTrailEntry,string?,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseComponentDeletionPostgresTests.cs",
            "EfAuditTrailService_ShouldPersistOneExactRecordPerIdempotencyKey"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Auditing/EfOidcIssuanceAuditTrailService.cs::EfOidcIssuanceAuditTrailService.StageSuccessAsync(AuditTrailEntry,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "OidcIssuanceSuccessAudit_ShouldCommitAtomicallyWithGrant"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/ClientReleases/EfClientReleaseComponentDeletionStore.cs::EfClientReleaseComponentDeletionStore.SaveChangesAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/ClientReleases/EfDeviceClientStateStore.cs::EfDeviceClientStateStore.SaveChangesAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/EdgeHosts/EfEdgeHostPlcRuntimeStateStore.cs::EfEdgeHostPlcRuntimeStateStore.SaveChangesAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReports_ShouldRecoverCommitConfirmationLoss"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/IIoTDbContext.cs::IIoTDbContext.SaveChangesAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EdgeReleaseApiKeyService.cs::EdgeReleaseApiKeyService.CreateAttemptAsync(CreateTarget,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EdgeReleaseApiKeyService.cs::EdgeReleaseApiKeyService.RevokeAttemptAsync(RevokeTarget,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EdgeReleaseApiKeyService.cs::EdgeReleaseApiKeyService.ValidateAttemptAsync(string,DateTimeOffset,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EdgeReleaseApiKeyService.cs::EdgeReleaseApiKeyService.EnsureRevokeAuditByIdempotencyKeyAsync(Guid,AuditTrailEntry,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EdgeReleaseApiKeyService.cs::EdgeReleaseApiKeyService.EnsureAuditTargetAsync(IIoTDbContext,Guid,AuditTrailEntry,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EfRefreshTokenService.cs::EfRefreshTokenService.IssueAttemptAsync(RefreshTokenSession,bool,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "HumanRefreshRotation_ShouldRecoverCommitLossAndRejectSourceReplay"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EfRefreshTokenService.cs::EfRefreshTokenService.RotateAttemptAsync(RotationTarget,Result<RefreshTokenRotationResult>,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "HumanRefreshRotation_ShouldRecoverCommitLossAndRejectSourceReplay"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EfRefreshTokenService.cs::EfRefreshTokenService.RevokeSubjectAttemptAsync(SubjectRevocationTarget,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "HumanRefreshRotation_ShouldRecoverCommitLossAndRejectSourceReplay"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EmployeeMutationObservationReader.cs::EmployeeMutationObservationReader.ObserveAsync(Guid,CancellationToken)::ObserveSnapshotAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EmployeeMutationObservation_ShouldUseOneSnapshotAcrossConcurrentMutation"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/EmployeeMutationVersionStore.cs::EmployeeMutationVersionStore.TryAdvanceAsync(Guid,uint,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/HumanSessionIssuanceLock.cs::HumanSessionIssuanceLock.ExecuteTransactionAsync(Func<Task>,Func<IIoTDbContext, CancellationToken, Task>,bool,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "OidcIssuanceSuccessAudit_ShouldCommitAtomicallyWithGrant"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/HumanSessionRevocationService.cs::HumanSessionRevocationService.RevokeAllAsync(Guid,string,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldRecoverCommitLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityAccountStore.cs::IdentityAccountStore.CreateAsync(IdentityAccount,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityAccountStore.cs::IdentityAccountStore.CompareExchangeStateAsync(Guid,IdentityAccountStateSnapshot,bool,string,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityAccountStore.cs::IdentityAccountStore.DeleteAsync(Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityAccountStore.cs::IdentityAccountStore.AssignRoleAsync(Guid,string,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityAccountStore.cs::IdentityAccountStore.ReplaceAssignableRoleAsync(Guid,string?,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityPasswordService.cs::IdentityPasswordService.SetPasswordAsync(Guid,string,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityPasswordService.cs::IdentityPasswordService.CheckPasswordAsync(Guid,string,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityPasswordService.cs::IdentityPasswordService.AcquirePasswordCheckLockAsync(IIoTDbContext,Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityPasswordService.cs::IdentityPasswordService.SetStandalonePasswordAsync(Guid,string?,string,bool,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IndependentHumanSessionRevocationService.cs::IndependentHumanSessionRevocationService.ReadBaselineAttemptAsync(Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldRecoverCommitLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IndependentHumanSessionRevocationService.cs::IndependentHumanSessionRevocationService.RevokeAttemptAsync(RevocationTarget,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldRecoverCommitLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IndependentHumanSessionRevocationService.cs::IndependentHumanSessionRevocationService.ObserveOutcomeAttemptAsync(RevocationTarget,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldRecoverCommitLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/OpenIddictClientSeeder.cs::OpenIddictClientSeeder.EnsureAicopilotClientAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/OpenIddictClientSeederPostgresTests.cs",
            "OidcClientSeed_FirstWarmAndCommitLoss_ShouldConvergeToOneClientId"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/RolePolicyService.cs::RolePolicyService.DefineRoleAsync(string,List<string>,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/RolePolicyService.cs::RolePolicyService.UpdateRolePermissionsAsync(string,List<string>,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/RolePolicyService.cs::RolePolicyService.UpdateUserPersonalPermissionsAsync(Guid,List<string>,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Outbox/OutboxMessageDispatcher.cs::OutboxMessageDispatcher.DispatchPendingCoreAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/OutboxDispatchPersistenceTests.cs",
            "OutboxCommitTransient_ShouldRepublishStableIdentityAndReceiverInboxApplyBusinessEffectOnce"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/CloudWriteObservationReader.cs::CloudWriteObservationReader.ObserveConsistentAsync(Func<IIoTDbContext, CancellationToken, Task<T>>,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EmployeeMutationObservation_ShouldUseOneSnapshotAcrossConcurrentMutation"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/DeviceDeletionTransactionLock.cs::DeviceDeletionTransactionLock.AcquireAsync(IIoTDbContext,Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldSeeSessionCommittedWhileWaitingForSubjectLock"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/EfUnitOfWork.cs::EfUnitOfWork.ExecuteResilientAsync(Func<CancellationToken, Task<TResult>>,CancellationToken)::<lambda#1>"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/EfUnitOfWork.cs::EfUnitOfWork.BeginTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/EfUnitOfWork.cs::EfUnitOfWork.CommitAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/EfUnitOfWork.cs::EfUnitOfWork.RollbackAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/RefreshTokenSubjectTransactionLock.cs::RefreshTokenSubjectTransactionLock.AcquireOidcTokenExchangeAsync(IIoTDbContext,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldSeeSessionCommittedWhileWaitingForSubjectLock"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Persistence/RefreshTokenSubjectTransactionLock.cs::RefreshTokenSubjectTransactionLock.AcquireSubjectCoreAsync(IIoTDbContext,Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "IndependentHumanSessionRevocation_ShouldSeeSessionCommittedWhileWaitingForSubjectLock"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/QueryServices/EfDeviceDeletionDependencyService.cs::EfDeviceDeletionDependencyService.DeleteCascadeAsync(Guid,CancellationToken,uint?)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/QueryServices/EfDeviceDeletionDependencyService.cs::EfDeviceDeletionDependencyService.DeleteAssociatedRowsAsync(Guid,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Repository/EfRepository.cs::EfRepository<T>.SaveChangesAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Uploads/EfUploadReceiveObservationRetentionPruner.cs::EfUploadReceiveObservationRetentionPruner.PruneBatchAttemptAsync(DateTimeOffset,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "UploadRegistrationAndOutbox_ShouldRecoverCommitLossAsOneLogicalMessage"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Uploads/EfUploadReceiveRegistry.cs::EfUploadReceiveRegistry.RegisterAttemptAsync(Guid,Guid,string,string?,string,OutboxMessage,DateTimeOffset,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "UploadRegistrationAndOutbox_ShouldRecoverCommitLossAsOneLogicalMessage"),
        ["src/infrastructure/IIoT.EntityFrameworkCore/Uploads/EfUploadReceiveRegistry.cs::EfUploadReceiveRegistry.RecordDuplicateObservationAsync(IIoTDbContext,UploadReceiveRegistration,Guid,DateTimeOffset,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "UploadRegistrationAndOutbox_ShouldRecoverCommitLossAsOneLogicalMessage"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/ActivateEmployee.cs::ActivateEmployeeHandler.Handle(ActivateEmployeeCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/DeactivateEmployee.cs::DeactivateEmployeeHandler.Handle(DeactivateEmployeeCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/OnboardEmployee.cs::OnboardEmployeeHandler.Handle(OnboardEmployeeCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/TerminateEmployee.cs::TerminateEmployeeHandler.Handle(TerminateEmployeeCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/UpdateEmployeeAccess.cs::UpdateEmployeeAccessHandler.Handle(UpdateEmployeeAccessCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/UpdateEmployeeProfile.cs::UpdateEmployeeProfileHandler.Handle(UpdateEmployeeProfileCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.EmployeeService/Commands/Human/Employees/UpdateEmployeeRole.cs::UpdateEmployeeRoleHandler.Handle(UpdateEmployeeRoleCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete"),
        ["src/services/IIoT.MasterDataService/Commands/Human/Processes/CreateProcess.cs::CreateProcessHandler.Handle(CreateProcessCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.MasterDataService/Commands/Human/Processes/DeleteProcess.cs::DeleteProcessHandler.Handle(DeleteProcessCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.MasterDataService/Commands/Human/Processes/UpdateProcess.cs::UpdateProcessHandler.Handle(UpdateProcessCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/ClientReleases/ClientReleaseComponentDeletionProcessor.cs::ClientReleaseComponentDeletionProcessor.PersistDeletionTargetAsync(ClientReleaseDeletionWriteState,ClientReleaseDeletionWriteState,Action<ClientReleaseComponentDeletion>,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/ClientReleases/ClientReleaseComponentDeletionProcessor.cs::ClientReleaseComponentDeletionProcessor.RemoveDeletionAsync(ClientReleaseComponentDeletion,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/ClientReleases/ClientReleaseRetentionService.cs::ClientReleaseRetentionService.ApplyPolicyAsync(ClientReleaseComponentKind,string,string,string,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Edge/ClientVersions/ReportDeviceClientVersion.cs::ReportDeviceClientVersionHandler.Handle(ReportDeviceClientVersionCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReports_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Edge/ClientVersions/ReportDeviceRuntimeHeartbeat.cs::ReportDeviceRuntimeHeartbeatHandler.Handle(ReportDeviceRuntimeHeartbeatCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReports_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Edge/EdgeHosts/ReportEdgeHostPlcRuntimeStates.cs::ReportEdgeHostPlcRuntimeStatesHandler.Handle(ReportEdgeHostPlcRuntimeStatesCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "EdgeReports_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/ChangeClientReleaseLifecycle.cs::ArchiveClientReleaseHandler.Handle(ArchiveClientReleaseCommand,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/DeleteClientReleasePackage.cs::DeleteClientReleasePackageHandler.PersistVersionTargetAsync(ClientReleaseVersionWriteState,ClientReleaseStatus,DateTime?,string?,string?,string?,Action<ClientReleaseComponent, ClientReleaseVersion>,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/GenerateEdgeInstallerPackage.cs::GenerateEdgeInstallerPackageHandler.PersistDeviceSecretsAsync(IReadOnlyCollection<DeviceBootstrapWriteState>,IReadOnlyCollection<DeviceBootstrapSecretTarget>,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/HardDeleteClientReleaseComponent.cs::HardDeleteClientReleaseComponentHandler.Handle(HardDeleteClientReleaseComponentCommand,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/PublishEdgePluginPackage.cs::PublishEdgePluginPackageHandler.Handle(PublishEdgePluginPackageCommand,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/PublishEdgeReleaseBundle.cs::PublishEdgeReleaseBundleHandler.Handle(PublishEdgeReleaseBundleCommand,CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/ClientReleases/UpdateClientReleaseRetentionPolicy.cs::UpdateClientReleaseRetentionPolicyHandler.Handle(UpdateClientReleaseRetentionPolicyCommand,CancellationToken)::ExecuteAttemptAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
            "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/Devices/RegisterDevice.cs::RegisterDeviceHandler.Handle(RegisterDeviceCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/Devices/UpdateDeviceProfile.cs::UpdateDeviceProfileHandler.Handle(UpdateDeviceProfileCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/Recipes/CreateRecipe.cs::CreateRecipeHandler.Handle(CreateRecipeCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/Recipes/DeleteRecipe.cs::DeleteRecipeHandler.Handle(DeleteRecipeCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
        ["src/services/IIoT.ProductionService/Commands/Human/Recipes/UpgradeRecipeVersion.cs::UpgradeRecipeVersionHandler.Handle(UpgradeRecipeVersionCommand,CancellationToken)::ExecuteTransactionAsync(CancellationToken)"] = new(
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs",
            "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss"),
    };

    public static PersistenceInventoryResult DiscoverProduction()
    {
        var repositoryRoot = Directory.GetParent(CloudRepositoryPath.Find("src"))!.FullName;
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var references = CreateMetadataReferences();
        var entries = new List<PersistenceWriteEntry>();
        var unresolved = new List<string>();
        var projectPaths = Directory
            .GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsProductionProject)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unitOfWorkReplayContract = VerifyProductionUnitOfWorkReplayContract(
            repositoryRoot,
            projectPaths,
            references);
        if (!unitOfWorkReplayContract.IsVerified)
        {
            unresolved.AddRange(unitOfWorkReplayContract.Diagnostics);
        }

        var projects = projectPaths
            .Select(projectPath => CreateProjectCompilation(projectPath, references))
            .Where(project => project is not null)
            .Cast<ProjectCompilation>()
            .ToArray();
        var models = projects
            .SelectMany(project => project.Models)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var protectionGraph = ProtectionGraph.Create(
            projects.SelectMany(project => project.Trees).ToArray(),
            models,
            unitOfWorkReplayContract.IsVerified,
            projectPaths
                .Select(GetAssemblyName)
                .ToHashSet(StringComparer.Ordinal));

        foreach (var project in projects)
        {
            DiscoverProject(
                repositoryRoot,
                project,
                protectionGraph,
                entries,
                unresolved);
        }

        var orderedEntries = entries
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Method, StringComparer.Ordinal)
            .ToArray();
        return new PersistenceInventoryResult(
            orderedEntries,
            orderedEntries
                .Where(entry => entry.Classification is null)
                .ToArray(),
            unresolved
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public static PersistenceInventoryResult DiscoverSnippet(
        string source,
        string relativePath = "InventoryFixture.cs",
        bool unitOfWorkReplayContractVerified = true)
    {
        var absolutePath = Path.Combine(
            Environment.CurrentDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, absolutePath);
        var references = CreateMetadataReferences().Values;
        var compilation = CSharpCompilation.Create(
            "PersistenceInventoryFixture",
            [CreateImplicitUsingsTree(absolutePath), tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var sites = new List<WriteSite>();
        var unresolved = new List<string>();
        DiscoverTreeWriteSites(
            Environment.CurrentDirectory,
            tree,
            model,
            sites,
            unresolved);
        var models = new Dictionary<SyntaxTree, SemanticModel>
        {
            [tree] = model
        };
        var graph = ProtectionGraph.Create(
            [tree],
            models,
            unitOfWorkReplayContractVerified);
        var entries = CreateEntries(sites, graph);
        return new PersistenceInventoryResult(
            entries,
            entries.Where(entry => entry.Classification is null).ToArray(),
            unresolved
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public static bool VerifyUnitOfWorkReplayImplementationSnippet(string source)
    {
        var absolutePath = Path.Combine(
            Environment.CurrentDirectory,
            "UnitOfWorkReplayImplementationFixture.cs");
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, absolutePath);
        var compilation = CSharpCompilation.Create(
            "UnitOfWorkReplayImplementationFixture",
            [CreateImplicitUsingsTree(absolutePath), tree],
            CreateMetadataReferences().Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var models = new Dictionary<SyntaxTree, SemanticModel>
        {
            [tree] = compilation.GetSemanticModel(tree, ignoreAccessibility: true)
        };
        var verification = VerifyUnitOfWorkReplayImplementations(
            Environment.CurrentDirectory,
            new ProjectCompilation([tree], models));
        return verification.ImplementationCount > 0 && verification.Diagnostics.Count == 0;
    }

    public static PersistenceInventoryResult DiscoverProjectGraphSnippets(
        string writerSource,
        params string[] callerSources)
    {
        var fixtureRoot = Path.Combine(
            Environment.CurrentDirectory,
            "PersistenceInventoryProjectGraphFixture");
        var references = CreateMetadataReferences().Values.ToArray();
        var writerPath = Path.Combine(fixtureRoot, "Writer.cs");
        var writerTree = CSharpSyntaxTree.ParseText(writerSource, ParseOptions, writerPath);
        var writerCompilation = CSharpCompilation.Create(
            "PersistenceInventoryWriterFixture",
            [CreateImplicitUsingsTree(writerPath), writerTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var compilations = new List<(SyntaxTree Tree, CSharpCompilation Compilation)>
        {
            (writerTree, writerCompilation)
        };
        for (var index = 0; index < callerSources.Length; index++)
        {
            var callerPath = Path.Combine(fixtureRoot, $"Caller{index + 1}.cs");
            var callerTree = CSharpSyntaxTree.ParseText(
                callerSources[index],
                ParseOptions,
                callerPath);
            var callerCompilation = CSharpCompilation.Create(
                $"PersistenceInventoryCallerFixture{index + 1}",
                [CreateImplicitUsingsTree(callerPath), callerTree],
                references.Append(writerCompilation.ToMetadataReference()),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            compilations.Add((callerTree, callerCompilation));
        }

        var models = compilations.ToDictionary(
            pair => pair.Tree,
            pair => pair.Compilation.GetSemanticModel(pair.Tree, ignoreAccessibility: true));
        var sites = new List<WriteSite>();
        var unresolved = new List<string>();
        foreach (var (tree, _) in compilations)
        {
            DiscoverTreeWriteSites(
                Environment.CurrentDirectory,
                tree,
                models[tree],
                sites,
                unresolved);
        }

        var graph = ProtectionGraph.Create(
            compilations.Select(pair => pair.Tree).ToArray(),
            models,
            unitOfWorkReplayContractVerified: true,
            compilations
                .Select(pair => pair.Compilation.AssemblyName!)
                .ToHashSet(StringComparer.Ordinal));
        var entries = CreateEntries(sites, graph);
        return new PersistenceInventoryResult(
            entries,
            entries.Where(entry => entry.Classification is null).ToArray(),
            unresolved
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static UnitOfWorkReplayVerification VerifyProductionUnitOfWorkReplayContract(
        string repositoryRoot,
        IReadOnlyCollection<string> projectPaths,
        IReadOnlyDictionary<string, MetadataReference> allReferences)
    {
        var implementationCount = 0;
        var diagnostics = new List<string>();
        foreach (var projectPath in projectPaths)
        {
            var project = CreateProjectCompilation(projectPath, allReferences);
            if (project is null)
            {
                continue;
            }

            var projectVerification = VerifyUnitOfWorkReplayImplementations(
                repositoryRoot,
                project);
            implementationCount += projectVerification.ImplementationCount;
            diagnostics.AddRange(projectVerification.Diagnostics);
        }

        if (implementationCount == 0)
        {
            diagnostics.Add(
                "IUnitOfWork replay contract invalid: no concrete production implementation was resolved.");
        }

        return new UnitOfWorkReplayVerification(
            implementationCount > 0 && diagnostics.Count == 0,
            implementationCount,
            diagnostics
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static UnitOfWorkReplayVerification VerifyUnitOfWorkReplayImplementations(
        string repositoryRoot,
        ProjectCompilation project)
    {
        const string contractName =
            "IIoT.Services.Contracts.Persistence.IUnitOfWork";
        var implementations = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in project.Trees)
        {
            var model = project.Models[tree];
            foreach (var declaration in tree.GetRoot()
                         .DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol
                    {
                        TypeKind: TypeKind.Class,
                        IsAbstract: false
                    } type ||
                    !type.AllInterfaces.Any(candidate =>
                        candidate.ToDisplayString() == contractName))
                {
                    continue;
                }

                implementations.Add(type);
            }
        }

        var diagnostics = new List<string>();
        foreach (var implementationType in implementations)
        {
            var contract = implementationType.AllInterfaces.Single(candidate =>
                candidate.ToDisplayString() == contractName);
            var contractMethod = contract.GetMembers("ExecuteResilientAsync")
                .OfType<IMethodSymbol>()
                .SingleOrDefault();
            var implementation = contractMethod is null
                ? null
                : implementationType.FindImplementationForInterfaceMember(contractMethod)
                    as IMethodSymbol;
            if (implementation is not null &&
                RoutesOperationThroughExecutionStrategy(implementation, project.Models))
            {
                continue;
            }

            var declaration = implementationType.DeclaringSyntaxReferences
                .FirstOrDefault()
                ?.GetSyntax();
            var path = declaration is null
                ? implementationType.ToDisplayString()
                : NormalizePath(Path.GetRelativePath(
                    repositoryRoot,
                    declaration.SyntaxTree.FilePath));
            diagnostics.Add(
                $"IUnitOfWork replay contract invalid: {path}::{implementationType.ToDisplayString()}.ExecuteResilientAsync does not route its operation delegate through IExecutionStrategy.");
        }

        return new UnitOfWorkReplayVerification(
            implementations.Count > 0 && diagnostics.Count == 0,
            implementations.Count,
            diagnostics);
    }

    private static bool RoutesOperationThroughExecutionStrategy(
        IMethodSymbol implementation,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
        => RoutesDelegateThroughExecutionStrategy(
            implementation,
            models,
            "operation");

    private static bool RoutesDelegateThroughExecutionStrategy(
        IMethodSymbol implementation,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        string parameterName)
    {
        var operationParameter = implementation.Parameters.SingleOrDefault(parameter =>
            parameter.Name == parameterName && parameter.Type.TypeKind == TypeKind.Delegate);
        if (operationParameter is null)
        {
            return false;
        }

        foreach (var syntaxReference in implementation.DeclaringSyntaxReferences)
        {
            var declaration = syntaxReference.GetSyntax();
            if (!models.TryGetValue(declaration.SyntaxTree, out var model))
            {
                continue;
            }

            var trackedDelegates = new HashSet<ISymbol>(SymbolEqualityComparer.Default)
            {
                operationParameter
            };
            var aliasSources = new HashSet<SyntaxNode>();
            var aliasTargets = new HashSet<SyntaxNode>();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var variable in declaration
                             .DescendantNodes()
                             .OfType<VariableDeclaratorSyntax>())
                {
                    if (variable.Initializer?.Value is not { } initializer ||
                        model.GetDeclaredSymbol(variable) is not ILocalSymbol
                        {
                            Type.TypeKind: TypeKind.Delegate
                        } local ||
                        GetDirectDelegateSource(initializer, model) is not { } source ||
                        !trackedDelegates.Contains(source))
                    {
                        continue;
                    }

                    if (trackedDelegates.Add(local))
                    {
                        changed = true;
                    }

                    aliasSources.Add(initializer);
                }

                foreach (var assignment in declaration
                             .DescendantNodes()
                             .OfType<AssignmentExpressionSyntax>()
                             .Where(candidate => candidate.IsKind(
                                 SyntaxKind.SimpleAssignmentExpression)))
                {
                    if (model.GetSymbolInfo(assignment.Left).Symbol is not ILocalSymbol
                        {
                            Type.TypeKind: TypeKind.Delegate
                        } local ||
                        GetDirectDelegateSource(assignment.Right, model) is not { } source ||
                        !trackedDelegates.Contains(source))
                    {
                        continue;
                    }

                    if (trackedDelegates.Add(local))
                    {
                        changed = true;
                    }

                    aliasSources.Add(assignment.Right);
                    aliasTargets.Add(assignment.Left);
                }
            }

            if (HasUnsafeAliasMutation(
                    declaration,
                    model,
                    trackedDelegates))
            {
                continue;
            }

            var protectedScopes = new List<SyntaxNode>();
            var directStrategyArguments = new List<SyntaxNode>();
            foreach (var invocation in declaration
                         .DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                if (model.GetOperation(invocation) is not IInvocationOperation operation ||
                    !IsExecutionStrategyInvocation(operation.TargetMethod))
                {
                    continue;
                }

                foreach (var argument in operation.Arguments.Where(argument =>
                             argument.Parameter is { } parameter &&
                             IsExecutionStrategyOperationParameter(parameter)))
                {
                    if (GetDirectDelegateSource(argument.Value.Syntax, model) is { } source &&
                        trackedDelegates.Contains(source))
                    {
                        directStrategyArguments.Add(argument.Value.Syntax);
                    }
                    else
                    {
                        protectedScopes.Add(argument.Value.Syntax);
                    }
                }
            }

            var protectedDelegateInvocations = declaration
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                    InvokesTrackedDelegate(invocation, model, trackedDelegates) &&
                    protectedScopes.Any(scope => scope.Span.Contains(invocation.Span)))
                .ToArray();
            var routed = directStrategyArguments.Count > 0 ||
                         protectedDelegateInvocations.Length > 0;
            if (!routed)
            {
                continue;
            }

            var references = declaration
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(reference =>
                    model.GetSymbolInfo(reference).Symbol is { } symbol &&
                    trackedDelegates.Contains(symbol));
            if (references.All(reference =>
                    aliasSources.Any(source => source.Span.Contains(reference.Span)) ||
                    aliasTargets.Any(target => target.Span.Contains(reference.Span)) ||
                    directStrategyArguments.Any(argument =>
                        argument.Span.Contains(reference.Span)) ||
                    IsNullGuardReference(reference, model, operationParameter) ||
                    protectedDelegateInvocations.Any(invocation =>
                        invocation.Span.Contains(reference.Span))))
            {
                return true;
            }
        }

        return false;
    }

    private static ISymbol? GetDirectDelegateSource(
        SyntaxNode syntax,
        SemanticModel model)
    {
        var operation = model.GetOperation(syntax);
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        while (operation is IDelegateCreationOperation delegateCreation)
        {
            operation = delegateCreation.Target;
        }

        return operation switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation local => local.Local,
            _ => null
        };
    }

    private static bool HasUnsafeAliasMutation(
        SyntaxNode declaration,
        SemanticModel model,
        IReadOnlySet<ISymbol> trackedDelegates)
    {
        foreach (var variable in declaration
                     .DescendantNodes()
                     .OfType<VariableDeclaratorSyntax>())
        {
            if (model.GetDeclaredSymbol(variable) is not ILocalSymbol local ||
                !trackedDelegates.Contains(local))
            {
                continue;
            }

            if (variable.Initializer?.Value is not { } initializer ||
                GetDirectDelegateSource(initializer, model) is not { } source ||
                !trackedDelegates.Contains(source))
            {
                return true;
            }
        }

        foreach (var assignment in declaration
                     .DescendantNodes()
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (model.GetSymbolInfo(assignment.Left).Symbol is not { } target ||
                !trackedDelegates.Contains(target))
            {
                continue;
            }

            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                GetDirectDelegateSource(assignment.Right, model) is not { } source ||
                !trackedDelegates.Contains(source))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InvokesTrackedDelegate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        IReadOnlySet<ISymbol> trackedDelegates)
    {
        ExpressionSyntax? receiver = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Invoke",
                Expression: var expression
            } => expression,
            _ => null
        };
        return receiver is not null &&
               model.GetSymbolInfo(receiver).Symbol is { } symbol &&
               trackedDelegates.Contains(symbol);
    }

    private static bool IsNullGuardReference(
        IdentifierNameSyntax reference,
        SemanticModel model,
        IParameterSymbol operationParameter)
    {
        if (!SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(reference).Symbol,
                operationParameter) ||
            reference.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault() is not
                { Parent.Parent: InvocationExpressionSyntax invocation })
        {
            return false;
        }

        return GetMethodSymbols(model.GetSymbolInfo(invocation)).Any(method =>
            method.Name == "ThrowIfNull" &&
            method.ContainingType.ToDisplayString() == "System.ArgumentNullException");
    }

    private static bool IsExecutionStrategyOperationParameter(IParameterSymbol parameter)
        => parameter.Type.TypeKind == TypeKind.Delegate &&
           parameter.Name is "operation" or "attempt" or "stage";

    private static bool IsExecutionStrategyInvocation(IMethodSymbol symbol)
    {
        var method = symbol.ReducedFrom ?? symbol;
        var namespaceName = method.ContainingNamespace.ToDisplayString();
        return (method.Name is "Execute" or "ExecuteAsync") &&
               (symbol.ReceiverType?.ToDisplayString() ==
                "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy" ||
                method.ContainingType.ToDisplayString() is
                    "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy" or
                    "Microsoft.EntityFrameworkCore.ExecutionStrategyExtensions" ||
                namespaceName.StartsWith(
                    "Microsoft.EntityFrameworkCore.Storage",
                    StringComparison.Ordinal));
    }

    public static bool IsIncludedProductionSource(string path)
        => !IsExcludedSource(path) && IsProductionSourcePath(path);

    public static void AssertEvidenceExists(PersistenceEvidence evidence)
    {
        var path = CloudRepositoryPath.Find(
            evidence.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions).GetRoot();
        var testMethod = root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method =>
                string.Equals(method.Identifier.ValueText, evidence.TestMethod, StringComparison.Ordinal));

        Assert.NotNull(testMethod);
        Assert.Contains(
            testMethod.AttributeLists.SelectMany(list => list.Attributes),
            attribute => attribute.Name.ToString() is "Fact" or "Theory" or "FactAttribute" or "TheoryAttribute");
        Assert.True(
            testMethod.Body is { Statements.Count: > 0 } || testMethod.ExpressionBody is not null,
            $"Persistence evidence test has no body: {evidence.RelativePath}::{evidence.TestMethod}");
    }

    private static void DiscoverProject(
        string repositoryRoot,
        ProjectCompilation project,
        ProtectionGraph protectionGraph,
        ICollection<PersistenceWriteEntry> entries,
        ICollection<string> unresolved)
    {
        var projectWriteSites = new List<WriteSite>();
        foreach (var tree in project.Trees)
        {
            DiscoverTreeWriteSites(
                repositoryRoot,
                tree,
                project.Models[tree],
                projectWriteSites,
                unresolved);
        }

        foreach (var entry in CreateEntries(projectWriteSites, protectionGraph))
        {
            entries.Add(entry);
        }
    }

    private static ProjectCompilation? CreateProjectCompilation(
        string projectPath,
        IReadOnlyDictionary<string, MetadataReference> allReferences)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourcePaths = Directory
            .GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedSource(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (sourcePaths.Length == 0)
        {
            return null;
        }

        var trees = sourcePaths
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                ParseOptions,
                path))
            .ToArray();
        var assemblyName = GetAssemblyName(projectPath);
        var references = allReferences
            .Where(pair => !string.Equals(
                pair.Key,
                assemblyName + ".dll",
                StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees.Prepend(CreateImplicitUsingsTree(projectPath)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));

        var models = trees.ToDictionary(
            tree => tree,
            tree => compilation.GetSemanticModel(tree, ignoreAccessibility: true));
        return new ProjectCompilation(trees, models);
    }

    private static IReadOnlyList<PersistenceWriteEntry> CreateEntries(
        IReadOnlyCollection<WriteSite> writeSites,
        ProtectionGraph protectionGraph)
    {
        return writeSites
            .GroupBy(site => site.Callable, SymbolEqualityComparer.Default)
            .Select(group =>
            {
                var sites = group.ToArray();
                var first = sites[0];
                var automaticClassification = sites.All(site =>
                    protectionGraph.IsInsideReplayRoot(site.Syntax, site.Callable));
                var classification = automaticClassification
                    ? PersistenceWriteClassification.ExecutionStrategyReplayRoot
                    : ClassifyKnownBoundary(first.Callable);
                var evidence = ResolveEvidence(
                    first.RelativePath,
                    first.Callable,
                    classification);
                return new PersistenceWriteEntry(
                    first.RelativePath,
                    first.Line,
                    GetCallableIdentity(first.Callable),
                    sites.Select(site => site.Kind)
                        .Distinct()
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    classification,
                    evidence);
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Method, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetCallableIdentity(IMethodSymbol callable)
    {
        if (callable.MethodKind is not MethodKind.LocalFunction and not MethodKind.AnonymousFunction)
        {
            var containingType = callable.ContainingType?.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "<unknown-type>";
            return $"{containingType}.{FormatCallableSegment(callable)}";
        }

        var containingCallable = callable.ContainingSymbol as IMethodSymbol;
        var containingIdentity = containingCallable is null
            ? callable.ContainingType?.ToDisplayString() ?? "<unknown-type>"
            : GetCallableIdentity(containingCallable);
        if (callable.MethodKind == MethodKind.LocalFunction)
        {
            return $"{containingIdentity}::{FormatCallableSegment(callable)}";
        }

        return $"{containingIdentity}::<lambda#{GetAnonymousFunctionOrdinal(callable)}>";
    }

    private static string FormatCallableSegment(IMethodSymbol callable)
        => $"{callable.Name}({string.Join(
            ",",
            callable.Parameters.Select(parameter =>
                parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})";

    private static int GetAnonymousFunctionOrdinal(IMethodSymbol callable)
    {
        var anonymous = callable.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<AnonymousFunctionExpressionSyntax>()
            .SingleOrDefault();
        if (anonymous is null)
        {
            return 0;
        }

        var owner = anonymous.Ancestors().FirstOrDefault(node =>
            node is BaseMethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                AccessorDeclarationSyntax);
        if (owner is null)
        {
            return 0;
        }

        return owner.DescendantNodes()
                   .OfType<AnonymousFunctionExpressionSyntax>()
                   .TakeWhile(candidate => candidate != anonymous)
                   .Count() + 1;
    }

    private static void DiscoverTreeWriteSites(
        string repositoryRoot,
        SyntaxTree tree,
        SemanticModel semanticModel,
        ICollection<WriteSite> sites,
        ICollection<string> unresolved)
    {
        var root = tree.GetRoot();
        var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, tree.FilePath));
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var methods = GetMethodSymbols(symbolInfo);
            var sink = methods
                .Select(method =>
                    TryClassifySink(method, out var kind)
                        ? (Method: method, Kind: kind)
                        : ((IMethodSymbol Method, string Kind)?)null)
                .FirstOrDefault(candidate => candidate is not null);
            if (sink is null)
            {
                if (methods.Count == 0 &&
                    IsUnresolvedPersistenceInvocation(invocation, semanticModel))
                {
                    var name = TryGetInvocationName(invocation) ?? "<unknown>";
                    unresolved.Add(FormatLocation(relativePath, invocation, name));
                }

                continue;
            }

            var (method, kind) = sink.Value;

            var callable = semanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            if (callable is null)
            {
                unresolved.Add(FormatLocation(relativePath, invocation, method.Name));
                continue;
            }

            if (kind == "dapper-write" &&
                IsProvenReadOnlyDapperInvocation(invocation, semanticModel))
            {
                continue;
            }

            sites.Add(new WriteSite(
                relativePath,
                GetLine(invocation),
                invocation,
                callable,
                kind));
        }

        foreach (var methodReference in root
                     .DescendantNodes()
                     .OfType<SimpleNameSyntax>()
                     .Where(reference => GetDirectInvocation(reference) is null))
        {
            var methodSymbols = GetMethodSymbols(
                semanticModel.GetSymbolInfo(methodReference));
            var sink = methodSymbols
                .Select(method =>
                    TryClassifySink(method, out var kind)
                        ? (Method: method, Kind: kind)
                        : ((IMethodSymbol Method, string Kind)?)null)
                .FirstOrDefault(candidate => candidate is not null);
            if (sink is null)
            {
                if (methodSymbols.Count == 0 &&
                    IsUnresolvedPersistenceReference(
                        methodReference,
                        semanticModel))
                {
                    unresolved.Add(FormatLocation(
                        relativePath,
                        methodReference,
                        methodReference.Identifier.ValueText));
                }

                continue;
            }

            var (symbol, kind) = sink.Value;

            var callable = semanticModel.GetEnclosingSymbol(methodReference.SpanStart) as IMethodSymbol;
            if (callable is null)
            {
                unresolved.Add(FormatLocation(relativePath, methodReference, symbol.Name));
                continue;
            }

            sites.Add(new WriteSite(
                relativePath,
                GetLine(methodReference),
                methodReference,
                callable,
                kind + "-method-group"));
        }
    }

    private static PersistenceWriteClassification? ClassifyKnownBoundary(
        IMethodSymbol callable)
    {
        var typeName = callable.ContainingType?.ToDisplayString() ?? string.Empty;
        var methodName = callable.Name;

        if (typeName is
            "IIoT.EntityFrameworkCore.IIoTDbContext" or
            "IIoT.EntityFrameworkCore.Repository.EfRepository<T>" or
            "IIoT.EntityFrameworkCore.ClientReleases.EfClientReleaseComponentDeletionStore" or
            "IIoT.EntityFrameworkCore.ClientReleases.EfDeviceClientStateStore" or
            "IIoT.EntityFrameworkCore.EdgeHosts.EfEdgeHostPlcRuntimeStateStore" or
            "IIoT.EntityFrameworkCore.Identity.HumanSessionRevocationService" or
            "IIoT.EntityFrameworkCore.Persistence.EfUnitOfWork" or
            "IIoT.EntityFrameworkCore.Persistence.DeviceDeletionTransactionLock" or
            "IIoT.EntityFrameworkCore.Persistence.RefreshTokenSubjectTransactionLock")
        {
            return PersistenceWriteClassification.TransactionParticipant;
        }

        if ((typeName ==
                 "IIoT.EntityFrameworkCore.Auditing.EfOidcIssuanceAuditTrailService" &&
             methodName == "StageSuccessAsync") ||
            (typeName ==
                 "IIoT.EntityFrameworkCore.Identity.EmployeeMutationVersionStore" &&
             methodName == "TryAdvanceAsync") ||
            (typeName == "IIoT.EntityFrameworkCore.Identity.IdentityAccountStore" &&
             methodName is "CreateAsync" or "CompareExchangeStateAsync" or
                 "DeleteAsync" or "AssignRoleAsync" or "ReplaceAssignableRoleAsync") ||
            (typeName == "IIoT.EntityFrameworkCore.Identity.IdentityPasswordService" &&
             methodName == "SetPasswordAsync"))
        {
            return PersistenceWriteClassification.TransactionParticipant;
        }

        if ((typeName == "IIoT.Dapper.Initializers.RecordSchemaInitializer" &&
             methodName == "InitializeAsync") ||
            (typeName ==
                 "IIoT.Dapper.Production.Repositories.Capacities.HourlyCapacityRecordRepository" &&
             methodName == "UpsertAsync") ||
            (typeName ==
                 "IIoT.Dapper.Production.Repositories.DeviceLogs.DeviceLogRecordRepository" &&
             methodName == "InsertBatchAsync") ||
            (typeName ==
                 "IIoT.Dapper.Production.Repositories.PassStations.PassStationRecordRepository" &&
             methodName == "InsertBatchAsync") ||
            (typeName == "IIoT.EntityFrameworkCore.Identity.OpenIddictClientSeeder" &&
             methodName == "EnsureAicopilotClientAsync"))
        {
            return PersistenceWriteClassification.StableKeyOrExactObservation;
        }

        if ((typeName == "IIoT.EntityFrameworkCore.Uploads.EfUploadReceiveRegistry" &&
             methodName == "RecordDuplicateObservationAsync") ||
            (typeName ==
                 "IIoT.ProductionService.Commands.ClientReleases.PublishEdgePluginPackageHandler" &&
             methodName == "Handle") ||
            (typeName ==
                 "IIoT.ProductionService.Commands.ClientReleases.PublishEdgeReleaseBundleHandler" &&
             methodName == "Handle"))
        {
            return PersistenceWriteClassification.StableKeyOrExactObservation;
        }

        return null;
    }

    private static PersistenceEvidence ResolveEvidence(
        string relativePath,
        IMethodSymbol callable,
        PersistenceWriteClassification? classification)
    {
        _ = classification;
        var bindingKey = $"{NormalizePath(relativePath)}::{GetCallableIdentity(callable)}";
        return ConcreteEvidenceBindings.TryGetValue(bindingKey, out var evidence)
            ? evidence
            : ArchitectureEvidence;
    }


    private static bool TryClassifySink(IMethodSymbol symbol, out string kind)
    {
        var method = symbol.ReducedFrom ?? symbol;
        var type = method.ContainingType;
        var typeName = type?.ToDisplayString() ?? string.Empty;
        var namespaceName = type?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if ((method.Name is "SaveChanges" or "SaveChangesAsync") &&
            (InheritsFrom(type, "Microsoft.EntityFrameworkCore.DbContext") ||
             IsIiotRepositoryOrStore(type)))
        {
            kind = "ef-save";
            return true;
        }

        if ((method.Name is "BeginTransaction" or "BeginTransactionAsync" or
             "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync") &&
            IsTransactionType(type, namespaceName))
        {
            kind = "manual-transaction";
            return true;
        }

        if (method.Name.StartsWith("ExecuteSql", StringComparison.Ordinal) &&
            namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            kind = "ef-raw-sql";
            return true;
        }

        if ((method.Name is "ExecuteUpdate" or "ExecuteUpdateAsync" or
             "ExecuteDelete" or "ExecuteDeleteAsync") &&
            namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            kind = "ef-bulk-write";
            return true;
        }

        if (typeName == "Dapper.SqlMapper" &&
            method.Parameters.Any(parameter =>
                (parameter.Name.Equals("sql", StringComparison.OrdinalIgnoreCase) &&
                 parameter.Type.SpecialType == SpecialType.System_String) ||
                parameter.Type.ToDisplayString() == "Dapper.CommandDefinition"))
        {
            kind = "dapper-write";
            return true;
        }

        if ((method.Name is "ExecuteNonQuery" or "ExecuteNonQueryAsync" or
             "ExecuteReader" or "ExecuteReaderAsync" or
             "ExecuteScalar" or "ExecuteScalarAsync") &&
            (InheritsFrom(type, "System.Data.Common.DbCommand") ||
             InheritsFrom(type, "System.Data.Common.DbBatch") ||
             typeName is
                 "System.Data.IDbCommand" or
                 "System.Data.Common.DbCommand" or
                 "System.Data.Common.DbBatch"))
        {
            kind = "db-command-write";
            return true;
        }

        if ((method.Name is "Migrate" or "MigrateAsync" or
             "EnsureCreated" or "EnsureCreatedAsync" or
             "EnsureDeleted" or "EnsureDeletedAsync") &&
            namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            kind = "migration";
            return true;
        }

        if (IsOpenIddictMutation(method))
        {
            kind = "oidc-write";
            return true;
        }

        if (IsIdentityManagerMutation(method) ||
            IsIdentityStoreMutation(method))
        {
            kind = "identity-write";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private static bool IsIdentityManagerMutation(IMethodSymbol method)
    {
        if (!IsIdentityManagerType(method.ContainingType))
        {
            return false;
        }

        if (IsIdentityStoreMutationName(
                method.ContainingAssembly,
                method.Name) ||
            IsIdentityResultTask(method.ReturnType) ||
            method.Name == "GenerateNewTwoFactorRecoveryCodesAsync" ||
            method.Name.StartsWith("UpdateNormalized", StringComparison.Ordinal))
        {
            return true;
        }

        if (method.Name is "Dispose" or "RegisterTokenProvider" or
            "CreateSecurityTokenAsync")
        {
            return false;
        }

        return !IdentityManagerReadOnlyMethodNames.Contains(method.Name);
    }

    private static bool IsProvenReadOnlyDapperInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
        {
            return false;
        }

        if (CloudArchitectureAnalyzer.HasCompileTimeReadOnlySql(operation))
        {
            return true;
        }

        if (!CloudArchitectureAnalyzer.IsDapperCommandDefinitionInvocation(operation))
        {
            return false;
        }

        return TryGetDapperCommandDefinitionSql(
                   operation,
                   semanticModel,
                   new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                   out var sql) &&
               CloudArchitectureAnalyzer.IsReadOnlySql(sql);
    }

    private static bool TryGetDapperCommandDefinitionSql(
        IOperation operation,
        SemanticModel semanticModel,
        ISet<ILocalSymbol> visiting,
        out string sql)
    {
        operation = UnwrapOperationConversion(operation);
        if (operation.ConstantValue.HasValue &&
            operation.ConstantValue.Value is string constant)
        {
            sql = constant;
            return true;
        }

        if (operation is IInvocationOperation invocation)
        {
            var commandArgument = invocation.Arguments.FirstOrDefault(argument =>
                argument.Value.Type?.ToDisplayString() == "Dapper.CommandDefinition");
            if (commandArgument is not null)
            {
                return TryGetDapperCommandDefinitionSql(
                    commandArgument.Value,
                    semanticModel,
                    visiting,
                    out sql);
            }
        }

        if (operation is IObjectCreationOperation creation &&
            creation.Type?.ToDisplayString() == "Dapper.CommandDefinition")
        {
            var sqlArgument = creation.Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Name.Equals(
                    "commandText",
                    StringComparison.OrdinalIgnoreCase) == true) ??
                creation.Arguments.FirstOrDefault(argument =>
                    argument.Value.Type?.SpecialType == SpecialType.System_String);
            if (sqlArgument is not null)
            {
                return TryGetDapperCommandDefinitionSql(
                    sqlArgument.Value,
                    semanticModel,
                    visiting,
                    out sql);
            }
        }

        if (operation is ILocalReferenceOperation localReference &&
            visiting.Add(localReference.Local))
        {
            try
            {
                var initializer = localReference.Local.DeclaringSyntaxReferences
                    .Select(reference => reference.GetSyntax())
                    .OfType<VariableDeclaratorSyntax>()
                    .Select(declaration => declaration.Initializer?.Value)
                    .SingleOrDefault(value => value is not null);
                if (initializer is not null &&
                    semanticModel.GetOperation(initializer) is { } initializerOperation)
                {
                    return TryGetDapperCommandDefinitionSql(
                        initializerOperation,
                        semanticModel,
                        visiting,
                        out sql);
                }
            }
            finally
            {
                visiting.Remove(localReference.Local);
            }
        }

        sql = string.Empty;
        return false;
    }

    private static IOperation UnwrapOperationConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool IsOpenIddictMutation(IMethodSymbol method)
    {
        if (!IsOpenIddictManagerOrStoreType(method.ContainingType))
        {
            return false;
        }

        if (method.Name.Contains("Create", StringComparison.Ordinal) ||
            method.Name.Contains("Delete", StringComparison.Ordinal) ||
            method.Name.Contains("Prune", StringComparison.Ordinal) ||
            method.Name.Contains("Purge", StringComparison.Ordinal) ||
            method.Name.Contains("Redeem", StringComparison.Ordinal) ||
            method.Name.Contains("Reject", StringComparison.Ordinal) ||
            method.Name.Contains("Revoke", StringComparison.Ordinal) ||
            method.Name.Contains("Update", StringComparison.Ordinal))
        {
            return true;
        }

        if (method.Name.StartsWith("Add", StringComparison.Ordinal) ||
            method.Name.StartsWith("Remove", StringComparison.Ordinal) ||
            method.Name.StartsWith("Replace", StringComparison.Ordinal) ||
            method.Name.StartsWith("Set", StringComparison.Ordinal))
        {
            return true;
        }

        if (method.Name is "Dispose" or "InstantiateAsync" or "PopulateAsync" ||
            method.Name.StartsWith("Count", StringComparison.Ordinal) ||
            method.Name.StartsWith("Find", StringComparison.Ordinal) ||
            method.Name.StartsWith("Get", StringComparison.Ordinal) ||
            method.Name.StartsWith("Has", StringComparison.Ordinal) ||
            method.Name.StartsWith("List", StringComparison.Ordinal) ||
            method.Name.StartsWith("Validate", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool IsOpenIddictManagerOrStoreType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return EnumerateTypeHierarchy(type).Any(candidate =>
        {
            var namespaceName = candidate.ContainingNamespace.ToDisplayString();
            var isOpenIddictContract = candidate.Name.StartsWith(
                "IOpenIddict",
                StringComparison.Ordinal);
            var isOpenIddictImplementation = candidate.Name.StartsWith(
                "OpenIddict",
                StringComparison.Ordinal);
            return namespaceName.StartsWith("OpenIddict", StringComparison.Ordinal) &&
                   (isOpenIddictContract || isOpenIddictImplementation) &&
                   (candidate.Name.Contains("Manager", StringComparison.Ordinal) ||
                    candidate.Name.Contains("Store", StringComparison.Ordinal));
        });
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(
        INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var contract in type.AllInterfaces)
        {
            yield return contract;
        }
    }

    private static bool IsIdentityStoreMutation(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null)
        {
            return false;
        }

        foreach (var contract in GetIdentityStoreContracts(type))
        {
            foreach (var contractMethod in contract.GetMembers(method.Name)
                         .OfType<IMethodSymbol>()
                         .Where(IsIdentityStoreMutationContractMethod))
            {
                var implementation = type.TypeKind == TypeKind.Interface &&
                                     SymbolEqualityComparer.Default.Equals(type, contract)
                    ? contractMethod
                    : type.FindImplementationForInterfaceMember(contractMethod) as IMethodSymbol;
                if (implementation is not null &&
                    SymbolEqualityComparer.Default.Equals(
                        implementation.OriginalDefinition,
                        method.OriginalDefinition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsIdentityStoreMutationName(
        IAssemblySymbol assembly,
        string methodName)
    {
        var identityNamespace = GetNamespace(
            assembly.GlobalNamespace,
            "Microsoft",
            "AspNetCore",
            "Identity");
        return identityNamespace is not null &&
               identityNamespace.GetTypeMembers()
                   .Where(IsIdentityStoreContract)
                   .SelectMany(type => type.GetMembers(methodName))
                   .OfType<IMethodSymbol>()
                   .Any(IsIdentityStoreMutationContractMethod);
    }

    private static INamespaceSymbol? GetNamespace(
        INamespaceSymbol root,
        params string[] segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            current = current.GetNamespaceMembers().SingleOrDefault(candidate =>
                string.Equals(candidate.Name, segment, StringComparison.Ordinal));
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static IEnumerable<INamedTypeSymbol> GetIdentityStoreContracts(
        INamedTypeSymbol type)
    {
        if (IsIdentityStoreContract(type))
        {
            yield return type;
        }

        foreach (var contract in type.AllInterfaces.Where(IsIdentityStoreContract))
        {
            yield return contract;
        }
    }

    private static bool IsIdentityStoreContract(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Interface &&
           type.Name.EndsWith("Store", StringComparison.Ordinal) &&
           type.ContainingNamespace.ToDisplayString() ==
           "Microsoft.AspNetCore.Identity";

    private static bool IsIdentityStoreMutationContractMethod(IMethodSymbol method)
        => !IdentityStoreReadOnlyMethodNames.Contains(method.Name);

    private static bool IsIdentityResultTask(ITypeSymbol returnType)
        => returnType is INamedTypeSymbol
           {
               Name: "Task",
               TypeArguments.Length: 1
           } task &&
           task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
           task.TypeArguments[0].ToDisplayString() ==
           "Microsoft.AspNetCore.Identity.IdentityResult";

    private static bool IsIdentityManagerType(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace.ToDisplayString() ==
                    "Microsoft.AspNetCore.Identity" &&
                current.MetadataName is "UserManager`1" or "RoleManager`1")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentityStoreType(INamedTypeSymbol? type)
        => type is not null && GetIdentityStoreContracts(type).Any();

    private static bool IsIiotRepositoryOrStore(INamedTypeSymbol? type)
    {
        if (type is null ||
            !type.ContainingNamespace.ToDisplayString().StartsWith("IIoT", StringComparison.Ordinal))
        {
            return false;
        }

        return type.Name.Contains("Repository", StringComparison.Ordinal) ||
               type.Name.Contains("Store", StringComparison.Ordinal) ||
               type.AllInterfaces.Any(candidate =>
                   candidate.Name.Contains("Repository", StringComparison.Ordinal) ||
                   candidate.Name.Contains("Store", StringComparison.Ordinal));
    }

    private static bool IsTransactionType(INamedTypeSymbol? type, string namespaceName)
    {
        if (type is null)
        {
            return false;
        }

        return namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
               namespaceName.StartsWith("System.Data", StringComparison.Ordinal) ||
               namespaceName.StartsWith("Npgsql", StringComparison.Ordinal) ||
               type.ToDisplayString() is
                   "IIoT.Services.Contracts.Persistence.IUnitOfWork" or
                   "Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction" or
                   "System.Data.IDbTransaction" ||
               type.AllInterfaces.Any(candidate =>
                   candidate.ToDisplayString() is
                       "IIoT.Services.Contracts.Persistence.IUnitOfWork" or
                       "Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction" or
                       "System.Data.IDbTransaction");
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, MetadataReference> CreateMetadataReferences()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        foreach (var path in trustedPlatformAssemblies?
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, path) in paths)
        {
            try
            {
                references.Add(fileName, MetadataReference.CreateFromFile(path));
            }
            catch (BadImageFormatException)
            {
                // Native testhost dependencies are not compiler references.
            }
        }

        return references;
    }

    private static SyntaxTree CreateImplicitUsingsTree(string projectPath)
    {
        return CSharpSyntaxTree.ParseText(
            """
            global using System;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Linq;
            global using System.Net.Http;
            global using System.Threading;
            global using System.Threading.Tasks;
            """,
            ParseOptions,
            projectPath + ".PersistenceInventory.GlobalUsings.g.cs");
    }

    private static string GetAssemblyName(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
                   .Descendants()
                   .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                   ?.Value.Trim()
               ?? Path.GetFileNameWithoutExtension(projectPath);
    }

    private static bool IsProductionProject(string projectPath)
    {
        return IsProductionSourcePath(projectPath);
    }

    private static bool IsProductionSourcePath(string path)
    {
        var normalized = "/" + NormalizePath(path).TrimStart('/');
        return !normalized.Contains("/src/tests/", StringComparison.Ordinal) &&
               !normalized.Contains("/src/testing/", StringComparison.Ordinal) &&
               !normalized.Contains("/src/analyzers/", StringComparison.Ordinal);
    }

    private static bool IsExcludedSource(string path)
    {
        var normalized = NormalizePath(path);
        var fileName = Path.GetFileName(path);
        return normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal) ||
               normalized.Contains("/Migrations/", StringComparison.Ordinal) ||
               fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static InvocationExpressionSyntax? GetDirectInvocation(SimpleNameSyntax methodReference)
    {
        return methodReference.Parent switch
        {
            MemberAccessExpressionSyntax
            {
                Name: var name,
                Parent: InvocationExpressionSyntax invocation
            } when name == methodReference && invocation.Expression == methodReference.Parent => invocation,
            MemberBindingExpressionSyntax
            {
                Name: var name,
                Parent: InvocationExpressionSyntax invocation
            } when name == methodReference && invocation.Expression == methodReference.Parent => invocation,
            _ => null
        };
    }

    private static IReadOnlyList<IMethodSymbol> GetMethodSymbols(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return [method];
        }

        return symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .ToArray();
    }

    private static string? TryGetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            SimpleNameSyntax name => name.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: var name } => name.Identifier.ValueText,
            MemberBindingExpressionSyntax { Name: var name } => name.Identifier.ValueText,
            _ => null
        };
    }

    private static bool IsUnresolvedPersistenceInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (TryGetInvocationName(invocation) is not { } name)
        {
            return false;
        }

        if (FailClosedCandidateNames.Contains(name))
        {
            return true;
        }

        var reference = invocation.Expression switch
        {
            SimpleNameSyntax simpleName => simpleName,
            MemberAccessExpressionSyntax { Name: var simpleName } => simpleName,
            MemberBindingExpressionSyntax { Name: var simpleName } => simpleName,
            _ => null
        };
        return reference is not null &&
               IsUnresolvedPersistenceReference(reference, semanticModel);
    }

    private static bool IsUnresolvedPersistenceReference(
        SimpleNameSyntax reference,
        SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(reference);
        if (symbolInfo.Symbol is not null &&
            symbolInfo.Symbol is not IMethodSymbol)
        {
            return false;
        }

        var name = reference.Identifier.ValueText;
        if (FailClosedCandidateNames.Contains(name))
        {
            return true;
        }

        var receiverType = GetReceiverType(reference, semanticModel);
        if (receiverType is null)
        {
            return false;
        }

        return (IsDapperExecutionCandidateName(name) &&
                IsDatabaseConnectionType(receiverType)) ||
               IsIdentityManagerType(receiverType as INamedTypeSymbol) ||
               IsIdentityStoreType(receiverType as INamedTypeSymbol) ||
               IsOpenIddictManagerOrStoreType(receiverType as INamedTypeSymbol);
    }

    private static bool IsDapperExecutionCandidateName(string name)
        => name.StartsWith("Execute", StringComparison.Ordinal) ||
           name.StartsWith("Query", StringComparison.Ordinal);

    private static ITypeSymbol? GetReceiverType(
        SimpleNameSyntax reference,
        SemanticModel semanticModel)
    {
        ExpressionSyntax? receiver = reference.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name == reference => memberAccess.Expression,
            MemberBindingExpressionSyntax memberBinding
                when memberBinding.Name == reference => memberBinding
                    .Ancestors()
                    .OfType<ConditionalAccessExpressionSyntax>()
                    .FirstOrDefault()
                    ?.Expression,
            _ => null
        };
        return receiver is null
            ? null
            : semanticModel.GetTypeInfo(receiver).Type;
    }

    private static bool IsDatabaseConnectionType(ITypeSymbol type)
    {
        var namedType = type as INamedTypeSymbol;
        return (type.ToDisplayString() is
                    "System.Data.IDbConnection" or
                    "System.Data.Common.DbConnection") ||
               namedType?.AllInterfaces.Any(candidate =>
                   candidate.ToDisplayString() == "System.Data.IDbConnection") == true ||
               InheritsFrom(namedType, "System.Data.Common.DbConnection");
    }

    private static string FormatLocation(string relativePath, SyntaxNode syntax, string name)
        => $"{relativePath}:{GetLine(syntax)} unresolved {name}";

    private static int GetLine(SyntaxNode syntax)
        => syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed class ProtectionGraph
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<SyntaxNode>> _references;
        private readonly IReadOnlyDictionary<SyntaxTree, SemanticModel> _models;
        private readonly bool _unitOfWorkReplayContractVerified;
        private readonly Dictionary<string, bool> _memo = new(StringComparer.Ordinal);
        private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);

        private ProtectionGraph(
            IReadOnlyDictionary<string, IReadOnlyList<SyntaxNode>> references,
            IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
            bool unitOfWorkReplayContractVerified)
        {
            _references = references;
            _models = models;
            _unitOfWorkReplayContractVerified = unitOfWorkReplayContractVerified;
        }

        public static ProtectionGraph Create(
            IReadOnlyCollection<SyntaxTree> trees,
            IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
            bool unitOfWorkReplayContractVerified = true,
            IReadOnlySet<string>? analyzedAssemblyNames = null)
        {
            analyzedAssemblyNames ??= models.Values
                .Select(model => model.Compilation.AssemblyName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            var references = new Dictionary<string, List<SyntaxNode>>(
                StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                var model = models[tree];
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    ISymbol? symbol = node switch
                    {
                        InvocationExpressionSyntax invocation =>
                            model.GetSymbolInfo(invocation).Symbol,
                        SimpleNameSyntax name when GetDirectInvocation(name) is null =>
                            model.GetSymbolInfo(name).Symbol,
                        _ => null
                    };
                    if (symbol is not IMethodSymbol method ||
                        (!method.Locations.Any(location => location.IsInSource) &&
                         !analyzedAssemblyNames.Contains(
                             method.ContainingAssembly.Identity.Name)))
                    {
                        continue;
                    }

                    var methodKey = GetCallableGraphKey(method);
                    if (!references.TryGetValue(methodKey, out var methodReferences))
                    {
                        methodReferences = [];
                        references.Add(methodKey, methodReferences);
                    }

                    methodReferences.Add(node);
                }
            }

            var readOnlyReferences =
                new Dictionary<string, IReadOnlyList<SyntaxNode>>(StringComparer.Ordinal);
            foreach (var (methodKey, methodReferences) in references)
            {
                readOnlyReferences.Add(methodKey, methodReferences);
            }

            return new ProtectionGraph(
                readOnlyReferences,
                models,
                unitOfWorkReplayContractVerified);
        }

        public bool IsInsideReplayRoot(SyntaxNode writeSyntax, IMethodSymbol callable)
        {
            var model = _models[writeSyntax.SyntaxTree];
            if (IsProtectedDelegateArgument(writeSyntax, model) ||
                (writeSyntax is InvocationExpressionSyntax &&
                 IsInsideProtectedDelegate(writeSyntax, model)))
            {
                return true;
            }

            return IsProtectedCallable(callable);
        }

        private bool IsProtectedCallable(IMethodSymbol callable)
        {
            var callableKey = GetCallableGraphKey(callable);
            if (_memo.TryGetValue(callableKey, out var cached))
            {
                return cached;
            }

            if (!_visiting.Add(callableKey))
            {
                return false;
            }

            try
            {
                if (!_references.TryGetValue(callableKey, out var references) ||
                    references.Count == 0)
                {
                    return _memo[callableKey] = false;
                }

                var protectedEverywhere = references.All(reference =>
                {
                    var model = _models[reference.SyntaxTree];
                    if (IsProtectedDelegateArgument(reference, model) ||
                        (reference is InvocationExpressionSyntax &&
                         IsInsideProtectedDelegate(reference, model)))
                    {
                        return true;
                    }

                    var caller = model.GetEnclosingSymbol(reference.SpanStart) as IMethodSymbol;
                    return caller is not null &&
                           !string.Equals(
                               GetCallableGraphKey(caller),
                               callableKey,
                               StringComparison.Ordinal) &&
                           IsProtectedCallable(caller);
                });
                return _memo[callableKey] = protectedEverywhere;
            }
            finally
            {
                _visiting.Remove(callableKey);
            }
        }

        private static string GetCallableGraphKey(IMethodSymbol symbol)
        {
            var method = (symbol.ReducedFrom ?? symbol).OriginalDefinition;
            var assemblyName = method.ContainingAssembly?.Identity.Name ?? "<unknown-assembly>";
            if (DocumentationCommentId.CreateDeclarationId(method) is { } declarationId)
            {
                return $"{assemblyName}|{declarationId}";
            }

            if (method.DeclaringSyntaxReferences.FirstOrDefault() is { } syntaxReference)
            {
                var syntax = syntaxReference.GetSyntax();
                return $"{assemblyName}|{NormalizePath(syntax.SyntaxTree.FilePath)}|" +
                       $"{syntax.SpanStart}|{method.MethodKind}|{method.Name}";
            }

            return $"{assemblyName}|" +
                   method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private bool IsInsideProtectedDelegate(SyntaxNode node, SemanticModel model)
        {
            foreach (var anonymous in node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
            {
                if (IsProtectedDelegateArgument(anonymous, model))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsProtectedDelegateArgument(SyntaxNode node, SemanticModel model)
        {
            var argument = node.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            if (argument?.Parent?.Parent is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (model.GetOperation(invocation) is not IInvocationOperation invocationOperation ||
                model.GetOperation(argument) is not IArgumentOperation argumentOperation ||
                argumentOperation.Parameter is not { } parameter)
            {
                return false;
            }

            return IsProtectedExecutor(invocationOperation.TargetMethod) &&
                   IsReplayOperationParameter(parameter);
        }

        private static bool IsReplayOperationParameter(IParameterSymbol parameter)
            => parameter.Type.TypeKind == TypeKind.Delegate &&
               parameter.Name is "operation" or "attempt" or "stage";

        private bool IsProtectedExecutor(IMethodSymbol symbol)
        {
            var method = symbol.ReducedFrom ?? symbol;
            if (method.Name == "ExecuteResilientAsync" &&
                (method.ContainingType.ToDisplayString() ==
                 "IIoT.Services.Contracts.Persistence.IUnitOfWork" ||
                 method.ContainingType.AllInterfaces.Any(candidate =>
                     candidate.ToDisplayString() ==
                     "IIoT.Services.Contracts.Persistence.IUnitOfWork")))
            {
                return _unitOfWorkReplayContractVerified;
            }

            if (IsExecutionStrategyExecutor(symbol))
            {
                return true;
            }

            var isKnownHelper =
                (method.Name == "ExecuteFreshStageAsync" &&
                 method.ContainingType.ToDisplayString() ==
                 "IIoT.MigrationWorkApp.DatabaseInitializationOrchestrator") ||
                (method.Name == "ExecuteRecoverableAsync" &&
                 method.ContainingType.ToDisplayString() is
                     "IIoT.EntityFrameworkCore.Identity.RolePolicyService" or
                     "IIoT.EntityFrameworkCore.Identity.IdentityPasswordService");
            var replayParameterName = method.Name == "ExecuteFreshStageAsync"
                ? "stage"
                : "attempt";
            return isKnownHelper && RoutesDelegateThroughExecutionStrategy(
                method,
                _models,
                replayParameterName);
        }

        private static bool IsExecutionStrategyExecutor(IMethodSymbol symbol)
        {
            var method = symbol.ReducedFrom ?? symbol;
            var namespaceName = method.ContainingNamespace.ToDisplayString();
            return (method.Name is "Execute" or "ExecuteAsync") &&
                   (symbol.ReceiverType?.ToDisplayString() ==
                    "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy" ||
                    method.ContainingType.ToDisplayString() is
                        "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy" or
                        "Microsoft.EntityFrameworkCore.ExecutionStrategyExtensions" ||
                    namespaceName.StartsWith(
                        "Microsoft.EntityFrameworkCore.Storage",
                        StringComparison.Ordinal));
        }
    }

    private sealed record ProjectCompilation(
        IReadOnlyList<SyntaxTree> Trees,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> Models);

    private sealed record UnitOfWorkReplayVerification(
        bool IsVerified,
        int ImplementationCount,
        IReadOnlyList<string> Diagnostics);

    private sealed record WriteSite(
        string RelativePath,
        int Line,
        SyntaxNode Syntax,
        IMethodSymbol Callable,
        string Kind);
}

internal sealed record PersistenceInventoryResult(
    IReadOnlyList<PersistenceWriteEntry> Entries,
    IReadOnlyList<PersistenceWriteEntry> UnclassifiedEntries,
    IReadOnlyList<string> UnresolvedCandidates);

internal sealed record PersistenceWriteEntry(
    string RelativePath,
    int Line,
    string Method,
    IReadOnlyList<string> SinkKinds,
    PersistenceWriteClassification? Classification,
    PersistenceEvidence Evidence)
{
    public string Diagnostic =>
        $"{RelativePath}:{Line} {Method} [{string.Join(",", SinkKinds)}]";
}

internal sealed record PersistenceEvidence(string RelativePath, string TestMethod);

internal enum PersistenceWriteClassification
{
    ExecutionStrategyReplayRoot,
    TransactionParticipant,
    StableKeyOrExactObservation
}
