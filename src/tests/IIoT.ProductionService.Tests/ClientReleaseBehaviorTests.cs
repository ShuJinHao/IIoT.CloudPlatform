using System.Linq.Expressions;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Specification;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class ClientReleaseBehaviorTests
{
    [Fact]
    public async Task UpsertClientHostReleaseHandler_ShouldCreateReleaseRecord()
    {
        var repository = new InMemoryRepository<ClientHostRelease>();
        var handler = new UpsertClientHostReleaseHandler(repository);

        var result = await handler.Handle(
            new UpsertClientHostReleaseCommand(
                "stable",
                "1.2.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "https://example.test/releases/host.zip",
                new string('a', 64),
                1024,
                "release notes",
                "Published",
                null,
                "IIoT"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.AddedEntity);
        Assert.Equal("1.2.0", repository.AddedEntity!.Version);
        Assert.Equal(ClientReleaseStatus.Published, repository.AddedEntity.Status);
        Assert.NotNull(repository.AddedEntity.PublishedAtUtc);
    }

    [Fact]
    public async Task ReportDeviceClientVersionHandler_ShouldRejectMismatchedClientCode()
    {
        var deviceId = Guid.NewGuid();
        var repository = new InMemoryRepository<DeviceClientVersionSnapshot>();
        var handler = new ReportDeviceClientVersionHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            repository);

        var result = await handler.Handle(
            new ReportDeviceClientVersionCommand(
                deviceId,
                "DEV-OTHER",
                "1.2.0",
                "1.0.0",
                [],
                [],
                "stable",
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
    }

    [Fact]
    public async Task ReportDeviceClientVersionHandler_ShouldStoreLatestPluginSnapshot()
    {
        var deviceId = Guid.NewGuid();
        var repository = new InMemoryRepository<DeviceClientVersionSnapshot>();
        var handler = new ReportDeviceClientVersionHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            repository);

        var result = await handler.Handle(
            new ReportDeviceClientVersionCommand(
                deviceId,
                "DEV-001",
                "1.2.0",
                "1.0.0",
                [new DeviceClientPluginVersionReportItem("Homogenization", "匀浆", "2.0.0", "1.0.0")],
                ["Homogenization"],
                "stable",
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<DeviceClientVersionSnapshot>(repository.AddedEntity);
        Assert.Equal(deviceId, snapshot.DeviceId);
        Assert.Equal("DEV-001", snapshot.ClientCode);
        var plugin = Assert.Single(snapshot.InstalledPlugins);
        Assert.Equal("Homogenization", plugin.ModuleId);
        Assert.True(plugin.Enabled);
    }

    private sealed class StubDeviceIdentityQueryService(DeviceIdentitySnapshot? snapshot) : IDeviceIdentityQueryService
    {
        public Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot?.DeviceId == deviceId ? snapshot : null);
        }

        public Task<bool> ExistsAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot?.DeviceId == deviceId);
        }
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> Items { get; } = [];

        public T? AddedEntity { get; private set; }

        public T Add(T entity)
        {
            AddedEntity = entity;
            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            Items.Remove(entity);
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
            return Task.FromResult(ApplySpecification(specification).SingleOrDefault());
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
            return Task.FromResult(Items.AsQueryable().Any(predicate));
        }

        public Task<int> CountAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Count(predicate));
        }

        private IEnumerable<T> ApplySpecification(ISpecification<T>? specification)
        {
            IEnumerable<T> query = Items;

            if (specification?.FilterCondition is not null)
            {
                query = query.Where(specification.FilterCondition.Compile());
            }

            return query;
        }
    }
}
