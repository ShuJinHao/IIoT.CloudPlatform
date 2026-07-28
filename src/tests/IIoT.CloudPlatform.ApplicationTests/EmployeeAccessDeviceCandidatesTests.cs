using IIoT.Core.Production.Aggregates.Devices;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class EmployeeAccessDeviceCandidatesTests
{
    [Fact]
    public void Query_ShouldRequireOnlyEmployeeUpdateAccessWithoutAdminOnlyOrDeviceRead()
    {
        var permissions = typeof(GetEmployeeAccessDeviceCandidatesQuery)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(attribute => attribute.Permission)
            .ToArray();
        var handlerDependencies = typeof(GetEmployeeAccessDeviceCandidatesHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal([CloudPermissionCatalog.Employee.UpdateAccess], permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Device.Read, permissions);
        Assert.Empty(typeof(GetEmployeeAccessDeviceCandidatesQuery)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
        Assert.Equal([typeof(IReadRepository<Device>)], handlerDependencies);
    }

    [Fact]
    public async Task HrAdminWithoutDeviceReadOrAssignedScope_ShouldReceiveEveryMinimalCandidateInStableOrder()
    {
        var firstSameName = new Device("同名设备", "DEV-FIRST", Guid.NewGuid());
        var secondSameName = new Device("同名设备", "DEV-SECOND", Guid.NewGuid());
        var alpha = new Device("Alpha", "DEV-ALPHA", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.AddRange([firstSameName, secondSameName, alpha]);
        var handler = new GetEmployeeAccessDeviceCandidatesHandler(repository);
        var query = new GetEmployeeAccessDeviceCandidatesQuery();
        var userId = Guid.NewGuid();
        var permissionProvider = new RecordingPermissionProvider
        {
            Permissions = [CloudPermissionCatalog.Employee.UpdateAccess]
        };
        var authorization = new AuthorizationBehavior<
            GetEmployeeAccessDeviceCandidatesQuery,
            Result<List<EmployeeAccessDeviceCandidateDto>>>(
            new TestCurrentUser
            {
                Id = userId.ToString(),
                UserName = "hr-admin",
                Roles = [SystemRoles.HrAdmin],
                ActorType = IIoTClaimTypes.HumanActor,
                Permissions = [],
                IsAuthenticated = true
            },
            permissionProvider);
        using var cancellationSource = new CancellationTokenSource();

        var result = await authorization.Handle(
            query,
            cancellationToken => handler.Handle(query, cancellationToken),
            cancellationSource.Token);

        Assert.True(result.IsSuccess);
        var candidates = Assert.IsType<List<EmployeeAccessDeviceCandidateDto>>(result.Value);
        Assert.Equal(userId, permissionProvider.LastUserId);
        Assert.Equal(
            repository.ListResult
                .OrderBy(device => device.DeviceName, StringComparer.Ordinal)
                .ThenBy(device => device.Id)
                .Select(device => device.Id),
            candidates.Select(candidate => candidate.Id));
        Assert.Equal(
            repository.ListResult
                .OrderBy(device => device.DeviceName, StringComparer.Ordinal)
                .ThenBy(device => device.Id)
                .Select(device => device.DeviceName),
            candidates.Select(candidate => candidate.DeviceName));
        Assert.Equal(1, repository.GetListCalls);
        Assert.Null(repository.LastGetListSpecification);
        Assert.Equal(cancellationSource.Token, repository.LastGetListCancellationToken);
    }

    [Fact]
    public async Task HumanAdminWithoutRawPermissionClaims_ShouldUseCanonicalAdminBypass()
    {
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("设备 A", "DEV-A", Guid.NewGuid()));
        var handler = new GetEmployeeAccessDeviceCandidatesHandler(repository);
        var query = new GetEmployeeAccessDeviceCandidatesQuery();
        var permissionProvider = new RecordingPermissionProvider
        {
            Permissions = []
        };
        var authorization = new AuthorizationBehavior<
            GetEmployeeAccessDeviceCandidatesQuery,
            Result<List<EmployeeAccessDeviceCandidateDto>>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-without-raw-permissions",
                Roles = [SystemRoles.Admin],
                ActorType = IIoTClaimTypes.HumanActor,
                Permissions = [],
                IsAuthenticated = true
            },
            permissionProvider);

        var result = await authorization.Handle(
            query,
            cancellationToken => handler.Handle(query, cancellationToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Null(permissionProvider.LastUserId);
        Assert.Equal(1, repository.GetListCalls);
    }

    [Fact]
    public async Task DeviceReadWithoutEmployeeUpdateAccess_ShouldBeRejectedBeforeHandlerAndRepository()
    {
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("设备 A", "DEV-A", Guid.NewGuid()));
        var handler = new GetEmployeeAccessDeviceCandidatesHandler(repository);
        var query = new GetEmployeeAccessDeviceCandidatesQuery();
        var handlerCalled = false;
        var authorization = new AuthorizationBehavior<
            GetEmployeeAccessDeviceCandidatesQuery,
            Result<List<EmployeeAccessDeviceCandidateDto>>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "device-reader",
                Roles = [SystemRoles.ProductionViewer],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new RecordingPermissionProvider
            {
                Permissions = [CloudPermissionCatalog.Device.Read]
            });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            authorization.Handle(
                query,
                cancellationToken =>
                {
                    handlerCalled = true;
                    return handler.Handle(query, cancellationToken);
                },
                CancellationToken.None));

        Assert.False(handlerCalled);
        Assert.Equal(0, repository.GetListCalls);
    }

    [Fact]
    public async Task UnauthenticatedCaller_ShouldBeRejectedBeforeHandlerAndRepository()
    {
        var repository = new InMemoryRepository<Device>();
        var handler = new GetEmployeeAccessDeviceCandidatesHandler(repository);
        var query = new GetEmployeeAccessDeviceCandidatesQuery();
        var handlerCalled = false;
        var permissionProvider = new RecordingPermissionProvider
        {
            Permissions = [CloudPermissionCatalog.Employee.UpdateAccess]
        };
        var authorization = new AuthorizationBehavior<
            GetEmployeeAccessDeviceCandidatesQuery,
            Result<List<EmployeeAccessDeviceCandidateDto>>>(
            new TestCurrentUser
            {
                IsAuthenticated = false
            },
            permissionProvider);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            authorization.Handle(
                query,
                cancellationToken =>
                {
                    handlerCalled = true;
                    return handler.Handle(query, cancellationToken);
                },
                CancellationToken.None));

        Assert.False(handlerCalled);
        Assert.Null(permissionProvider.LastUserId);
        Assert.Equal(0, repository.GetListCalls);
    }

    [Fact]
    public async Task Handler_ShouldReturnSuccessfulEmptyCollection()
    {
        var repository = new InMemoryRepository<Device>();
        var handler = new GetEmployeeAccessDeviceCandidatesHandler(repository);

        var result = await handler.Handle(
            new GetEmployeeAccessDeviceCandidatesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
        Assert.Equal(1, repository.GetListCalls);
    }
}
