using System.Linq.Expressions;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Services.Contracts.Auditing;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;

namespace IIoT.ServiceLayer.Tests;

internal sealed class InMemoryRepository<T> : IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    public T? SingleOrDefaultResult { get; set; }

    public List<T> ListResult { get; } = [];

    public ISpecification<T>? LastGetListSpecification { get; private set; }

    public ISpecification<T>? LastGetSingleOrDefaultSpecification { get; private set; }

    public ISpecification<T>? LastCountSpecification { get; private set; }

    public ISpecification<T>? LastAnySpecification { get; private set; }

    public T? AddedEntity { get; private set; }

    public List<T> UpdatedEntities { get; } = [];

    public int SaveChangesResult { get; set; } = 1;

    public T Add(T entity)
    {
        AddedEntity = entity;
        ListResult.Add(entity);
        return entity;
    }

    public void Update(T entity)
    {
        UpdatedEntities.Add(entity);
        if (!ListResult.Contains(entity))
        {
            ListResult.Add(entity);
        }
    }

    public void Delete(T entity)
    {
        ListResult.Remove(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SaveChangesResult);
    }

    public Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        LastGetListSpecification = specification;
        return Task.FromResult(ListResult.ToList());
    }

    public Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        LastGetSingleOrDefaultSpecification = specification;
        return Task.FromResult(SingleOrDefaultResult);
    }

    public Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        LastCountSpecification = specification;
        return Task.FromResult(ListResult.Count);
    }

    public Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        LastAnySpecification = specification;
        return Task.FromResult(ListResult.Count > 0);
    }

    public Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ListResult.AsQueryable().Any(predicate));
    }

    public Task<int> CountAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ListResult.AsQueryable().Count(predicate));
    }
}

internal sealed class RecordingCacheService(List<string>? callOrder = null) : ICacheService
{
    public List<string> RemovedKeys { get; } = [];
    public List<string> RemovedPatterns { get; } = [];
    public string? LastSetKey { get; private set; }
    public TimeSpan? LastAbsoluteExpireTime { get; private set; }
    public Dictionary<string, object?> Values { get; } = [];
    public int GetOrSetCalls { get; private set; }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (Values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return Task.FromResult<T?>(typedValue);
        }

        return Task.FromResult(default(T));
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? absoluteExpireTime = null,
        CancellationToken cancellationToken = default)
    {
        GetOrSetCalls++;

        if (Values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        var created = await factory(cancellationToken);
        LastSetKey = key;
        LastAbsoluteExpireTime = absoluteExpireTime;
        Values[key] = created;
        return created;
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpireTime = null,
        CancellationToken cancellationToken = default)
    {
        LastSetKey = key;
        LastAbsoluteExpireTime = absoluteExpireTime;
        Values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        callOrder?.Add($"cache:{key}");
        RemovedKeys.Add(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        callOrder?.Add($"cache-pattern:{pattern}");
        RemovedPatterns.Add(pattern);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingHourlyCapacityRecordRepository : IHourlyCapacityRecordRepository
{
    public HourlyCapacityWriteModel? LastUpsert { get; private set; }

    public Task UpsertAsync(
        HourlyCapacityWriteModel item,
        CancellationToken cancellationToken = default)
    {
        LastUpsert = item;
        return Task.CompletedTask;
    }
}

internal sealed class StubDeviceIdentityQueryService : IDeviceIdentityQueryService
{
    public bool Exists { get; set; }

    public DeviceIdentitySnapshot? Snapshot { get; set; }

    public Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Snapshot);
    }

    public Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Exists);
    }
}

internal sealed class StubProcessReadQueryService : IProcessReadQueryService
{
    public bool Exists { get; set; }

    public bool CodeExists { get; set; }

    public IReadOnlyList<Guid> DeviceIds { get; set; } = [];

    public bool HasDevices { get; set; }

    public bool HasRecipes { get; set; }

    public Task<bool> ExistsAsync(Guid processId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Exists);
    }

    public Task<bool> CodeExistsAsync(
        string processCode,
        Guid? excludingProcessId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CodeExists);
    }

    public Task<IReadOnlyList<Guid>> GetDeviceIdsAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeviceIds);
    }

    public Task<bool> HasDevicesAsync(Guid processId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HasDevices);
    }

    public Task<bool> HasRecipesAsync(Guid processId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HasRecipes);
    }
}

internal sealed class StubDeviceReadQueryService : IDeviceReadQueryService
{
    public bool Exists { get; set; }

    public bool ExistsInProcess { get; set; }

    public bool CodeExists { get; set; }

    public Task<bool> ExistsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Exists);
    }

    public Task<bool> ExistsInProcessAsync(
        Guid deviceId,
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ExistsInProcess);
    }

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludingDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CodeExists);
    }
}

internal sealed class StubCapacityQueryService : ICapacityQueryService
{
    public List<HourlyCapacityDto> HourlyResult { get; set; } = [];

    public int HourlyCalls { get; private set; }

