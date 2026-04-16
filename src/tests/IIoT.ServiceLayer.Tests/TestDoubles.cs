using System.Linq.Expressions;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Contracts.Identity;
using IIoT.Services.Common.Contracts.Persistence;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Domain;
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

internal sealed class RecordingCacheService : ICacheService
{
    public List<string> RemovedKeys { get; } = [];
    public List<string> RemovedPatterns { get; } = [];
    public string? LastSetKey { get; private set; }
    public TimeSpan? LastAbsoluteExpireTime { get; private set; }
    public Dictionary<string, object?> Values { get; } = [];

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (Values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return Task.FromResult<T?>(typedValue);
        }

        return Task.FromResult(default(T));
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
        RemovedKeys.Add(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
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
    public Guid? LastSetEnabledId { get; private set; }

    public bool LastSetEnabledValue { get; private set; }

    public Result<bool> SetEnabledResult { get; set; } = Result.Success(true);

    public Task<Result<IdentityAccount>> CreateAsync(
        IdentityAccount account,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(account));
    }

    public Task<IdentityAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IdentityAccount?>(null);
    }

    public Task<IdentityAccount?> GetByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IdentityAccount?>(null);
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
        return Task.FromResult(Result.Success(true));
    }

    public Task<Result<bool>> AssignRoleAsync(
        Guid id,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(true));
    }

    public Task<IList<string>> GetRolesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IList<string>>([]);
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
