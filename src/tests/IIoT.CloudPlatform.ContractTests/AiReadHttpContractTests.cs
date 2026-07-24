using System.Reflection;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.AiRead;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class AiReadHttpContractTests
{
    [Fact]
    public void AiReadProductionRecords_ShouldExposeOnlyReadRouteAndProductionRecordPermission()
    {
        var controller = typeof(AiReadController);
        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == controller)
            .ToArray();
        var productionRecords = controller.GetMethod(nameof(AiReadController.GetProductionRecords))!;
        var authorization = typeof(GetAiReadProductionRecordsQuery)
            .GetCustomAttribute<AuthorizeAiReadAttribute>();

        Assert.Equal("api/v1/ai/read", controller.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal(HttpApiPolicies.RequireAiReadToken, controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal(HttpApiRateLimitPolicies.AiRead, controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal("production-records", productionRecords.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal(AiReadPermissions.ProductionRecord, authorization?.Permission);
        Assert.Contains(productionRecords.GetParameters(), parameter => parameter.Name == "plcCode"
            && parameter.GetCustomAttribute<FromQueryAttribute>() is not null);
        Assert.Contains(productionRecords.GetParameters(), parameter => parameter.Name == "plcName"
            && parameter.GetCustomAttribute<FromQueryAttribute>() is not null);
        Assert.DoesNotContain(actions, method => method.GetCustomAttributes<HttpMethodAttribute>()
            .Any(attribute => attribute.HttpMethods.Any(verb => !string.Equals(verb, "GET", StringComparison.Ordinal))));
        Assert.DoesNotContain(actions.SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()), attribute =>
            attribute.Template?.Contains("pass-stations", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void AiReadQueries_ShouldCarryExactlyOneAiReadAuthorizationContract()
    {
        var queryTypes = typeof(GetAiReadProductionRecordsQuery).Assembly.GetTypes()
            .Where(type => type.IsClass && type.Name.StartsWith("GetAiRead", StringComparison.Ordinal)
                && type.Name.EndsWith("Query", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(queryTypes);
        foreach (var queryType in queryTypes)
        {
            var attribute = Assert.Single(queryType.GetCustomAttributes<AuthorizeAiReadAttribute>());
            Assert.StartsWith("AiRead.", attribute.Permission, StringComparison.Ordinal);
            Assert.True(attribute.Permission.Length > "AiRead.".Length, queryType.FullName);
        }
    }

    [Fact]
    public void AiReadProductionRecordDto_ShouldExposeStructuredFieldsWithoutRawPayloadColumn()
    {
        var properties = typeof(AiReadProductionRecordDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains(nameof(AiReadProductionRecordDto.Fields), properties);
        Assert.Contains(nameof(AiReadProductionRecordDto.FieldSchema), properties);
        Assert.DoesNotContain(properties, property =>
            property.Contains("payload_jsonb", StringComparison.OrdinalIgnoreCase));
    }
}
