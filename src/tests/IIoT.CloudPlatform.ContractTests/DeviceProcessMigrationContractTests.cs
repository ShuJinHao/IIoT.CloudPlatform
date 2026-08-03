using System.Reflection;
using IIoT.HttpApi.Controllers;
using IIoT.ProductionService.Commands.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class DeviceProcessMigrationContractTests
{
    [Fact]
    public void HumanDeviceController_ShouldExposeExactMigrationAndLedgerContextRoutes()
    {
        var controller = typeof(HumanDeviceController);
        var impact = controller.GetMethod(
            nameof(HumanDeviceController.GetProcessMigrationImpact))!;
        var migrate = controller.GetMethod(
            nameof(HumanDeviceController.MigrateProcess))!;
        var processOptions = controller.GetMethod(
            nameof(HumanDeviceController.GetLedgerProcessOptions))!;

        AssertRoute(impact, "GET", "{id}/process-migration-impact");
        AssertRoute(migrate, "POST", "{id}/process-migration");
        AssertRoute(processOptions, "GET", "processes/select");
        Assert.Equal(
            ["id", "targetProcessId", "cancellationToken"],
            impact.GetParameters().Select(parameter => parameter.Name));
        Assert.NotNull(impact.GetParameters()[0].GetCustomAttribute<FromRouteAttribute>());
        Assert.NotNull(impact.GetParameters()[1].GetCustomAttribute<FromQueryAttribute>());
        Assert.Equal(
            ["id", "command", "cancellationToken"],
            migrate.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            typeof(MigrateDeviceProcessCommand),
            migrate.GetParameters()[1].ParameterType);
        Assert.NotNull(migrate.GetParameters()[1].GetCustomAttribute<FromBodyAttribute>());
    }

    private static void AssertRoute(
        MethodInfo action,
        string verb,
        string template)
    {
        var attribute = Assert.Single(
            action.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal([verb], attribute.HttpMethods);
        Assert.Equal(template, attribute.Template);
    }
}