    public Task<List<HourlyCapacityDto>> GetHourlyByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        HourlyCalls++;
        return Task.FromResult(HourlyResult);
    }

    public Task<DailySummaryDto?> GetSummaryByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<(List<DailyCapacityPagedItemDto> Items, int TotalCount)> GetDailyPagedAsync(
        Pagination pagination,
        DateOnly? date = null,
        Guid? deviceId = null,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class StubRecipeReadQueryService : IRecipeReadQueryService
{
    public bool VersionExists { get; set; }

    public Task<bool> VersionExistsAsync(
        Guid processId,
        Guid deviceId,
        string recipeName,
        string version,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(VersionExists);
    }
}

internal sealed class RecordingIdentityAccountStore : IIdentityAccountStore
{
    public Result<IdentityAccount> CreateResult { get; set; } = Result.Success(IdentityAccount.Create(Guid.NewGuid(), "E000"));

    public IdentityAccount? AccountById { get; set; }

    public IdentityAccount? AccountByEmployeeNo { get; set; }

    public Dictionary<Guid, IList<string>> RolesByUserId { get; } = [];

    public List<IdentityAccount> CreatedAccounts { get; } = [];

    public List<Guid> DeletedIds { get; } = [];

    public List<(Guid UserId, string RoleName)> AssignedRoles { get; } = [];

    public Guid? LastSetEnabledId { get; private set; }

    public bool LastSetEnabledValue { get; private set; }

    public Result<bool> SetEnabledResult { get; set; } = Result.Success(true);

    public Result<bool> DeleteResult { get; set; } = Result.Success(true);

    public Result<bool> AssignRoleResult { get; set; } = Result.Success(true);

    public Task<Result<IdentityAccount>> CreateAsync(
        IdentityAccount account,
        CancellationToken cancellationToken = default)
    {
        if (CreateResult.IsSuccess)
        {
            CreatedAccounts.Add(account);
            AccountById = account;
            AccountByEmployeeNo = account;
        }

        return Task.FromResult(
            CreateResult.IsSuccess
                ? Result.Success(account)
                : Result.Failure(CreateResult.Errors?.ToArray() ?? ["create failed"]));
    }

    public Task<IdentityAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AccountById?.Id == id ? AccountById : null);
    }

    public Task<IdentityAccount?> GetByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            string.Equals(AccountByEmployeeNo?.EmployeeNo, employeeNo, StringComparison.Ordinal)
                ? AccountByEmployeeNo
                : null);
    }

    public Task<Result<bool>> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        LastSetEnabledId = id;
        LastSetEnabledValue = isEnabled;
        return Task.FromResult(SetEnabledResult);
    }

    public Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (DeleteResult.IsSuccess)
        {
            DeletedIds.Add(id);
            if (AccountById?.Id == id)
            {
                AccountById = null;
            }

            if (AccountByEmployeeNo?.Id == id)
            {
                AccountByEmployeeNo = null;
            }
        }

        return Task.FromResult(DeleteResult);
    }

    public Task<Result<bool>> AssignRoleAsync(
        Guid id,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (AssignRoleResult.IsSuccess)
        {
            AssignedRoles.Add((id, roleName));
            if (!RolesByUserId.TryGetValue(id, out var roles))
            {
                roles = [];
                RolesByUserId[id] = roles;
            }

            roles.Add(roleName);
        }

        return Task.FromResult(AssignRoleResult);
    }

    public Task<IList<string>> GetRolesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            RolesByUserId.TryGetValue(id, out var roles)
                ? roles
                : []);
    }
}

internal sealed class RecordingAuditTrailService : IAuditTrailService
{
    public List<AuditTrailEntry> Entries { get; } = [];

    public Task TryWriteAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingDeviceCacheInvalidationService : IDeviceCacheInvalidationService
{
    public List<Guid> RegisteredProcessIds { get; } = [];

    public List<DeviceCacheDescriptor> DeletedDevices { get; } = [];

    public Task InvalidateListsAfterRegisterAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        RegisteredProcessIds.Add(processId);
        return Task.CompletedTask;
    }

    public Task InvalidateAfterDeleteAsync(
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default)
    {
        DeletedDevices.Add(device);
        return Task.CompletedTask;
    }
}

internal sealed class StubIdentityPasswordService : IIdentityPasswordService
{
    public Result<bool> SetPasswordResult { get; set; } = Result.Success(true);

    public Guid? LastChangedUserId { get; private set; }

    public string? LastCurrentPassword { get; private set; }

    public string? LastNewPassword { get; private set; }

    public Result ChangePasswordResult { get; set; } = Result.Success();

    public Task<Result<bool>> CheckPasswordAsync(
        Guid accountId,
        string password,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<bool>> SetPasswordAsync(
        Guid accountId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SetPasswordResult);
    }

    public Task<Result> ChangePasswordAsync(
        Guid accountId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        LastChangedUserId = accountId;
        LastCurrentPassword = currentPassword;
        LastNewPassword = newPassword;
        return Task.FromResult(ChangePasswordResult);
    }

