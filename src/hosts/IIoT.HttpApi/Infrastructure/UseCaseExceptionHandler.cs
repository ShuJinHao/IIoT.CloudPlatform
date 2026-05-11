using IIoT.Services.CrossCutting.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IIoT.HttpApi.Infrastructure;

public sealed class UseCaseExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ForbiddenException forbidden => CreateProblem(
                StatusCodes.Status403Forbidden,
                "禁止访问",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/403",
                forbidden.Message,
                httpContext.Request.Path),
            TimeoutException timeout => CreateProblem(
                StatusCodes.Status409Conflict,
                "请求冲突",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/409",
                timeout.Message,
                httpContext.Request.Path),
            DbUpdateConcurrencyException => CreateProblem(
                StatusCodes.Status409Conflict,
                "请求冲突",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/409",
                "资源已被其他请求修改，请刷新后重试。",
                httpContext.Request.Path),
            ArgumentException argument => CreateProblem(
                StatusCodes.Status400BadRequest,
                "请求参数错误",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/400",
                argument.Message,
                httpContext.Request.Path),
            InvalidOperationException invalidOperation => CreateProblem(
                StatusCodes.Status400BadRequest,
                "请求参数错误",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/400",
                invalidOperation.Message,
                httpContext.Request.Path),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } tooLarge => CreateProblem(
                StatusCodes.Status413PayloadTooLarge,
                "请求体过大",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/413",
                tooLarge.Message,
                httpContext.Request.Path),
            _ => CreateProblem(
                StatusCodes.Status500InternalServerError,
                "服务器内部错误",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/500",
                "服务器处理请求时发生未预期错误。",
                httpContext.Request.Path)
        };

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ProblemDetails CreateProblem(
        int status,
        string title,
        string type,
        string detail,
        PathString path)
    {
        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = detail
        }.AddCode(CloudProblemCodes.Resolve(status, path, [detail]));
    }
}
