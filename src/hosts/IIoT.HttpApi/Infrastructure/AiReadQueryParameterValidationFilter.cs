using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IIoT.HttpApi.Infrastructure;

public sealed class AiReadQueryParameterValidationFilter
    : IAsyncResourceFilter
{
    private static readonly StringComparer ParameterComparer =
        StringComparer.OrdinalIgnoreCase;

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var allowedParameters = context.ActionDescriptor.Parameters
            .OfType<ControllerParameterDescriptor>()
            .Select(CreateQueryParameter)
            .Where(parameter => parameter is not null)
            .Select(parameter => parameter!)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => parameter,
                ParameterComparer);

        var invalidParameters = new HashSet<string>(
            ParameterComparer);
        foreach (var queryParameterName in context.HttpContext.Request
                     .Query.Keys)
        {
            if (!allowedParameters.TryGetValue(
                    queryParameterName,
                    out var allowedParameter))
            {
                invalidParameters.Add(queryParameterName);
                continue;
            }

            if (allowedParameter.IsScalar
                && context.HttpContext.Request.Query[queryParameterName]
                    .Count > 1)
            {
                invalidParameters.Add(queryParameterName);
            }
        }

        if (invalidParameters.Count > 0)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid AI read query parameters",
                Detail = "请求包含未知、重复或不适用的查询参数。"
            };
            problem.Extensions["code"] =
                "ai_read_invalid_query_parameters";
            problem.Extensions["invalidParameters"] = invalidParameters
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            context.Result = new BadRequestObjectResult(problem);
            return;
        }

        await next();
    }

    private static QueryParameter? CreateQueryParameter(
        ControllerParameterDescriptor parameter)
    {
        var fromQuery = parameter.ParameterInfo
            .GetCustomAttribute<FromQueryAttribute>();
        if (fromQuery is null)
        {
            return null;
        }

        var name = parameter.BindingInfo?.BinderModelName
            ?? fromQuery.Name
            ?? parameter.Name;
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new QueryParameter(
                name,
                IsScalar(parameter.ParameterType));
    }

    private static bool IsScalar(Type parameterType)
    {
        var type = Nullable.GetUnderlyingType(parameterType)
            ?? parameterType;
        return type == typeof(string)
               || type == typeof(Guid)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(DateOnly)
               || type == typeof(TimeOnly)
               || type == typeof(decimal)
               || type.IsPrimitive
               || type.IsEnum;
    }

    private sealed record QueryParameter(
        string Name,
        bool IsScalar);
}
