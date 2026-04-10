using IIoT.SharedKernel.Result; // 引用你精妙的底层 Result
using MediatR;
using Microsoft.AspNetCore.Mvc;
using IResult = IIoT.SharedKernel.Result.IResult;

namespace IIoT.HttpApi.Infrastructure;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    // 动态获取 ISender，保持子类干净
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
                return StatusCode(403);

            case ResultStatus.Unauthorized:
                return Unauthorized();

            default:
                return BadRequest();
        }
    }
}