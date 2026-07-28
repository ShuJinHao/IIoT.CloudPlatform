using System.Reflection;
using System.Text.Json.Serialization;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class EmployeeRoleAssignmentContractTests
{
    [Fact]
    public void Command_ShouldExposeOnlyEmployeeUpdateAccessAndEmployeeLock()
    {
        var commandType = typeof(UpdateEmployeeRoleCommand);
        var authorization = Assert.Single(
            commandType.GetCustomAttributes<AuthorizeRequirementAttribute>());
        var distributedLock = Assert.Single(
            commandType.GetCustomAttributes<DistributedLockAttribute>());

        Assert.Equal(CloudPermissionCatalog.Employee.UpdateAccess, authorization.Permission);
        Assert.Equal("iiot:lock:employee:{EmployeeId}", distributedLock.KeyTemplate);
        Assert.Equal(5, distributedLock.TimeoutSeconds);
        Assert.Empty(commandType.GetCustomAttributes<AdminOnlyAttribute>());
        Assert.DoesNotContain(
            CloudPermissionCatalog.Role.Define,
            commandType.GetCustomAttributes<AuthorizeRequirementAttribute>()
                .Select(attribute => attribute.Permission));
        Assert.DoesNotContain(
            CloudPermissionCatalog.Role.Update,
            commandType.GetCustomAttributes<AuthorizeRequirementAttribute>()
                .Select(attribute => attribute.Permission));
    }

    [Fact]
    public void Controller_ShouldExposeExactHumanOnlyRolePutRoute()
    {
        var action = typeof(HumanEmployeeController).GetMethod(
            nameof(HumanEmployeeController.UpdateRole))!;
        var httpMethod = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
        var authorization = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
        var parameters = action.GetParameters();

        Assert.Equal(["PUT"], httpMethod.HttpMethods);
        Assert.Equal("{id}/role", httpMethod.Template);
        Assert.Equal(HttpApiPolicies.RequireHumanUserToken, authorization.Policy);
        Assert.Equal(
            ["id", "request", "cancellationToken"],
            parameters.Select(parameter => parameter.Name));
        Assert.NotNull(parameters[0].GetCustomAttribute<FromRouteAttribute>());
        Assert.NotNull(parameters[1].GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public void RequestBody_ShouldContainOnlyRequiredNullableRoleName()
    {
        var requestType = typeof(UpdateEmployeeRoleRequest);
        var properties = requestType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var roleName = Assert.Single(properties);
        var unmappedHandling = Assert.Single(
            requestType.GetCustomAttributes<JsonUnmappedMemberHandlingAttribute>());

        Assert.Equal(nameof(UpdateEmployeeRoleRequest.RoleName), roleName.Name);
        Assert.Equal(typeof(string), roleName.PropertyType);
        Assert.NotNull(roleName.GetCustomAttribute<JsonRequiredAttribute>());
        Assert.Equal(
            JsonUnmappedMemberHandling.Disallow,
            unmappedHandling.UnmappedMemberHandling);
        Assert.Empty(requestType.GetFields(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void IdentityStore_ShouldExposeRoleOnlyStatusVersionRotation()
    {
        var method = typeof(IIdentityAccountStore).GetMethod(
            nameof(IIdentityAccountStore.RotateSecurityStampAsync))!;

        Assert.Equal(
            ["id", "cancellationToken"],
            method.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(typeof(Task<IIoT.SharedKernel.Result.Result<bool>>), method.ReturnType);
    }
}
