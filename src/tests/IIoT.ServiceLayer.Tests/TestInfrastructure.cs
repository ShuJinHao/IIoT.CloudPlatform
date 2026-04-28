using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;

namespace IIoT.ServiceLayer.Tests;

internal static class TestServiceProviders
{
    public static ServiceProvider CreateEfServiceProvider(IMediator mediator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mediator);
        services.AddSingleton<IMediator>(mediator);
        services.AddDbContext<IIoTDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        return services.BuildServiceProvider();
    }

    public static ServiceProvider CreateIdentityServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IIoTDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();

        return services.BuildServiceProvider();
    }
}

internal sealed class RecordingMediator : IMediator
{
    public List<object> PublishedNotifications { get; } = [];

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
        PublishedNotifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        return Publish((object)notification!, cancellationToken);
    }
}

internal sealed class NoopMediator : IMediator
{
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
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingMediator(string message) : IMediator
{
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
        throw new InvalidOperationException(message);
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        return Publish((object)notification!, cancellationToken);
    }
}

[DistributedLock("iiot:lock:missing:{MissingProperty}")]
internal sealed record BrokenLockCommand(Guid DeviceId) : ICommand<Result<bool>>;

internal sealed record DeviceScopedCommand(Guid DeviceId) : IDeviceCommand<Result<bool>>;

internal sealed class NoopDistributedLockService : IDistributedLockService
{
    public Task<IAsyncDisposable> AcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IAsyncDisposable>(new NoopAsyncDisposable());
    }
}

internal sealed class NoopAsyncDisposable : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
