using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using IResult = IIoT.SharedKernel.Result.IResult;

namespace IIoT.HttpApi.Infrastructure;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    public ISender Sender => HttpContext.RequestServices.GetRequiredService<ISender>();

    [NonAction]
    public IActionResult ReturnResult(IResult result)
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
                {
                    var value = result.GetValue();
                    return value is null ? NoContent() : Ok(value);
                }
            case ResultStatus.Error:
                return ProblemResult(result, StatusCodes.Status400BadRequest, "请求处理失败");

            case ResultStatus.NotFound:
                return ProblemResult(result, StatusCodes.Status404NotFound, "资源不存在");

            case ResultStatus.Invalid:
                return ProblemResult(result, StatusCodes.Status400BadRequest, "请求参数无效");

            case ResultStatus.Forbidden:
                return ProblemResult(result, StatusCodes.Status403Forbidden, "禁止访问");

            case ResultStatus.Unauthorized:
                return ProblemResult(result, StatusCodes.Status401Unauthorized, "未认证或凭据无效");

            default:
                return ProblemResult(result, StatusCodes.Status400BadRequest, "请求处理失败");
        }
    }

    [NonAction]
    public IActionResult ReturnResult<T>(Result<T> result, Func<T, string>? createdLocationFactory = null)
    {
        if (!result.IsSuccess)
        {
            return ReturnResult((IResult)Result.From(result));
        }

        if (result.Value is null)
        {
            return NoContent();
        }

        return createdLocationFactory is null
            ? Ok(result.Value)
            : Created(createdLocationFactory(result.Value), result.Value);
    }

    [NonAction]
    public IActionResult ReturnBodyResult<TSource, TBody>(
        Result<TSource> result,
        Func<TSource, TBody> bodyFactory)
    {
        if (!result.IsSuccess)
        {
            return ReturnResult((IResult)Result.From(result));
        }

        if (result.Value is null)
        {
            return NoContent();
        }

        var body = bodyFactory(result.Value);
        return body is null ? NoContent() : Ok(body);
    }

    private IActionResult ProblemResult(IResult result, int statusCode, string title)
    {
        var errors = result.Errors?
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToArray();

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = errors?.Length == 1 ? errors[0] : null,
            Instance = HttpContext.Request.Path
        }.AddCode(CloudProblemCodes.Resolve(
            statusCode,
            HttpContext.Request.Path,
            errors ?? []));

        if (errors is { Length: > 0 })
        {
            problemDetails.Extensions["errors"] = errors;
        }

        var objectResult = new ObjectResult(problemDetails)
        {
            StatusCode = statusCode
        };
        objectResult.ContentTypes.Add("application/problem+json");
        return objectResult;
    }
}
