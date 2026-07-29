using System.Reflection;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.HttpApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class EmployeeDeviceAccessIntegrityContractTests
{
    [Fact]
    public void UpdateAccess_ShouldKeepExistingRouteAndRequestContract()
    {
        var action = typeof(HumanEmployeeController).GetMethod(
            nameof(HumanEmployeeController.UpdateAccess))!;
        var httpMethod = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
        var parameters = action.GetParameters();
        var commandParameter = Assert.Single(
            parameters,
            parameter => parameter.ParameterType == typeof(UpdateEmployeeAccessCommand));

        Assert.Equal(["PUT"], httpMethod.HttpMethods);
        Assert.Equal("{id}/access", httpMethod.Template);
        Assert.Equal(
            ["id", "command", "cancellationToken"],
            parameters.Select(parameter => parameter.Name));
        Assert.NotNull(commandParameter.GetCustomAttribute<FromBodyAttribute>());
        Assert.Equal(
            [
                (nameof(UpdateEmployeeAccessCommand.DeviceIds), typeof(List<Guid>)),
                (nameof(UpdateEmployeeAccessCommand.EmployeeId), typeof(Guid))
            ],
            typeof(UpdateEmployeeAccessCommand)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => (property.Name, property.PropertyType))
                .OrderBy(property => property.Name, StringComparer.Ordinal));
    }
}
