using System.Security.Claims;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.ProductionService.AiRead;
using IIoT.ProductionService.Queries.AiRead;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.AiRead;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class AiReadBehaviorTests
{
    [Fact]
    public async Task RequestKindGuard_ShouldAllowAiReadRequestWithAiReadAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<ValidAiReadQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var result = await behavior.Handle(
            new ValidAiReadQuery(),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectAiReadRequestWithHumanAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<AiReadWithHumanAuthorizationQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new AiReadWithHumanAuthorizationQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeRequirementAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectHumanRequestWithAiReadAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<HumanWithAiReadAuthorizationQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new HumanWithAiReadAuthorizationQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeAiReadAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectAiReadRequestWithAdminOnly()
    {
        var behavior = new RequestKindGuardBehavior<AiReadWithAdminOnlyQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new AiReadWithAdminOnlyQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AdminOnlyAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectAiReadRequestWithoutAiReadAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<UnprotectedAiReadQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new UnprotectedAiReadQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeAiReadAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldAllowPublicRequestWithoutAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<PublicDownloadQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var result = await behavior.Handle(
            new PublicDownloadQuery(),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectPublicRequestWithHumanAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<PublicWithHumanAuthorizationQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new PublicWithHumanAuthorizationQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeRequirementAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiReadAuthorization_ShouldAllowAiServiceAccountWithRequiredPermission()
    {
        var behavior = new AiReadAuthorizationBehavior<ValidAiReadQuery, Result<bool>>(
            CreateAccessor(IIoTClaimTypes.AiServiceActor, [AiReadPermissions.Device]));

        var result = await behavior.Handle(
            new ValidAiReadQuery(),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AiReadAuthorization_ShouldRejectHumanActorEvenWithAiReadPermission()
    {
        var behavior = new AiReadAuthorizationBehavior<ValidAiReadQuery, Result<bool>>(
            CreateAccessor(IIoTClaimTypes.HumanActor, [AiReadPermissions.Device]));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new ValidAiReadQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));
    }

    [Fact]
    public async Task AiReadAuthorization_ShouldRejectMissingAiReadPermission()
    {
        var behavior = new AiReadAuthorizationBehavior<ValidAiReadQuery, Result<bool>>(
            CreateAccessor(IIoTClaimTypes.AiServiceActor, []));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new ValidAiReadQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));
    }

    [Fact]
    public async Task AiReadAudit_ShouldWriteMetadataWithoutPromptPayload()
    {
        var auditTrail = new RecordingAuditTrailService();
        var behavior = new AiReadAuditBehavior<AuditedAiReadQuery, Result<AiReadListResponse<int>>>(
            CreateAccessor(IIoTClaimTypes.AiServiceActor, [AiReadPermissions.Device]),
            auditTrail);

        var response = new AiReadListResponse<int>(
            [1, 2],
            DateTimeOffset.UtcNow,
            "devices",
            "deviceId=abc;keyword=present",
            2,
            Truncated: true);

        await behavior.Handle(
            new AuditedAiReadQuery(),
            _ => Task.FromResult(Result.Success(response)),
            CancellationToken.None);

        var entry = Assert.Single(auditTrail.Entries);
        Assert.Equal("AiRead.Query", entry.OperationType);
        Assert.Contains("source=devices", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("rowCount=2", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("truncated=True", entry.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", entry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiReadDeviceLogs_ShouldRejectMissingTimeRange()
    {
        var handler = new GetAiReadDeviceLogsHandler(
            new StubDeviceLogQueryService(),
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDeviceLogsQuery(Guid.NewGuid(), null, null, Keyword: "error"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AiReadCapacitySummary_ShouldMarkTruncatedWhenRowsExceedMaxRows()
    {
        var deviceId = Guid.NewGuid();
        var handler = new GetAiReadCapacitySummaryHandler(
            new StubCapacityQueryService
            {
                SummaryRangeResult =
                [
                    new DailyRangeSummaryDto(DateOnly.FromDateTime(DateTime.UtcNow), 10, 9, 1, 10, 9, 1, 0, 0, 0),
                    new DailyRangeSummaryDto(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), 20, 19, 1, 20, 19, 1, 0, 0, 0)
                ]
            },
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions { MaxRows = 1 }));

        var result = await handler.Handle(
            new GetAiReadCapacitySummaryQuery(
                deviceId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Truncated);
        Assert.Equal(1, result.Value.RowCount);
    }

    [Fact]
    public async Task AiReadDevices_ShouldReturnDeviceCode()
    {
        var processId = Guid.NewGuid();
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("Injection Device", "DEV-AIREAD-001", processId));
        var handler = new GetAiReadDevicesHandler(
            repository,
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDevicesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("DEV-AIREAD-001", item.DeviceCode);
        Assert.Equal("Injection Device", item.DeviceName);
        Assert.Equal(processId, item.ProcessId);
    }

    private static HttpContextAccessor CreateAccessor(string actorType, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "ai-read-test"),
            new(IIoTClaimTypes.ActorType, actorType)
        };
        claims.AddRange(permissions.Select(permission => new Claim(IIoTClaimTypes.Permission, permission)));

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
    }

    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record ValidAiReadQuery() : IAiReadQuery<Result<bool>>;

    [AuthorizeRequirement("Device.Read")]
    private sealed record AiReadWithHumanAuthorizationQuery() : IAiReadQuery<Result<bool>>;

    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record HumanWithAiReadAuthorizationQuery() : IHumanQuery<Result<bool>>;

    [AdminOnly]
    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record AiReadWithAdminOnlyQuery() : IAiReadQuery<Result<bool>>;

    private sealed record UnprotectedAiReadQuery() : IAiReadQuery<Result<bool>>;

    private sealed record PublicDownloadQuery() : IPublicQuery<Result<bool>>;

    [AuthorizeRequirement("Device.Read")]
    private sealed record PublicWithHumanAuthorizationQuery() : IPublicQuery<Result<bool>>;

    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record AuditedAiReadQuery() : IAiReadQuery<Result<AiReadListResponse<int>>>;
}
