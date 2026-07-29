using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.SharedKernel.Result;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class EmployeeDeviceAccessIntegrityTests
{
    [Fact]
    public void Command_ShouldKeepEmployeeUpdateAccessAndEmployeeLockOnly()
    {
        var commandType = typeof(UpdateEmployeeAccessCommand);
        var permission = Assert.Single(
            commandType.GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
                .Cast<AuthorizeRequirementAttribute>());
        var distributedLock = Assert.Single(
            commandType.GetCustomAttributes(typeof(DistributedLockAttribute), true)
                .Cast<DistributedLockAttribute>());

        Assert.Equal(CloudPermissionCatalog.Employee.UpdateAccess, permission.Permission);
        Assert.Equal("iiot:lock:employee:{EmployeeId}", distributedLock.KeyTemplate);
        Assert.Equal(5, distributedLock.TimeoutSeconds);
        Assert.Empty(commandType.GetCustomAttributes(typeof(AdminOnlyAttribute), true));
    }

    [Fact]
    public async Task ValidAssignments_ShouldNormalizeDuplicatesAndReplaceCompleteSet()
    {
        var employeeId = Guid.NewGuid();
        var originalDeviceId = Guid.NewGuid();
        var retainedDeviceId = Guid.NewGuid();
        var addedDeviceId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E-ACCESS-001", "Access User");
        employee.AddDeviceAccess(originalDeviceId);
        employee.AddDeviceAccess(retainedDeviceId);
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var deviceQueries = new StubDeviceReadQueryService
        {
            ExistingDeviceIds = [retainedDeviceId, addedDeviceId]
        };
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            new StubAdminTargetGuard(),
            deviceQueries);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(
                employeeId,
                [retainedDeviceId, addedDeviceId, retainedDeviceId]),
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, deviceQueries.GetExistingIdsCalls);
        Assert.Equal(
            new[] { retainedDeviceId, addedDeviceId }.OrderBy(id => id),
            deviceQueries.LastRequestedDeviceIds.OrderBy(id => id));
        Assert.Equal(
            new[] { retainedDeviceId, addedDeviceId }.OrderBy(id => id),
            employee.DeviceAccesses.Select(access => access.DeviceId).OrderBy(id => id));
        Assert.Same(employee, Assert.Single(repository.UpdatedEntities));
        Assert.Equal(1, repository.SaveChangesCalls);
        Assert.Equal(cancellation.Token, deviceQueries.LastGetExistingIdsCancellationToken);
        Assert.Equal(cancellation.Token, repository.LastSaveChangesCancellationToken);
    }

    [Fact]
    public async Task EmptyAssignments_ShouldClearAccessWithoutQueryingDevices()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E-ACCESS-002", "Clear Access");
        employee.AddDeviceAccess(Guid.NewGuid());
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var deviceQueries = new StubDeviceReadQueryService();
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            new StubAdminTargetGuard(),
            deviceQueries);

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(employeeId, []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(employee.DeviceAccesses);
        Assert.Equal(0, deviceQueries.GetExistingIdsCalls);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task MixedExistingAndMissingAssignments_ShouldRejectWithoutAnyMutation()
    {
        var employeeId = Guid.NewGuid();
        var originalDeviceId = Guid.NewGuid();
        var existingDeviceId = Guid.NewGuid();
        var missingDeviceId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E-ACCESS-003", "Rejected Access");
        employee.AddDeviceAccess(originalDeviceId);
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var deviceQueries = new StubDeviceReadQueryService
        {
            ExistingDeviceIds = [existingDeviceId]
        };
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            new StubAdminTargetGuard(),
            deviceQueries);

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(
                employeeId,
                [existingDeviceId, missingDeviceId]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [EmployeeAccessErrors.SelectedDeviceNoLongerExists],
            result.Errors);
        Assert.Equal([originalDeviceId], employee.DeviceAccesses.Select(access => access.DeviceId));
        Assert.Empty(repository.UpdatedEntities);
        Assert.Equal(0, repository.SaveChangesCalls);
        Assert.Equal(1, deviceQueries.GetExistingIdsCalls);
    }

    [Fact]
    public async Task AdminTarget_ShouldBeRejectedBeforeEmployeeOrDeviceReads()
    {
        var employeeId = Guid.NewGuid();
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = new Employee(employeeId, "E-ACCESS-004", "Protected Admin")
        };
        var targetGuard = new StubAdminTargetGuard
        {
            GuardResult = Result.Failure(AdminTargetProtectionErrors.AdminTargetProtected)
        };
        var deviceQueries = new StubDeviceReadQueryService();
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            targetGuard,
            deviceQueries);

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(employeeId, [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, targetGuard.Calls);
        Assert.Equal(0, repository.GetSingleOrDefaultCalls);
        Assert.Equal(0, deviceQueries.GetExistingIdsCalls);
        Assert.Empty(repository.UpdatedEntities);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task MissingEmployee_ShouldBeRejectedBeforeDeviceRead()
    {
        var repository = new InMemoryRepository<Employee>();
        var deviceQueries = new StubDeviceReadQueryService();
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            new StubAdminTargetGuard(),
            deviceQueries);

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(Guid.NewGuid(), [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, repository.GetSingleOrDefaultCalls);
        Assert.Equal(0, deviceQueries.GetExistingIdsCalls);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task DeviceValidationCancellation_ShouldPropagateWithoutPartialMutation()
    {
        var employeeId = Guid.NewGuid();
        var originalDeviceId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E-ACCESS-005", "Cancelled Access");
        employee.AddDeviceAccess(originalDeviceId);
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        using var cancellation = new CancellationTokenSource();
        var deviceQueries = new StubDeviceReadQueryService
        {
            GetExistingIdsException = new OperationCanceledException(cancellation.Token)
        };
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            new StubAdminTargetGuard(),
            deviceQueries);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.Handle(
                new UpdateEmployeeAccessCommand(employeeId, [Guid.NewGuid()]),
                cancellation.Token));

        Assert.Equal(cancellation.Token, deviceQueries.LastGetExistingIdsCancellationToken);
        Assert.Equal([originalDeviceId], employee.DeviceAccesses.Select(access => access.DeviceId));
        Assert.Empty(repository.UpdatedEntities);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task HrAdminWithoutDeviceRead_ShouldReachHandlerWithEmployeeUpdateAccessOnly()
    {
        var nextCalled = false;
        var permissionProvider = new RecordingPermissionProvider
        {
            Permissions = [CloudPermissionCatalog.Employee.UpdateAccess]
        };
        var behavior = new AuthorizationBehavior<UpdateEmployeeAccessCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "hr-admin",
                Roles = [SystemRoles.HrAdmin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            permissionProvider);

        var result = await behavior.Handle(
            new UpdateEmployeeAccessCommand(Guid.NewGuid(), []),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success(true));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
        Assert.DoesNotContain(
            DevicePermissions.Read,
            permissionProvider.Permissions);
    }

    [Theory]
    [InlineData(IIoTClaimTypes.EdgeDeviceActor)]
    [InlineData(IIoTClaimTypes.AiServiceActor)]
    [InlineData(IIoTClaimTypes.EdgeReleasePublisherActor)]
    public async Task MachineIdentity_ShouldBeRejectedBeforeHandler(string actorType)
    {
        var nextCalled = false;
        var behavior = new AuthorizationBehavior<UpdateEmployeeAccessCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "machine",
                Roles = [SystemRoles.Admin],
                ActorType = actorType,
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new UpdateEmployeeAccessCommand(Guid.NewGuid(), []),
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result.Success(true));
                },
                CancellationToken.None));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task UnauthenticatedSubject_ShouldBeRejectedBeforeHandler()
    {
        var nextCalled = false;
        var behavior = new AuthorizationBehavior<UpdateEmployeeAccessCommand, Result<bool>>(
            new TestCurrentUser
            {
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = false
            },
            new RecordingPermissionProvider());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new UpdateEmployeeAccessCommand(Guid.NewGuid(), []),
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result.Success(true));
                },
                CancellationToken.None));

        Assert.False(nextCalled);
    }
}
