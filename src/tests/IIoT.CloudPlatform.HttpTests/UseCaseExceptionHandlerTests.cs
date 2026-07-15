using System.Text.Json;
using IIoT.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class UseCaseExceptionHandlerTests
{
    [Theory]
    [InlineData("timeout", StatusCodes.Status409Conflict, "请求冲突", "operation timed out")]
    [InlineData("argument", StatusCodes.Status400BadRequest, "请求参数错误", "invalid argument")]
    [InlineData("invalid-operation", StatusCodes.Status400BadRequest, "请求参数错误", "invalid state")]
    [InlineData("unknown", StatusCodes.Status500InternalServerError, "服务器内部错误", "服务器处理请求时发生未预期错误。")]
    public async Task Handler_ShouldMapKnownAndUnknownRuntimeExceptions(
        string exceptionKind,
        int expectedStatus,
        string expectedTitle,
        string expectedDetail)
    {
        var exception = exceptionKind switch
        {
            "timeout" => (Exception)new TimeoutException("operation timed out"),
            "argument" => new ArgumentException("invalid argument"),
            "invalid-operation" => new InvalidOperationException("invalid state"),
            "unknown" => new ApplicationException("sensitive raw failure"),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionKind), exceptionKind, null)
        };
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/human/devices";
        context.Response.Body = new MemoryStream();
        var handler = new UseCaseExceptionHandler();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var problem = document.RootElement;
        Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, problem.GetProperty("title").GetString());
        Assert.Equal(expectedDetail, problem.GetProperty("detail").GetString());
        if (exceptionKind == "unknown")
            Assert.DoesNotContain("sensitive raw failure", problem.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_CallerCancellation_ShouldRemainUnhandled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var expected = new OperationCanceledException("caller cancelled", cancellation.Token);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/human/devices";
        context.Response.Body = new MemoryStream();

        var handled = await new UseCaseExceptionHandler()
            .TryHandleAsync(context, expected, cancellation.Token);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }
}
