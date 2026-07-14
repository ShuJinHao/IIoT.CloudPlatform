using System.Text.Json;
using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class ClientReleasePublishExceptionHandlerTests
{
    [Theory]
    [InlineData("conflict", StatusCodes.Status409Conflict, ClientReleasePublishConflictException.Code)]
    [InlineData("commit-unknown", StatusCodes.Status503ServiceUnavailable, ClientReleaseCommitUnknownException.Code)]
    [InlineData("unavailable", StatusCodes.Status503ServiceUnavailable, ClientReleasePublishUnavailableException.Code)]
    public async Task Handler_ShouldMapTypedPublishFailureToStableCodeWithoutRawDatabaseOrPath(
        string failure,
        int expectedStatus,
        string expectedCode)
    {
        const string sentinel = "/private/release/SECRET-db-driver-response";
        var exception = failure switch
        {
            "conflict" => (ClientReleasePublishException)new ClientReleasePublishConflictException(),
            "commit-unknown" => new ClientReleaseCommitUnknownException(),
            "unavailable" => new ClientReleasePublishUnavailableException(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
        exception.Data["raw-database-error"] = sentinel;
        exception.Source = sentinel;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/human/client-releases/plugin-package";
        context.Response.Body = new MemoryStream();
        var handler = new UseCaseExceptionHandler();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(exception.SafeMessage, root.GetProperty("detail").GetString());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.DoesNotContain(sentinel, root.GetRawText(), StringComparison.Ordinal);
    }
}
