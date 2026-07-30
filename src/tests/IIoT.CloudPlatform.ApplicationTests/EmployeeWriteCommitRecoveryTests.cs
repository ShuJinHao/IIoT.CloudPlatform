using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class EmployeeWriteCommitRecoveryTests
{
    [Fact]
    public async Task Onboard_PostCommitFailureWithBothAggregates_ShouldRecoverSharedId()
    {
        var repository = new InMemoryRepository<Employee>();
        var identityStore = new RecordingIdentityAccountStore();
        var unitOfWork = CommitFails();
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (employeeId, _) =>
                Task.FromResult(new EmployeeMutationObservation(
                    EmployeeExists: true,
                    EmployeeIsActive: true,
                    AccountExists: true,
                    AccountIsEnabled: true,
                    AccountSecurityStamp: "created",
                    Roles: [],
                    EmployeeNo: "E-RECOVER-ONBOARD",
                    EmployeeRealName: "Recovered Onboard",
                    EmployeeRowVersion: 1,
                    EmployeeDeviceIds: [],
                    AccountEmployeeNo: "E-RECOVER-ONBOARD",
                    HasActiveHumanSessions: false))
        };
        var handler = CreateOnboardHandler(
            repository,
            identityStore,
            unitOfWork,
            observer);

        var result = await handler.Handle(
            new OnboardEmployeeCommand(
                " E-RECOVER-ONBOARD ",
                " Recovered Onboard ",
                "Password123!"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value, Assert.Single(identityStore.CreatedAccounts).Id);
        Assert.Equal(result.Value, Assert.IsType<Employee>(repository.AddedEntity).Id);
        Assert.Single(repository.ListResult);
        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task Onboard_PostCommitFailureWithPartialAggregate_ShouldConflict()
    {
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) =>
                Task.FromResult(new EmployeeMutationObservation(
                    EmployeeExists: true,
                    EmployeeIsActive: true,
                    AccountExists: false,
                    AccountIsEnabled: false,
                    AccountSecurityStamp: null,
                    Roles: [],
                    EmployeeNo: "E-RECOVER-PARTIAL",
                    EmployeeRealName: "Partial",
                    EmployeeRowVersion: 1,
                    EmployeeDeviceIds: [],
                    AccountEmployeeNo: null,
                    HasActiveHumanSessions: false))
        };
        var handler = CreateOnboardHandler(
            new InMemoryRepository<Employee>(),
            new RecordingIdentityAccountStore(),
            CommitFails(),
            observer);

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new OnboardEmployeeCommand(
                    "E-RECOVER-PARTIAL",
                    "Partial",
                    "Password123!"),
                CancellationToken.None));
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("timeout")]
    public async Task Onboard_PostCommitObservationUnavailable_ShouldBeCommitUnknown(
        string observationFailure)
    {
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, token) => observationFailure switch
            {
                "failure" => throw new InvalidOperationException(
                    "sensitive observation failure"),
                "timeout" => throw new OperationCanceledException(token),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(observationFailure))
            }
        };
        var handler = CreateOnboardHandler(
            new InMemoryRepository<Employee>(),
            new RecordingIdentityAccountStore(),
            CommitFails(),
            observer);

        var exception =
            await Assert.ThrowsAsync<EmployeeWriteCommitUnknownException>(() =>
                handler.Handle(
                    new OnboardEmployeeCommand(
                        "E-RECOVER-UNKNOWN",
                        "Unknown",
                        "Password123!"),
                    CancellationToken.None));

        Assert.Equal(
            EmployeeWriteCommitUnknownException.Code,
            exception.ProblemCode);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("access")]
    [InlineData("deactivate")]
    [InlineData("terminate")]
    public async Task ExistingEmployeeWrites_ShouldObserveBeforeOpeningWriteTransaction(
        string operation)
    {
        var employee = new Employee(
            Guid.NewGuid(),
            $"E-OBSERVE-FIRST-{operation}",
            "Before");
        var identityStore = EnabledIdentity(employee, "baseline-stamp");
        var unitOfWork = new RecordingUnitOfWork();
        var observedBeforeTransaction = false;
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) =>
            {
                observedBeforeTransaction = unitOfWork.BeginCalls == 0;
                return Task.FromResult(Observe(
                    employee,
                    accountEnabled: true,
                    securityStamp: "baseline-stamp"));
            }
        };

        switch (operation)
        {
            case "profile":
                await new UpdateEmployeeProfileHandler(
                        RepositoryWith(employee),
                        unitOfWork,
                        new StubAdminTargetGuard(),
                        observer)
                    .Handle(
                        new UpdateEmployeeProfileCommand(employee.Id, "After"),
                        CancellationToken.None);
                break;
            case "access":
                var deviceId = Guid.NewGuid();
                await new UpdateEmployeeAccessHandler(
                        RepositoryWith(employee),
                        new StubAdminTargetGuard(),
                        new StubDeviceReadQueryService
                        {
                            ExistingDeviceIds = [deviceId]
                        },
                        unitOfWork,
                        observer,
                        new StubEmployeeMutationVersionStore { Result = 23 })
                    .Handle(
                        new UpdateEmployeeAccessCommand(
                            employee.Id,
                            [deviceId]),
                        CancellationToken.None);
                break;
            case "deactivate":
                await new DeactivateEmployeeHandler(
                        RepositoryWith(employee),
                        identityStore,
                        unitOfWork,
                        new StubHumanSessionRevocationService(),
                        new StubAdminTargetGuard(),
                        observer)
                    .Handle(
                        new DeactivateEmployeeCommand(employee.Id),
                        CancellationToken.None);
                break;
            case "terminate":
                await new TerminateEmployeeHandler(
                        RepositoryWith(employee),
                        identityStore,
                        unitOfWork,
                        new StubHumanSessionRevocationService(),
                        new StubAdminTargetGuard(),
                        observer)
                    .Handle(
                        new TerminateEmployeeCommand(employee.Id),
                        CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Assert.True(observedBeforeTransaction);
        Assert.Equal(1, observer.Calls);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task Profile_AttemptObservation_ShouldPropagateCallbackCancellation()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-OBSERVE-CANCEL",
            "Before");
        using var requestCancellation = new CancellationTokenSource();
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, token) =>
            {
                requestCancellation.Cancel();
                Assert.True(token.IsCancellationRequested);
                throw new OperationCanceledException(token);
            }
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new UpdateEmployeeProfileHandler(
            RepositoryWith(employee),
            unitOfWork,
            new StubAdminTargetGuard(),
            observer);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(
                new UpdateEmployeeProfileCommand(employee.Id, "After"),
                requestCancellation.Token));

        Assert.Equal(requestCancellation.Token, exception.CancellationToken);
        Assert.Equal(0, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task Profile_PostCommitFailureWithExactTarget_ShouldRecover()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-PROFILE",
            "Before");
        var baseline = Observe(employee, realName: "Before");
        var observer = Sequence(
            baseline,
            baseline with { EmployeeRealName = "After" });
        var handler = new UpdateEmployeeProfileHandler(
            RepositoryWith(employee),
            CommitFails(),
            new StubAdminTargetGuard(),
            observer);

        var result = await handler.Handle(
            new UpdateEmployeeProfileCommand(employee.Id, " After "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("After", employee.RealName);
        Assert.Equal(2, observer.Calls);
    }

    [Fact]
    public async Task Profile_PostCommitFailureWithBaselineOnly_ShouldBeCommitUnknown()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-PROFILE-BASE",
            "Before");
        var baseline = Observe(employee, realName: "Before");
        var handler = new UpdateEmployeeProfileHandler(
            RepositoryWith(employee),
            CommitFails(),
            new StubAdminTargetGuard(),
            Sequence(baseline, baseline));

        await Assert.ThrowsAsync<EmployeeWriteCommitUnknownException>(() =>
            handler.Handle(
                new UpdateEmployeeProfileCommand(employee.Id, "After"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Profile_PostCommitFailureWithConcurrentDrift_ShouldConflict()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-PROFILE-DRIFT",
            "Before");
        var baseline = Observe(employee, realName: "Before");
        var handler = new UpdateEmployeeProfileHandler(
            RepositoryWith(employee),
            CommitFails(),
            new StubAdminTargetGuard(),
            Sequence(
                baseline,
                baseline with
                {
                    EmployeeRealName = "Concurrent",
                    EmployeeRowVersion = 2
                }));

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new UpdateEmployeeProfileCommand(employee.Id, "After"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Access_PostCommitCancellationWithExactTarget_ShouldRecoverOutsideRequestToken()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-ACCESS",
            "Access");
        var oldDeviceId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        employee.AddDeviceAccess(oldDeviceId);
        var baseline = Observe(
            employee,
            deviceIds: [oldDeviceId],
            rowVersion: 0);
        using var requestCancellation = new CancellationTokenSource();
        var observationCall = 0;
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, token) =>
            {
                observationCall++;
                if (observationCall == 2)
                {
                    Assert.True(requestCancellation.IsCancellationRequested);
                    Assert.False(token.IsCancellationRequested);
                    Assert.NotEqual(requestCancellation.Token, token);
                }

                return Task.FromResult(
                    observationCall == 1
                        ? baseline
                        : baseline with
                        {
                            EmployeeDeviceIds = [newDeviceId],
                            EmployeeRowVersion = 23
                        });
            }
        };
        var unitOfWork = new RecordingUnitOfWork
        {
            OnCommit = () =>
            {
                requestCancellation.Cancel();
                throw new OperationCanceledException(requestCancellation.Token);
            }
        };
        var versionStore = new StubEmployeeMutationVersionStore
        {
            Result = 23
        };
        var handler = new UpdateEmployeeAccessHandler(
            RepositoryWith(employee),
            new StubAdminTargetGuard(),
            new StubDeviceReadQueryService
            {
                ExistingDeviceIds = [newDeviceId]
            },
            unitOfWork,
            observer,
            versionStore);

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(employee.Id, [newDeviceId]),
            requestCancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, observer.Calls);
        Assert.Equal(1, versionStore.Calls);
        Assert.Equal([newDeviceId], employee.DeviceAccesses
            .Select(access => access.DeviceId));
    }

    [Fact]
    public async Task Access_PostCommitFailureWithDifferentTarget_ShouldConflict()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-ACCESS-DRIFT",
            "Access");
        var requestedDeviceId = Guid.NewGuid();
        var concurrentDeviceId = Guid.NewGuid();
        var baseline = Observe(employee, deviceIds: [], rowVersion: 0);
        var handler = new UpdateEmployeeAccessHandler(
            RepositoryWith(employee),
            new StubAdminTargetGuard(),
            new StubDeviceReadQueryService
            {
                ExistingDeviceIds = [requestedDeviceId]
            },
            CommitFails(),
            Sequence(
                baseline,
                baseline with
                {
                    EmployeeDeviceIds = [concurrentDeviceId],
                    EmployeeRowVersion = 24
                }),
            new StubEmployeeMutationVersionStore { Result = 23 });

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new UpdateEmployeeAccessCommand(
                    employee.Id,
                    [requestedDeviceId]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_PostCommitFailureWithExactTarget_ShouldRecover()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-DEACTIVATE",
            "Deactivate");
        var identityStore = EnabledIdentity(employee, "baseline-stamp");
        var baseline = Observe(
            employee,
            accountEnabled: true,
            securityStamp: "baseline-stamp",
            roles: ["Operator"],
            hasActiveSessions: true);
        identityStore.RolesByUserId[employee.Id] = ["Operator"];
        var observer = new StubEmployeeMutationObservationReader();
        var observationCall = 0;
        observer.ObserveAsyncOverride = (_, _) =>
        {
            observationCall++;
            if (observationCall == 1)
            {
                return Task.FromResult(baseline);
            }

            identityStore.SecurityStampsByUserId.TryGetValue(
                employee.Id,
                out var securityStamp);
            return Task.FromResult(
                baseline with
                {
                    EmployeeIsActive = false,
                    AccountIsEnabled = false,
                    AccountSecurityStamp = securityStamp,
                    HasActiveHumanSessions = false
                });
        };
        var handler = new DeactivateEmployeeHandler(
            RepositoryWith(employee),
            identityStore,
            CommitFails(),
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        var result = await handler.Handle(
            new DeactivateEmployeeCommand(employee.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(employee.IsActive);
        Assert.False(identityStore.AccountById!.IsEnabled);
        Assert.Single(identityStore.StateCompareExchanges);
    }

    [Fact]
    public async Task Deactivate_PostCommitFailureWithRoleDrift_ShouldConflict()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-DEACTIVATE-DRIFT",
            "Deactivate");
        var identityStore = EnabledIdentity(employee, "baseline-stamp");
        var baseline = Observe(
            employee,
            accountEnabled: true,
            securityStamp: "baseline-stamp",
            roles: ["Operator"],
            hasActiveSessions: true);
        var observationCall = 0;
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) =>
            {
                observationCall++;
                if (observationCall == 1)
                {
                    return Task.FromResult(baseline);
                }

                identityStore.SecurityStampsByUserId.TryGetValue(
                    employee.Id,
                    out var securityStamp);
                return Task.FromResult(
                    baseline with
                    {
                        EmployeeIsActive = false,
                        AccountIsEnabled = false,
                        AccountSecurityStamp = securityStamp,
                        Roles = ["Supervisor"],
                        HasActiveHumanSessions = false
                    });
            }
        };
        var handler = new DeactivateEmployeeHandler(
            RepositoryWith(employee),
            identityStore,
            CommitFails(),
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new DeactivateEmployeeCommand(employee.Id),
                CancellationToken.None));
    }

    [Fact]
    public async Task Terminate_PostCommitFailureWithAllStateAbsent_ShouldRecover()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-TERMINATE",
            "Terminate");
        var identityStore = EnabledIdentity(employee, "baseline-stamp");
        var baseline = Observe(
            employee,
            accountEnabled: true,
            securityStamp: "baseline-stamp",
            hasActiveSessions: true);
        var repository = RepositoryWith(employee);
        var handler = new TerminateEmployeeHandler(
            repository,
            identityStore,
            CommitFails(),
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Sequence(baseline, Absent()));

        var result = await handler.Handle(
            new TerminateEmployeeCommand(employee.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.ListResult);
        Assert.Null(identityStore.AccountById);
        Assert.Single(identityStore.DeletedIds);
    }

    [Fact]
    public async Task Terminate_PostCommitFailureWithPartialDeletion_ShouldConflict()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-TERMINATE-PARTIAL",
            "Terminate");
        var identityStore = EnabledIdentity(employee, "baseline-stamp");
        var baseline = Observe(
            employee,
            accountEnabled: true,
            securityStamp: "baseline-stamp",
            hasActiveSessions: true);
        var partial = baseline with
        {
            EmployeeExists = false,
            EmployeeIsActive = false,
            EmployeeNo = null,
            EmployeeRealName = null,
            EmployeeRowVersion = null,
            EmployeeDeviceIds = []
        };
        var handler = new TerminateEmployeeHandler(
            RepositoryWith(employee),
            identityStore,
            CommitFails(),
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Sequence(baseline, partial));

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new TerminateEmployeeCommand(employee.Id),
                CancellationToken.None));
    }

    [Fact]
    public async Task Terminate_PostCommitFailureWithBaselineOnly_ShouldBeCommitUnknown()
    {
        var employee = new Employee(
            Guid.NewGuid(),
            "E-RECOVER-TERMINATE-BASE",
            "Terminate");
        var identityStore = EnabledIdentity(employee, "baseline-stamp");
        var baseline = Observe(
            employee,
            accountEnabled: true,
            securityStamp: "baseline-stamp",
            hasActiveSessions: true);
        var handler = new TerminateEmployeeHandler(
            RepositoryWith(employee),
            identityStore,
            CommitFails(),
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Sequence(baseline, baseline));

        await Assert.ThrowsAsync<EmployeeWriteCommitUnknownException>(() =>
            handler.Handle(
                new TerminateEmployeeCommand(employee.Id),
                CancellationToken.None));
    }

    private static OnboardEmployeeHandler CreateOnboardHandler(
        InMemoryRepository<Employee> repository,
        RecordingIdentityAccountStore identityStore,
        RecordingUnitOfWork unitOfWork,
        IEmployeeMutationObservationReader observer)
        => new(
            identityStore,
            new StubIdentityPasswordService(),
            new StubRolePolicyService(),
            repository,
            unitOfWork,
            new TestCurrentUser(),
            new RecordingPermissionProvider(),
            observer);

    private static RecordingUnitOfWork CommitFails()
        => new()
        {
            OnCommit = () => throw new TimeoutException(
                "simulated commit acknowledgement loss")
        };

    private static InMemoryRepository<Employee> RepositoryWith(
        Employee employee)
    {
        var repository = new InMemoryRepository<Employee>();
        repository.ListResult.Add(employee);
        return repository;
    }

    private static RecordingIdentityAccountStore EnabledIdentity(
        Employee employee,
        string securityStamp)
    {
        var account = IdentityAccount.Create(
            employee.Id,
            employee.EmployeeNo);
        var store = new RecordingIdentityAccountStore
        {
            AccountById = account,
            AccountByEmployeeNo = account
        };
        store.SecurityStampsByUserId[employee.Id] = securityStamp;
        return store;
    }

    private static StubEmployeeMutationObservationReader Sequence(
        params EmployeeMutationObservation[] observations)
    {
        var index = 0;
        return new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) =>
            {
                var observation = observations[
                    Math.Min(index, observations.Length - 1)];
                index++;
                return Task.FromResult(observation);
            }
        };
    }

    private static EmployeeMutationObservation Observe(
        Employee employee,
        string? realName = null,
        IReadOnlyList<Guid>? deviceIds = null,
        uint? rowVersion = null,
        bool accountEnabled = true,
        string? securityStamp = "baseline-stamp",
        IReadOnlyList<string>? roles = null,
        bool hasActiveSessions = false)
        => new(
            EmployeeExists: true,
            EmployeeIsActive: employee.IsActive,
            AccountExists: true,
            AccountIsEnabled: accountEnabled,
            AccountSecurityStamp: securityStamp,
            Roles: roles ?? [],
            EmployeeNo: employee.EmployeeNo,
            EmployeeRealName: realName ?? employee.RealName,
            EmployeeRowVersion: rowVersion ?? employee.RowVersion,
            EmployeeDeviceIds: deviceIds
                ?? employee.DeviceAccesses
                    .Select(access => access.DeviceId)
                    .OrderBy(deviceId => deviceId)
                    .ToArray(),
            AccountEmployeeNo: employee.EmployeeNo,
            HasActiveHumanSessions: hasActiveSessions);

    private static EmployeeMutationObservation Absent()
        => new(
            EmployeeExists: false,
            EmployeeIsActive: false,
            AccountExists: false,
            AccountIsEnabled: false,
            AccountSecurityStamp: null,
            Roles: [],
            EmployeeNo: null,
            EmployeeRealName: null,
            EmployeeRowVersion: null,
            EmployeeDeviceIds: [],
            AccountEmployeeNo: null,
            HasActiveHumanSessions: false);
}
