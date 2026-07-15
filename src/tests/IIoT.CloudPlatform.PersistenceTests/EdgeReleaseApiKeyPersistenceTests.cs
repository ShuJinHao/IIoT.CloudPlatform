using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.IdentityService.Commands;
using IIoT.Infrastructure.Authentication;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EdgeReleaseApiKeyPersistenceTests
{
    [Fact]
    public async Task EdgeReleaseApiKeyService_ShouldStoreHashOnlyAndValidateActiveKey()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);

        var created = await service.CreateAsync(
            "edge-deploy",
            null,
            DateTimeOffset.UtcNow.AddDays(30),
            Guid.NewGuid());

        Assert.True(created.IsSuccess);
        Assert.StartsWith("iiot_edge_release_", created.Value!.ApiKey, StringComparison.Ordinal);

        var row = await dbContext.EdgeReleaseApiKeys.SingleAsync();
        Assert.NotEqual(created.Value.ApiKey, row.KeyHash);
        Assert.Equal(64, row.KeyHash.Length);
        Assert.DoesNotContain(created.Value.ApiKey, row.PermissionsJson, StringComparison.Ordinal);

        var validation = await service.ValidateAsync(created.Value.ApiKey);

        Assert.True(validation.IsSuccess);
        Assert.Equal(created.Value.Id, validation.Value!.Id);
        Assert.Equal("edge-deploy", validation.Value.Name);
        Assert.Contains(ClientReleasePermissions.Read, validation.Value.Permissions);
        Assert.Contains(ClientReleasePermissions.Publish, validation.Value.Permissions);
        Assert.NotNull((await dbContext.EdgeReleaseApiKeys.SingleAsync()).LastUsedAtUtc);
    }

    [Fact]
    public async Task EdgeReleaseApiKeyService_ShouldRejectRevokedKey()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);

        var created = await service.CreateAsync(
            "edge-deploy",
            [ClientReleasePermissions.Read, ClientReleasePermissions.Publish],
            DateTimeOffset.UtcNow.AddDays(30),
            Guid.NewGuid());
        Assert.True(created.IsSuccess);

        var revoked = await service.RevokeAsync(created.Value!.Id, Guid.NewGuid(), "rotation");
        var validation = await service.ValidateAsync(created.Value.ApiKey);

        Assert.True(revoked.IsSuccess);
        Assert.False(validation.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, validation.Status);
    }

    [Fact]
    public async Task EdgeReleaseApiKeyHandlers_ShouldAuditCreateAndRevokeAfterCommit()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);
        var auditTrail = new RecordingAuditTrailService();
        var actorUserId = Guid.NewGuid();
        var currentUser = new TestCurrentUser
        {
            Id = actorUserId.ToString(),
            UserName = "release-admin",
            IsAuthenticated = true
        };

        var createHandler = new CreateEdgeReleaseApiKeyHandler(service, currentUser, auditTrail);
        var created = await createHandler.Handle(
            new CreateEdgeReleaseApiKeyCommand("edge-deploy", null, DateTimeOffset.UtcNow.AddDays(30)),
            CancellationToken.None);
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors ?? []));

        var revokeHandler = new RevokeEdgeReleaseApiKeyHandler(service, currentUser, auditTrail);
        var revoked = await revokeHandler.Handle(
            new RevokeEdgeReleaseApiKeyCommand(created.Value!.Id, "rotation"),
            CancellationToken.None);
        Assert.True(revoked.IsSuccess, string.Join("; ", revoked.Errors ?? []));

        Assert.Equal(2, auditTrail.Entries.Count);
        Assert.Contains(auditTrail.Entries, entry =>
            entry.ActorUserId == actorUserId
            && entry.ActorEmployeeNo == "release-admin"
            && entry.OperationType == "ClientRelease.ApiKey.Create"
            && entry.TargetType == "EdgeReleaseApiKey"
            && entry.TargetIdOrKey == created.Value.Id.ToString()
            && entry.Succeeded);
        Assert.Contains(auditTrail.Entries, entry =>
            entry.ActorUserId == actorUserId
            && entry.OperationType == "ClientRelease.ApiKey.Revoke"
            && entry.TargetIdOrKey == created.Value.Id.ToString()
            && entry.Succeeded);
    }
}
