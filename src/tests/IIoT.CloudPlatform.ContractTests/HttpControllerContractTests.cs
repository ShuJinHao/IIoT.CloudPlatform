using System.Reflection;
using System.Security.Claims;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;
using CloudResult = IIoT.SharedKernel.Result.IResult;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class HttpControllerContractTests
{
    [Fact]
    public void BootstrapLookup_ShouldExposeExactQueryHeaderRouteAndRatePolicy()
    {
        var controller = typeof(EdgeBootstrapController);
        var action = controller.GetMethod(nameof(EdgeBootstrapController.GetDeviceByInstance))!;
        var parameters = action.GetParameters();

        Assert.Equal("api/v1/edge/bootstrap", GetRequiredAttribute<RouteAttribute>(controller).Template);
        Assert.Equal("device-instance", GetRequiredAttribute<HttpGetAttribute>(action).Template);
        Assert.Equal(HttpApiRateLimitPolicies.Bootstrap, GetRequiredAttribute<EnableRateLimitingAttribute>(action).PolicyName);
        Assert.NotNull(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("clientCode", parameters[0].Name);
        Assert.NotNull(parameters[0].GetCustomAttribute<FromQueryAttribute>());
        Assert.Equal("bootstrapSecret", parameters[1].Name);
        Assert.Equal(
            BootstrapSecretHeaderNames.Secret,
            GetRequiredAttribute<FromHeaderAttribute>(parameters[1]).Name);
    }

    [Fact]
    public void HumanDeviceController_ShouldNotExposeManualBootstrapSecretRotation()
    {
        var actions = typeof(HumanDeviceController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(HumanDeviceController))
            .ToArray();

        Assert.DoesNotContain(actions, method =>
            method.Name.Contains("BootstrapSecret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            actions.SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
                .Select(attribute => attribute.Template),
            template => template?.Contains("bootstrap-secret", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void CurrentUser_ShouldPreserveEveryDistinctRoleAndExposeNoSingleRoleShortcut()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "employee-1"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "Operator"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "test");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new HttpContextAccessor { HttpContext = context };

        ICurrentUser currentUser = new CurrentUser(accessor);

        Assert.Equal(["Admin", "Operator"], currentUser.Roles);
        Assert.Null(typeof(ICurrentUser).GetProperty("Role"));
        Assert.Null(typeof(CurrentUser).GetProperty("Role"));
    }

    [Fact]
    public void ControllerRoutes_ShouldBelongToAnApprovedHttpSurface()
    {
        string[] approvedPrefixes =
        [
            "api/v1/human/",
            "api/v1/edge/",
            "api/v1/public/",
            "api/v1/machine/",
            "api/v1/ai/read",
            "api/v1/ai/identity"
        ];
        var invalid = typeof(EdgeBootstrapController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .Select(type => (Type: type, Route: type.GetCustomAttribute<RouteAttribute>()?.Template))
            .Where(item => item.Route is not null)
            .Where(item => !approvedPrefixes.Any(prefix => item.Route!.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(item => $"{item.Type.FullName}:{item.Route}")
            .ToArray();

        Assert.Empty(invalid);
    }

    [Fact]
    public void EdgeHostControllers_ShouldKeepHumanReadAndEdgeReportSurfacesSeparated()
    {
        var human = typeof(HumanEdgeHostController);
        var humanActions = human.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == human)
            .ToArray();
        var edge = typeof(EdgeHostPlcRuntimeStateController);
        var report = edge.GetMethod(nameof(EdgeHostPlcRuntimeStateController.Report))!;

        Assert.Equal("api/v1/human/edge-hosts", GetRequiredAttribute<RouteAttribute>(human).Template);
        Assert.Equal(HttpApiRateLimitPolicies.GeneralApi, GetRequiredAttribute<EnableRateLimitingAttribute>(human).PolicyName);
        Assert.Equal(
            [null, "{deviceId:guid}", "{deviceId:guid}/plc-runtime-states"],
            humanActions.Select(action => GetRequiredAttribute<HttpGetAttribute>(action).Template));
        Assert.DoesNotContain(humanActions, action => action.GetCustomAttributes<HttpMethodAttribute>()
            .Any(attribute => attribute.HttpMethods.Any(verb => !string.Equals(verb, "GET", StringComparison.Ordinal))));

        Assert.Equal("api/v1/edge/edge-hosts/plc-runtime-states", GetRequiredAttribute<RouteAttribute>(edge).Template);
        Assert.Equal(HttpApiPolicies.RequireEdgeDeviceToken, GetRequiredAttribute<AuthorizeAttribute>(edge).Policy);
        Assert.NotNull(report.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal(
            HttpApiRateLimitPolicies.EdgeHostPlcStateUpload,
            GetRequiredAttribute<EnableRateLimitingAttribute>(report).PolicyName);
        Assert.Equal(
            UploadValidationLimits.MaxUploadRequestBodyBytes,
            ((IRequestSizeLimitMetadata)GetRequiredAttribute<RequestSizeLimitAttribute>(report)).MaxRequestBodySize);
    }

    [Fact]
    public void HumanClientReleaseCatalogAndHistory_ShouldExposeExactReadContracts()
    {
        var controller = typeof(HumanClientReleaseController);
        var catalog = controller.GetMethod(nameof(HumanClientReleaseController.GetCatalog))!;
        var history = controller.GetMethod(nameof(HumanClientReleaseController.GetHistory))!;
        var catalogParameters = catalog.GetParameters();
        var historyParameters = history.GetParameters();

        Assert.Equal(
            "api/v1/human/client-releases",
            GetRequiredAttribute<RouteAttribute>(controller).Template);
        Assert.Equal(
            HttpApiRateLimitPolicies.GeneralApi,
            GetRequiredAttribute<EnableRateLimitingAttribute>(controller).PolicyName);
        Assert.Equal("catalog", GetRequiredAttribute<HttpGetAttribute>(catalog).Template);
        Assert.Equal("history", GetRequiredAttribute<HttpGetAttribute>(history).Template);

        Assert.Equal(
            ["channel", "targetRuntime", "onlyPublished", "cancellationToken"],
            catalogParameters.Select(parameter => parameter.Name));
        Assert.DoesNotContain(
            catalogParameters,
            parameter => string.Equals(parameter.Name, "includeArchived", StringComparison.OrdinalIgnoreCase));
        Assert.All(
            catalogParameters[..3],
            parameter => Assert.NotNull(parameter.GetCustomAttribute<FromQueryAttribute>()));

        Assert.Equal(
            ["channel", "targetRuntime", "pageNumber", "pageSize", "cancellationToken"],
            historyParameters.Select(parameter => parameter.Name));
        Assert.All(
            historyParameters[..4],
            parameter => Assert.NotNull(parameter.GetCustomAttribute<FromQueryAttribute>()));
        Assert.Equal(1, historyParameters[2].DefaultValue);
        Assert.Equal(10, historyParameters[3].DefaultValue);
    }

    [Fact]
    public void HumanDeviceClientOverview_ShouldExposeOnlyPagedListAndReleaseDetailGetContracts()
    {
        var controller = typeof(HumanDeviceClientOverviewController);
        var list = controller.GetMethod(nameof(HumanDeviceClientOverviewController.GetPagedList))!;
        var releaseDetails = controller.GetMethod(
            nameof(HumanDeviceClientOverviewController.GetReleaseDetails))!;
        var listParameters = list.GetParameters();
        var releaseDetailParameters = releaseDetails.GetParameters();
        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == controller)
            .ToArray();

        Assert.Equal(
            "api/v1/human/device-client-overviews",
            GetRequiredAttribute<RouteAttribute>(controller).Template);
        Assert.Equal(
            HttpApiRateLimitPolicies.GeneralApi,
            GetRequiredAttribute<EnableRateLimitingAttribute>(controller).PolicyName);
        Assert.Null(GetRequiredAttribute<HttpGetAttribute>(list).Template);
        Assert.Equal(
            "{deviceId:guid}/release-details",
            GetRequiredAttribute<HttpGetAttribute>(releaseDetails).Template);
        Assert.DoesNotContain(
            actions,
            action => action.GetCustomAttributes<HttpMethodAttribute>()
                .Any(attribute => attribute.HttpMethods.Any(
                    verb => !string.Equals(verb, "GET", StringComparison.Ordinal))));

        Assert.Equal(
            ["pageNumber", "pageSize", "keyword", "sortBy", "sortDirection", "cancellationToken"],
            listParameters.Select(parameter => parameter.Name));
        Assert.All(
            listParameters[..5],
            parameter => Assert.NotNull(parameter.GetCustomAttribute<FromQueryAttribute>()));
        Assert.Equal(1, listParameters[0].DefaultValue);
        Assert.Equal(10, listParameters[1].DefaultValue);

        Assert.Equal(
            ["deviceId", "channel", "targetRuntime", "cancellationToken"],
            releaseDetailParameters.Select(parameter => parameter.Name));
        Assert.NotNull(releaseDetailParameters[0].GetCustomAttribute<FromRouteAttribute>());
        Assert.All(
            releaseDetailParameters[1..3],
            parameter => Assert.NotNull(parameter.GetCustomAttribute<FromQueryAttribute>()));
    }

    [Theory]
    [InlineData(typeof(EdgeDeviceLogController), nameof(EdgeDeviceLogController.Receive), null, HttpApiRateLimitPolicies.DeviceLogUpload)]
    [InlineData(typeof(EdgeCapacityController), nameof(EdgeCapacityController.ReceiveHourly), "hourly", HttpApiRateLimitPolicies.CapacityUpload)]
    [InlineData(typeof(EdgePassStationController), nameof(EdgePassStationController.ReceiveBatch), "{typeKey}/batch", HttpApiRateLimitPolicies.PassStationUpload)]
    public void EdgeUploadActions_ShouldExposeExactLimitsAndDedicatedPolicies(
        Type controller,
        string methodName,
        string? route,
        string ratePolicy)
    {
        var action = controller.GetMethod(methodName)!;
        var httpPost = GetRequiredAttribute<HttpPostAttribute>(action);

        Assert.Equal(route, httpPost.Template);
        Assert.Equal(ratePolicy, GetRequiredAttribute<EnableRateLimitingAttribute>(action).PolicyName);
        Assert.Equal(
            UploadValidationLimits.MaxUploadRequestBodyBytes,
            ((IRequestSizeLimitMetadata)GetRequiredAttribute<RequestSizeLimitAttribute>(action)).MaxRequestBodySize);
        Assert.Equal(HttpApiPolicies.RequireEdgeDeviceToken, GetRequiredAttribute<AuthorizeAttribute>(controller).Policy);
    }

    [Fact]
    public void ApiControllerBase_ShouldReturnProblemDetailsWithStableMediaTypeAndErrors()
    {
        var controller = new ControllerProbe
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Path = "/api/v1/human/devices";

        var response = Assert.IsType<ObjectResult>(controller.Project(Result.Invalid("name is required", "id is invalid")));
        var problem = Assert.IsType<ProblemDetails>(response.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.ContentTypes);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("/api/v1/human/devices", problem.Instance);
        Assert.Equal(
            ["name is required", "id is invalid"],
            Assert.IsAssignableFrom<IEnumerable<string>>(problem.Extensions["errors"]));
        Assert.True(problem.Extensions.ContainsKey("code"));
    }

    private static TAttribute GetRequiredAttribute<TAttribute>(MemberInfo member)
        where TAttribute : Attribute =>
        member.GetCustomAttribute<TAttribute>()
        ?? throw new InvalidOperationException($"{member} does not declare {typeof(TAttribute).Name}.");

    private static TAttribute GetRequiredAttribute<TAttribute>(ParameterInfo parameter)
        where TAttribute : Attribute =>
        parameter.GetCustomAttribute<TAttribute>()
        ?? throw new InvalidOperationException($"{parameter} does not declare {typeof(TAttribute).Name}.");

    private sealed class ControllerProbe : ApiControllerBase
    {
        public IActionResult Project(CloudResult result) => ReturnResult(result);
    }
}
