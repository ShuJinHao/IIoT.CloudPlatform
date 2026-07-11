using System.Text.Json;
using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IIoT.EndToEndTests;

public sealed class DistributedLockExceptionHandlerTests
{
    [Theory]
    [InlineData("conflict", StatusCodes.Status409Conflict, DistributedLockConflictException.PublicMessage)]
    [InlineData("unavailable", StatusCodes.Status503ServiceUnavailable, DistributedLockUnavailableException.PublicMessage)]
    [InlineData("ownership-lost", StatusCodes.Status503ServiceUnavailable, DistributedLockOwnershipLostException.PublicMessage)]
    public async Task Handler_ShouldReturnStableLockFailureWithoutSensitiveDetails(
        string failure,
        int expectedStatus,
        string expectedDetail)
    {
        var exception = failure switch
        {
            "conflict" => (Exception)new DistributedLockConflictException(),
            "unavailable" => new DistributedLockUnavailableException(),
            "ownership-lost" => new DistributedLockOwnershipLostException(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/human/processes";
        context.Response.Body = new MemoryStream();
        var handler = new UseCaseExceptionHandler();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedDetail, root.GetProperty("detail").GetString());
        var payload = root.GetRawText();
        Assert.DoesNotContain("iiot:lock", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redis", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", payload, StringComparison.OrdinalIgnoreCase);
    }
}
