using System.Reflection;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.Services.Common.Caching.Options;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class ReviewRegressionTests
{
    [Fact]
    public void Recipe_ShouldValidateInputsBeforeTrimming()
    {
        var processId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        Assert.ThrowsAny<ArgumentException>(() => new Recipe(null!, processId, deviceId, "{\"speed\":120}"));
        Assert.ThrowsAny<ArgumentException>(() => new Recipe("  ", processId, deviceId, "{\"speed\":120}"));
        Assert.ThrowsAny<ArgumentException>(() => new Recipe("Recipe", processId, deviceId, null!));
        Assert.ThrowsAny<ArgumentException>(() => new Recipe("Recipe", processId, deviceId, "   "));
    }

    [Fact]
    public void Recipe_CreateNextVersion_ShouldReuseValidation()
    {
        var recipe = new Recipe("Recipe", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");

        Assert.ThrowsAny<ArgumentException>(() => recipe.CreateNextVersion(null!, "{\"speed\":140}"));
        Assert.ThrowsAny<ArgumentException>(() => recipe.CreateNextVersion("V1.1", null!));
    }

    [Fact]
    public async Task EfUnitOfWork_ShouldRetainAndRetryPendingDomainEventsAfterPostCommitFailure()
    {
        using var provider = CreateServiceProvider(new ThrowOnceMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var mediator = (ThrowOnceMediator)scope.ServiceProvider.GetRequiredService<IMediator>();
        var unitOfWork = new EfUnitOfWork(dbContext, CreateLogger<EfUnitOfWork>());

        AddPendingDomainEvent(dbContext, new TestDomainEvent(Guid.NewGuid()));
        SetTransaction(unitOfWork, new FakeDbContextTransaction());

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync());

        Assert.True(dbContext.HasPendingDomainEvents);
        Assert.Equal(1, mediator.PublishAttempts);

        await unitOfWork.RollbackAsync();

        Assert.True(dbContext.HasPendingDomainEvents);

        await unitOfWork.CommitAsync();

        Assert.False(dbContext.HasPendingDomainEvents);
        Assert.Equal(2, mediator.PublishAttempts);
    }

    [Fact]
    public async Task EfUnitOfWork_BeginTransaction_ShouldRejectWhenPendingCommittedEventsExist()
    {
        using var provider = CreateServiceProvider(new ThrowOnceMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var unitOfWork = new EfUnitOfWork(dbContext, CreateLogger<EfUnitOfWork>());

        AddPendingDomainEvent(dbContext, new TestDomainEvent(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.BeginTransactionAsync());

        Assert.Contains("pending dispatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DevicePermissionService_ShouldUseResolvedCacheExpiration()
    {
        using var provider = CreateServiceProvider(new ThrowOnceMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = new Employee(Guid.NewGuid(), "E1001", "Operator");
        employee.AddDeviceAccess(Guid.NewGuid());
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var cacheService = new RecordingCacheService();
        var service = new DevicePermissionService(
            dbContext,
            cacheService,
            Options.Create(new PermissionCacheOptions
            {
                ExpirationMinutes = 10,
                ExpirationHours = 2
            }));

        var accessibleDeviceIds = await service.GetAccessibleDeviceIdsAsync(employee.Id, isAdmin: false);

        Assert.Single(accessibleDeviceIds!);
        Assert.Equal(TimeSpan.FromMinutes(10), cacheService.LastAbsoluteExpireTime);
    }

    private static ServiceProvider CreateServiceProvider(IMediator mediator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mediator);
        services.AddSingleton<IMediator>(mediator);
        services.AddDbContext<IIoTDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        return services.BuildServiceProvider();
    }

    private static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.Create(_ => { }).CreateLogger<T>();
    }

    private static void AddPendingDomainEvent(IIoTDbContext dbContext, IDomainEvent domainEvent)
    {
        var field = typeof(IIoTDbContext).GetField("_pendingDomainEvents", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Unable to access pending domain events field.");

        var pendingDomainEvents = (List<IDomainEvent>)(field.GetValue(dbContext)
            ?? throw new InvalidOperationException("Pending domain events list was not initialized."));

        pendingDomainEvents.Add(domainEvent);
    }

    private static void SetTransaction(EfUnitOfWork unitOfWork, IDbContextTransaction transaction)
    {
        var field = typeof(EfUnitOfWork).GetField("_transaction", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Unable to access unit of work transaction field.");

        field.SetValue(unitOfWork, transaction);
    }

    private sealed record TestDomainEvent(Guid EntityId) : IDomainEvent;

    private sealed class ThrowOnceMediator : IMediator
    {
        public int PublishAttempts { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            PublishAttempts++;
            if (PublishAttempts == 1)
            {
                throw new InvalidOperationException("Simulated publish failure.");
            }

            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            return Publish((object)notification, cancellationToken);
        }
    }

    private sealed class FakeDbContextTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public void Commit()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Rollback()
        {
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void CreateSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void RollbackToSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void ReleaseSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public bool SupportsSavepoints => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCacheService : ICacheService
    {
        public TimeSpan? LastAbsoluteExpireTime { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(default(T));
        }

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? absoluteExpireTime = null,
            CancellationToken cancellationToken = default)
        {
            LastAbsoluteExpireTime = absoluteExpireTime;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
