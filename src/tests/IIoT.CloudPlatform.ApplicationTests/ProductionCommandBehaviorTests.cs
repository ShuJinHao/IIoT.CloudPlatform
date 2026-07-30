using System.Linq.Expressions;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class ProductionCommandBehaviorTests
{
    private static StubDeviceWriteObservationReader ObserveDevice(
        Device device,
        bool duplicateName = false)
        => new()
        {
            ObservationFactory = (_, deviceName, _, _) =>
                new DeviceWriteObservation(
                    new DeviceWriteState(
                        device.Id,
                        device.DeviceName,
                        device.Code,
                        device.ProcessId,
                        device.RowVersion),
                    duplicateName
                        ? Guid.NewGuid()
                        : string.Equals(
                            device.DeviceName,
                            deviceName,
                            StringComparison.Ordinal)
                            ? device.Id
                            : null,
                    device.Id,
                    true,
                    new DeviceDeletionImpact(
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
        };

    private static StubRecipeWriteObservationReader ObserveRecipe(
        InMemoryRepository<Recipe> repository)
        => new()
        {
            ObservationFactory = (
                recipeId,
                processId,
                deviceId,
                recipeName) =>
            {
                var recipes = repository.ListResult.Count > 0
                    ? repository.ListResult
                    : repository.SingleOrDefaultResult is null
                        ? []
                        : [repository.SingleOrDefaultResult];
                var family = recipes
                    .Where(recipe =>
                        recipe.ProcessId == processId
                        && recipe.DeviceId == deviceId
                        && recipe.RecipeName == recipeName)
                    .Select(recipe => new RecipeWriteState(
                        recipe.Id,
                        recipe.RecipeName,
                        recipe.Version,
                        recipe.ProcessId,
                        recipe.DeviceId,
                        recipe.ParametersJsonb,
                        (int)recipe.Status,
                        recipe.RowVersion))
                    .OrderBy(recipe => recipe.Id)
                    .ToArray();
                return new RecipeWriteObservation(
                    family.SingleOrDefault(
                        recipe => recipe.Id == recipeId),
                    family,
                    true,
                    true);
            }
        };

    [Fact]
    public async Task UpdateDeviceProfileHandler_ShouldRenameAuthorizedDevice()
    {
        var device = new Device("Device-01", "DEV-PRODTEST01", Guid.NewGuid());
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var handler = new UpdateDeviceProfileHandler(
            repository,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            new RecordingUnitOfWork(),
            ObserveDevice(device));

        var result = await handler.Handle(
            new UpdateDeviceProfileCommand(device.Id, " Device-02 "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Device-02", device.DeviceName);
        Assert.Contains(device, repository.UpdatedEntities);
    }

    [Fact]
    public async Task UpdateDeviceProfileHandler_ShouldRejectDeviceOutsideCurrentUserScope()
    {
        var device = new Device("Device-01", "DEV-PRODTEST02", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var handler = new UpdateDeviceProfileHandler(
            repository,
            new StubCurrentUserDeviceAccessService { FailureMessage = "forbidden" },
            new RecordingUnitOfWork(),
            ObserveDevice(device));

        var result = await handler.Handle(
            new UpdateDeviceProfileCommand(device.Id, "Device-02"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Device-01", device.DeviceName);
        Assert.Empty(repository.UpdatedEntities);
    }

    [Fact]
    public async Task UpdateDeviceProfileHandler_ShouldRejectDuplicateDeviceName()
    {
        var device = new Device("Device-01", "DEV-PRODTEST03", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var handler = new UpdateDeviceProfileHandler(
            repository,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            new RecordingUnitOfWork(),
            ObserveDevice(device, duplicateName: true));

        var result = await handler.Handle(
            new UpdateDeviceProfileCommand(device.Id, "Device-02"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Device-01", device.DeviceName);
        Assert.Empty(repository.UpdatedEntities);
    }

    [Fact]
    public async Task UpgradeRecipeVersionHandler_ShouldArchiveActiveVersionAndCreateNextVersion()
    {
        var processId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var source = new Recipe("Recipe-A", processId, deviceId, "{\"speed\":120}");
        source.ClearDomainEvents();
        var repository = new InMemoryRepository<Recipe>
        {
            SingleOrDefaultResult = source
        };
        repository.ListResult.Add(source);
        var handler = new UpgradeRecipeVersionHandler(
            repository,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            new RecordingUnitOfWork(),
            ObserveRecipe(repository));

        var result = await handler.Handle(
            new UpgradeRecipeVersionCommand(source.Id, "V1.1", "{\"speed\":130}"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecipeStatus.Archived, source.Status);
        Assert.Contains(source, repository.UpdatedEntities);
        Assert.NotNull(repository.AddedEntity);
        Assert.Equal("V1.1", repository.AddedEntity!.Version);
        Assert.Equal(RecipeStatus.Active, repository.AddedEntity.Status);
        Assert.Equal(processId, repository.AddedEntity.ProcessId);
        Assert.Equal(deviceId, repository.AddedEntity.DeviceId);
    }

    [Fact]
    public async Task UpgradeRecipeVersionHandler_ShouldRejectDuplicateVersion()
    {
        var source = new Recipe("Recipe-A", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");
        var repository = new InMemoryRepository<Recipe>
        {
            SingleOrDefaultResult = source
        };
        var handler = new UpgradeRecipeVersionHandler(
            repository,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            new RecordingUnitOfWork(),
            new StubRecipeWriteObservationReader
            {
                ObservationFactory = (
                    recipeId,
                    processId,
                    deviceId,
                    recipeName) => new RecipeWriteObservation(
                    new RecipeWriteState(
                        source.Id,
                        source.RecipeName,
                        source.Version,
                        source.ProcessId,
                        source.DeviceId,
                        source.ParametersJsonb,
                        (int)source.Status,
                        source.RowVersion),
                    [
                        new RecipeWriteState(
                            source.Id,
                            source.RecipeName,
                            source.Version,
                            source.ProcessId,
                            source.DeviceId,
                            source.ParametersJsonb,
                            (int)source.Status,
                            source.RowVersion),
                        new RecipeWriteState(
                            Guid.NewGuid(),
                            recipeName,
                            "V1.1",
                            processId,
                            deviceId,
                            "{}",
                            (int)RecipeStatus.Archived,
                            1)
                    ],
                    true,
                    true)
            });

        var result = await handler.Handle(
            new UpgradeRecipeVersionCommand(source.Id, "V1.1", "{\"speed\":130}"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecipeStatus.Active, source.Status);
        Assert.Null(repository.AddedEntity);
        Assert.Empty(repository.UpdatedEntities);
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public T? SingleOrDefaultResult { get; set; }

        public List<T> ListResult { get; } = [];

        public T? AddedEntity { get; private set; }

        public List<T> UpdatedEntities { get; } = [];

        public T Add(T entity)
        {
            AddedEntity = entity;
            ListResult.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
            UpdatedEntities.Add(entity);
        }

        public void Delete(T entity)
        {
            ListResult.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).ToList());
        }

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SingleOrDefaultResult);
        }

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).Any());
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

        private IEnumerable<T> ApplySpecification(ISpecification<T>? specification)
        {
            IEnumerable<T> query = ListResult;

            if (specification?.FilterCondition is not null)
            {
                query = query.Where(specification.FilterCondition.Compile());
            }

            return query;
        }
    }

    private sealed class StubCurrentUserDeviceAccessService : ICurrentUserDeviceAccessService
    {
        public bool IsAdministrator { get; init; }

        public string? FailureMessage { get; init; }

        public Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success<IReadOnlyList<Guid>?>(IsAdministrator ? null : []));
        }

        public Task<Result> EnsureCanAccessDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                FailureMessage is null
                    ? Result.Success()
                    : Result.Failure(FailureMessage));
        }
    }

    private sealed class StubRecipeReadQueryService : IRecipeReadQueryService
    {
        public bool VersionExists { get; init; }

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

    private sealed class StubDeviceReadQueryService : IDeviceReadQueryService
    {
        public bool NameExists { get; init; }

        public Task<IReadOnlyList<Guid>> GetExistingIdsAsync(
            IReadOnlyCollection<Guid> deviceIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([.. deviceIds]);
        }

        public Task<bool> ExistsAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> ExistsInProcessAsync(
            Guid deviceId,
            Guid processId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludingDeviceId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> NameExistsAsync(
            string name,
            Guid? excludingDeviceId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NameExists);
        }
    }
}
