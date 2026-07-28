using System.Reflection;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class EmployeeAccessDeviceCandidatesContractTests
{
    [Fact]
    public void CandidateDto_ShouldExposeOnlyIdAndDeviceName()
    {
        var properties = typeof(EmployeeAccessDeviceCandidateDto)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (property.Name, property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                (nameof(EmployeeAccessDeviceCandidateDto.DeviceName), typeof(string)),
                (nameof(EmployeeAccessDeviceCandidateDto.Id), typeof(Guid))
            ],
            properties);
        Assert.Empty(typeof(EmployeeAccessDeviceCandidateDto)
            .GetFields(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void HumanDeviceController_ShouldExposeExactHumanOnlyCandidateGetRoute()
    {
        var action = typeof(HumanDeviceController).GetMethod(
            nameof(HumanDeviceController.GetEmployeeAccessCandidates))!;
        var httpMethod = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
        var authorization = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(["GET"], httpMethod.HttpMethods);
        Assert.Equal("employee-access-candidates", httpMethod.Template);
        Assert.Equal(HttpApiPolicies.RequireHumanUserToken, authorization.Policy);
        Assert.Equal(
            ["cancellationToken"],
            action.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void ExistingAllAndSelectRoutes_ShouldRemainUnchanged()
    {
        var getAll = typeof(HumanDeviceController).GetMethod(
            nameof(HumanDeviceController.GetAll))!;
        var getSelect = typeof(HumanDeviceController).GetMethod(
            nameof(HumanDeviceController.GetSelectList))!;

        Assert.Equal(
            "all",
            Assert.Single(getAll.GetCustomAttributes<HttpMethodAttribute>()).Template);
        Assert.Equal(
            "select",
            Assert.Single(getSelect.GetCustomAttributes<HttpMethodAttribute>()).Template);
    }
}
