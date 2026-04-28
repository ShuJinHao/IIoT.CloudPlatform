using Microsoft.AspNetCore.Http;

namespace IIoT.HttpApi.Infrastructure;

public static class RateLimitPartitionKeyResolver
{
    public static string ResolveClientPartitionKey(
        HttpContext context,
        string anonymousFallback)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? anonymousFallback;
    }

    public static string ResolveEdgeUploadPartitionKey(HttpContext context)
    {
        return context.User.FindFirst(IIoT.Services.Contracts.Identity.IIoTClaimTypes.DeviceId)?.Value
               ?? ResolveClientPartitionKey(context, "edge-anonymous");
    }
}
