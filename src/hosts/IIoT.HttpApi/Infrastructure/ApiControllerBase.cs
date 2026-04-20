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
                return result.Errors is null ? BadRequest() : BadRequest(new { errors = result.Errors });

            case ResultStatus.NotFound:
                return result.Errors is null ? NotFound() : NotFound(new { errors = result.Errors });

            case ResultStatus.Invalid:
                return result.Errors is null ? BadRequest() : BadRequest(new { errors = result.Errors });

            case ResultStatus.Forbidden:
                return StatusCode(403, result.Errors is null ? null : new { errors = result.Errors });

            case ResultStatus.Unauthorized:
                return Unauthorized(result.Errors is null ? null : new { errors = result.Errors });

            default:
                return BadRequest();
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
}
