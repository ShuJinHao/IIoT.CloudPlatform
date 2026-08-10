using System.Reflection;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class AiReadQueryParameterValidationFilterTests
{
    private const string SecretValue = "SENSITIVE-QUERY-VALUE";

    public static TheoryData<string> AiReadGetActions
        => new(
            typeof(AiReadController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<HttpGetAttribute>()
                    is not null)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

    [Theory]
    [MemberData(nameof(AiReadGetActions))]
    public async Task Filter_ShouldAllowEveryDeclaredQueryParameter(
        string actionName)
    {
        var method = GetAction(actionName);
        var query = string.Join(
            "&",
            GetAllowedNames(method).Select(name => $"{name}=value"));

        var (result, nextCalled) = await ExecuteAsync(
            method,
            query);

        Assert.True(nextCalled);
        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(AiReadGetActions))]
    public async Task Filter_ShouldRejectUnknownTypoAndInapplicableParameters(
        string actionName)
    {
        var method = GetAction(actionName);
        var allowedNames = GetAllowedNames(method);
        var typo = allowedNames[0] + "x";
        var inapplicable = GetAllAllowedNames()
            .First(name => !allowedNames.Contains(
                name,
                StringComparer.OrdinalIgnoreCase));

        foreach (var invalidName in new[]
                 {
                     "unknownField",
                     typo,
                     inapplicable
                 })
        {
            var (result, nextCalled) = await ExecuteAsync(
                method,
                $"{invalidName}={SecretValue}");

            Assert.False(nextCalled);
            AssertInvalidProblem(
                result,
                invalidName,
                SecretValue);
        }
    }

    [Theory]
    [MemberData(nameof(AiReadGetActions))]
    public async Task Filter_ShouldRejectRepeatedScalarParameter(
        string actionName)
    {
        var method = GetAction(actionName);
        var parameterName = GetAllowedNames(method)[0];

        var (result, nextCalled) = await ExecuteAsync(
            method,
            $"{parameterName}=first&{parameterName}=second");

        Assert.False(nextCalled);
        AssertInvalidProblem(result, parameterName, "first", "second");
    }

    [Fact]
    public async Task Filter_ShouldCompareNamesIgnoringCase_AndSortInvalidNames()
    {
        var method = GetAction(nameof(AiReadController.GetDevices));

        var (allowedResult, allowedNextCalled) = await ExecuteAsync(
            method,
            "DEVICEID=00000000-0000-0000-0000-000000000001");
        var (invalidResult, invalidNextCalled) = await ExecuteAsync(
            method,
            $"unknown={SecretValue}&deviceCod={SecretValue}");

        Assert.True(allowedNextCalled);
        Assert.Null(allowedResult);
        Assert.False(invalidNextCalled);
        var problem = AssertInvalidProblem(
            invalidResult,
            "deviceCod",
            SecretValue);
        Assert.Equal(
            ["deviceCod", "unknown"],
            Assert.IsType<string[]>(
                problem.Extensions["invalidParameters"]));
    }

    [Fact]
    public void AiReadController_ShouldOwnTheValidationFilter()
    {
        var attribute = Assert.Single(
            typeof(AiReadController)
                .GetCustomAttributes<ServiceFilterAttribute>(),
            candidate => candidate.ServiceType
                == typeof(AiReadQueryParameterValidationFilter));

        Assert.Equal(
            typeof(AiReadQueryParameterValidationFilter),
            attribute.ServiceType);
    }

    private static async Task<(IActionResult? Result, bool NextCalled)>
        ExecuteAsync(
            MethodInfo method,
            string query)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = string.IsNullOrEmpty(query)
            ? QueryString.Empty
            : new QueryString("?" + query);
        var descriptor = CreateActionDescriptor(method);
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            descriptor,
            new ModelStateDictionary());
        var context = new ResourceExecutingContext(
            actionContext,
            [],
            []);
        var nextCalled = false;
        var filter = new AiReadQueryParameterValidationFilter();

        await filter.OnResourceExecutionAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(new ResourceExecutedContext(
                    actionContext,
                    []));
            });

        return (context.Result, nextCalled);
    }

    private static ControllerActionDescriptor CreateActionDescriptor(
        MethodInfo method)
    {
        var descriptor = new ControllerActionDescriptor
        {
            ActionName = method.Name,
            ControllerName = nameof(AiReadController),
            ControllerTypeInfo = typeof(AiReadController).GetTypeInfo(),
            MethodInfo = method
        };
        descriptor.Parameters = method.GetParameters()
            .Select(parameter => (ParameterDescriptor)
                new ControllerParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterInfo = parameter,
                    ParameterType = parameter.ParameterType
                })
            .ToList();
        return descriptor;
    }

    private static MethodInfo GetAction(string actionName)
        => typeof(AiReadController).GetMethod(actionName)
           ?? throw new InvalidOperationException(
               $"AiRead action '{actionName}' was not found.");

    private static IReadOnlyList<string> GetAllowedNames(MethodInfo method)
        => method.GetParameters()
            .Select(parameter => new
            {
                Parameter = parameter,
                Attribute = parameter.GetCustomAttribute<FromQueryAttribute>()
            })
            .Where(item => item.Attribute is not null)
            .Select(item => item.Attribute!.Name
                ?? item.Parameter.Name!)
            .ToArray();

    private static IReadOnlyList<string> GetAllAllowedNames()
        => typeof(AiReadController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<HttpGetAttribute>()
                is not null)
            .SelectMany(GetAllowedNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ProblemDetails AssertInvalidProblem(
        IActionResult? result,
        string expectedInvalidParameter,
        params string[] forbiddenValues)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(
            "Invalid AI read query parameters",
            problem.Title);
        Assert.Equal(
            "ai_read_invalid_query_parameters",
            problem.Extensions["code"]);
        Assert.Contains(
            expectedInvalidParameter,
            Assert.IsType<string[]>(
                problem.Extensions["invalidParameters"]),
            StringComparer.OrdinalIgnoreCase);
        var serialized = System.Text.Json.JsonSerializer.Serialize(problem);
        foreach (var forbiddenValue in forbiddenValues)
        {
            Assert.DoesNotContain(
                forbiddenValue,
                serialized,
                StringComparison.Ordinal);
        }

        return problem;
    }
}
