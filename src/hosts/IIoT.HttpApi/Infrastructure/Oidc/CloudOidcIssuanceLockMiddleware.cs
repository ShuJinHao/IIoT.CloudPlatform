using System.IO.Pipelines;
using System.Security.Claims;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Infrastructure.Oidc;

public sealed class CloudOidcIssuanceLockMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IHumanSessionIssuanceLock issuanceLock)
    {
        if (string.Equals(
                context.Request.Path.Value,
                "/connect/token",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!await ExecuteBufferedAsync(
                    context,
                    operation => issuanceLock.TryExecuteTokenExchangeAsync(
                        operation,
                        context.RequestAborted)))
            {
                await WriteTokenExchangeCapacityRejectedAsync(context);
            }

            return;
        }

        if (!string.Equals(
                context.Request.Path.Value,
                "/connect/authorize",
                StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var authentication = await context.AuthenticateAsync(
            CloudOidcDefaults.SessionScheme);
        if (!Guid.TryParse(
                authentication.Principal?.FindFirstValue(
                    ClaimTypes.NameIdentifier),
                out var subjectId))
        {
            await next(context);
            return;
        }

        if (!await ExecuteBufferedAsync(
                context,
                operation => issuanceLock.TryExecuteAuthorizationAsync(
                    subjectId,
                    operation,
                    context.RequestAborted)))
        {
            await WriteCapacityRejectedAsync(
                context,
                "OIDC authorization 请求繁忙，请稍后重试。");
        }
    }

    private async Task<bool> ExecuteBufferedAsync(
        HttpContext context,
        Func<Func<Task>, Task<bool>> execute)
    {
        var originalFeature =
            context.Features.Get<IHttpResponseBodyFeature>()
            ?? throw new InvalidOperationException(
                "The HTTP response body feature is unavailable.");
        await using var bufferedFeature =
            new BufferedResponseBodyFeature();
        context.Features.Set<IHttpResponseBodyFeature>(bufferedFeature);
        try
        {
            var executed = await execute(
                async () =>
                {
                    await next(context);
                    if (context.Response.HasStarted)
                    {
                        throw new InvalidOperationException(
                            "OIDC response started before its database transaction committed.");
                    }
                });
            if (!executed)
            {
                return false;
            }

            context.Features.Set(originalFeature);
            await bufferedFeature.CopyToAsync(
                originalFeature.Stream,
                context.RequestAborted);
            return true;
        }
        finally
        {
            context.Features.Set(originalFeature);
        }
    }

    private sealed class BufferedResponseBodyFeature
        : IHttpResponseBodyFeature,
            IAsyncDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly PipeWriter _writer;

        public BufferedResponseBodyFeature()
        {
            _writer = PipeWriter.Create(
                _stream,
                new StreamPipeWriterOptions(leaveOpen: true));
        }

        public Stream Stream => _stream;

        public PipeWriter Writer => _writer;

        public void DisableBuffering()
        {
        }

        public Task StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task SendFileAsync(
            string path,
            long offset,
            long? count,
            CancellationToken cancellationToken = default)
        {
            await using var source = File.OpenRead(path);
            source.Position = offset;
            if (count is null)
            {
                await source.CopyToAsync(_stream, cancellationToken);
                return;
            }

            var buffer = new byte[81920];
            var remaining = count.Value;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(
                        0,
                        (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await _stream.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
                remaining -= read;
            }
        }

        public async Task CompleteAsync()
        {
            await _writer.FlushAsync();
        }

        public async Task CopyToAsync(
            Stream destination,
            CancellationToken cancellationToken)
        {
            await _writer.FlushAsync(cancellationToken);
            _stream.Position = 0;
            await _stream.CopyToAsync(destination, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.CompleteAsync();
            await _stream.DisposeAsync();
        }
    }

    private static async Task WriteTokenExchangeCapacityRejectedAsync(
        HttpContext context)
        => await WriteCapacityRejectedAsync(
            context,
            "OIDC token 交换繁忙，请稍后重试。");

    private static async Task WriteCapacityRejectedAsync(
        HttpContext context,
        string detail)
    {
        context.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = "1";
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "请求过于频繁",
                Type =
                    "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/429",
                Detail = detail
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
