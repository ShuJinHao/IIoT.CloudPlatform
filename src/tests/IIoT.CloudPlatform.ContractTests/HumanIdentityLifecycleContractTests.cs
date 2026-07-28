using System.Reflection;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.HttpApi.Controllers;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class HumanIdentityLifecycleContractTests
{
    [Fact]
    public void HumanStatusVersionClaimAndJwtContract_ShouldBeExplicit()
    {
        Assert.Equal("status_version", IIoTClaimTypes.IdentityStatusVersion);

        var method = typeof(IJwtTokenGenerator).GetMethod(
            nameof(IJwtTokenGenerator.GenerateHumanToken))!;
        Assert.Equal(
            [
                "userId",
                "userName",
                "roles",
                "permissions",
                "identityStatusVersion"
            ],
            method.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(typeof(string), method.GetParameters()[^1].ParameterType);
    }

    [Fact]
    public void HumanStatusVersion_ShouldDependOnlyOnStatusSpecificState()
    {
        var method = typeof(CloudIdentityStatusVersions).GetMethod(
            nameof(CloudIdentityStatusVersions.Create))!;

        Assert.Equal(
            [
                "cloudUserId",
                "accountEnabled",
                "employeeActive",
                "accountSecurityStamp"
            ],
            method.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void ActivateEmployeeCommand_ShouldReuseDeactivatePermissionAndEmployeeLock()
    {
        var authorization = Assert.Single(
            typeof(ActivateEmployeeCommand)
                .GetCustomAttributes<AuthorizeRequirementAttribute>());
        var distributedLock = Assert.Single(
            typeof(ActivateEmployeeCommand)
                .GetCustomAttributes<DistributedLockAttribute>());

        Assert.Equal(CloudPermissionCatalog.Employee.Deactivate, authorization.Permission);
        Assert.Equal("iiot:lock:employee:{EmployeeId}", distributedLock.KeyTemplate);
        Assert.Equal(5, distributedLock.TimeoutSeconds);
        Assert.Empty(typeof(ActivateEmployeeCommand)
            .GetCustomAttributes<AdminOnlyAttribute>());
    }

    [Fact]
    public void HumanEmployeeController_ShouldExposeExactActivatePutRoute()
    {
        var action = typeof(HumanEmployeeController).GetMethod(
            nameof(HumanEmployeeController.Activate))!;
        var httpMethod = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());

        Assert.Equal(["PUT"], httpMethod.HttpMethods);
        Assert.Equal("{id}/activate", httpMethod.Template);
        Assert.Equal(
            ["id", "cancellationToken"],
            action.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void HumanSessionRevocationContract_ShouldCoverOneSubjectAndReason()
    {
        var method = typeof(IHumanSessionRevocationService).GetMethod(
            nameof(IHumanSessionRevocationService.RevokeAllAsync))!;

        Assert.Equal(
            ["subjectId", "reason", "cancellationToken"],
            method.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void HumanRefreshContract_ShouldRequireAndPreserveIdentityStatusVersion()
    {
        var issueMethod = typeof(IRefreshTokenService).GetMethod(
            nameof(IRefreshTokenService.IssueHumanAsync))!;

        Assert.Equal(
            ["subjectId", "identityStatusVersion", "cancellationToken"],
            issueMethod.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            typeof(string),
            typeof(RefreshTokenRotationResult)
                .GetProperty(nameof(RefreshTokenRotationResult.IdentityStatusVersion))!
                .PropertyType);
    }
}