    public Task<Result<bool>> ResetPasswordAsync(
        Guid accountId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class StubRefreshTokenService : IRefreshTokenService
{
    public List<(string ActorType, Guid SubjectId, string Reason)> Revocations { get; } = [];

    public List<(string ActorType, Guid SubjectId)> Issues { get; } = [];

    public string? LastRotateActorType { get; private set; }

    public string? LastRotatedRefreshToken { get; private set; }

    public Guid NextRotateSubjectId { get; set; } = Guid.NewGuid();

    public Result<RefreshTokenRotationResult>? RotateResultOverride { get; set; }

    public Task<RefreshTokenEnvelope> IssueAsync(
        string actorType,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        Issues.Add((actorType, subjectId));
        return Task.FromResult(new RefreshTokenEnvelope(
            $"refresh-{actorType}-{subjectId:N}",
            DateTimeOffset.UtcNow.AddDays(
                string.Equals(actorType, IIoTClaimTypes.EdgeDeviceActor, StringComparison.Ordinal)
                    ? 30
                    : 7)));
    }

    public Task<Result<RefreshTokenRotationResult>> RotateAsync(
        string actorType,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        LastRotateActorType = actorType;
        LastRotatedRefreshToken = refreshToken;

        if (RotateResultOverride is not null)
        {
            return Task.FromResult(RotateResultOverride);
        }

        return Task.FromResult(Result.Success(new RefreshTokenRotationResult(
            actorType,
            NextRotateSubjectId,
            new RefreshTokenEnvelope($"rotated-{refreshToken}", DateTimeOffset.UtcNow.AddDays(7)))));
    }

    public Task RevokeSubjectTokensAsync(
        string actorType,
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        Revocations.Add((actorType, subjectId, reason));
        return Task.CompletedTask;
    }
}

internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int BeginCalls { get; private set; }

    public int CommitCalls { get; private set; }

    public int RollbackCalls { get; private set; }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        BeginCalls++;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        CommitCalls++;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RollbackCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public string? Id { get; init; }

    public string? UserName { get; init; }

    public string? Role { get; init; }

    public Guid? DeviceId { get; init; }

    public bool IsAuthenticated { get; init; }
}

internal sealed class StubDevicePermissionService : IDevicePermissionService
{
    public IReadOnlyList<Guid>? AccessibleDeviceIds { get; set; }

    public Task<IReadOnlyList<Guid>?> GetAccessibleDeviceIdsAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AccessibleDeviceIds);
    }
}

internal sealed class StubRolePolicyService : IRolePolicyService
{
    public IList<string> Roles { get; set; } = [];

    public bool RoleExists { get; set; }

    public Result CreateRoleResult { get; set; } = Result.Success();

    public Result DeleteRoleResult { get; set; } = Result.Success();

    public Result<bool> UpdateRolePermissionsResult { get; set; } = Result.Success(true);

    public Result<bool> UpdateUserPersonalPermissionsResult { get; set; } = Result.Success(true);

    public string? DeletedRoleName { get; private set; }

    public Task<IList<string>> GetAllRolesAsync()
    {
        return Task.FromResult(Roles);
    }

    public Task<bool> RoleExistsAsync(string roleName)
    {
        return Task.FromResult(RoleExists);
    }

    public Task<Result> CreateRoleAsync(string roleName)
    {
        return Task.FromResult(CreateRoleResult);
    }

    public Task<Result> DeleteRoleAsync(string roleName)
    {
        DeletedRoleName = roleName;
        return Task.FromResult(DeleteRoleResult);
    }

    public Task<Result> RemoveRoleFromUserAsync(string employeeNo, string roleName)
    {
        return Task.FromResult(Result.Success());
    }

    public Task<List<string>?> GetRolePermissionsAsync(string roleName)
    {
        return Task.FromResult<List<string>?>([]);
    }

    public Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions)
    {
        return Task.FromResult(UpdateRolePermissionsResult);
    }

    public Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions)
    {
        return Task.FromResult(UpdateUserPersonalPermissionsResult);
    }

    public Task<List<string>> GetUserPersonalPermissionsAsync(Guid userId)
    {
        return Task.FromResult<List<string>>([]);
    }
}

internal sealed class RecordingEventPublisher(
    List<string>? callOrder = null,
    Exception? publishException = null) : IEventPublisher
{
    public object? LastPublishedEvent { get; private set; }

    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        callOrder?.Add("publish");
        if (publishException is not null)
        {
            throw publishException;
        }

        LastPublishedEvent = @event;
        return Task.CompletedTask;
    }
}

internal sealed class StubDeviceDeletionDependencyQueryService : IDeviceDeletionDependencyQueryService
{
    public DeviceDeletionDependencies Dependencies { get; set; } = new(false, false, false, false);

    public Task<DeviceDeletionDependencies> GetDependenciesAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Dependencies);
    }
}

internal sealed class StubJwtTokenGenerator : IJwtTokenGenerator
{
    public JwtTokenResult GenerateHumanToken(
        Guid userId,
        string userName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        return new JwtTokenResult($"human-{userId:N}", DateTimeOffset.UtcNow.AddMinutes(60));
    }

    public JwtTokenResult GenerateEdgeDeviceToken(
        Guid deviceId,
        string clientCode,
        Guid processId)
    {
        return new JwtTokenResult($"edge-{deviceId:N}", DateTimeOffset.UtcNow.AddMinutes(60));
    }
}
