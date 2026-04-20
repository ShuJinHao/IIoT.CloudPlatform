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
                "Forbidden",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/403",
                forbidden.Message),
            TimeoutException timeout => CreateProblem(
                StatusCodes.Status409Conflict,
                "Conflict",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/409",
                timeout.Message),
            DbUpdateConcurrencyException => CreateProblem(
                StatusCodes.Status409Conflict,
                "Conflict",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/409",
                "The resource was modified by another request. Please refresh and retry."),
            ArgumentException argument => CreateProblem(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/400",
                argument.Message),
            InvalidOperationException invalidOperation => CreateProblem(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/400",
                invalidOperation.Message),
            _ => CreateProblem(
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/500",
                "The server encountered an unexpected error while processing the request.")
        };

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ProblemDetails CreateProblem(
        int status,
        string title,
        string type,
        string detail)
    {
        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = detail
        };
    }
}
