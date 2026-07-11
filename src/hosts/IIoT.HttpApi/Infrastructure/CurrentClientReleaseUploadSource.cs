using IIoT.ProductionService.ClientReleases;

namespace IIoT.HttpApi.Infrastructure;

public sealed class CurrentClientReleaseUploadSource(
    IHttpContextAccessor contextAccessor) : IClientReleaseUploadSource
{
    public long? DeclaredLength => CurrentContext.Request.ContentLength;

    public string? AuditSource => CurrentContext.Connection.RemoteIpAddress?.ToString();

    public ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
        => CurrentContext.Request.Body.ReadAsync(buffer, cancellationToken);

    private HttpContext CurrentContext => contextAccessor.HttpContext
        ?? throw new InvalidOperationException("当前客户端发布上传来源不可用。");
}
