using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Events;

namespace IIoT.Infrastructure.Logging;

public static class IIoTRequestLoggingExtensions
{
    public static IApplicationBuilder UseIIoTSerilogRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms request_id={RequestId} trace_id={TraceId}";
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception is not null || httpContext.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode >= 400)
                {
                    return LogEventLevel.Warning;
                }

                return LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                var requestId = httpContext.Request.Headers[RequestLogHeaders.RequestId].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    requestId = httpContext.TraceIdentifier;
                }

                diagnosticContext.Set("RequestId", requestId ?? string.Empty);
                diagnosticContext.Set("TraceId", Activity.Current?.TraceId.ToString() ?? string.Empty);

                var routeSurface = httpContext.Request.Headers[RequestLogHeaders.RouteSurface].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(routeSurface))
                {
                    diagnosticContext.Set("RouteSurface", routeSurface);
                }
            };
        });
    }
}

public static class RequestLogHeaders
{
    public const string RequestId = "X-Request-Id";
    public const string RouteSurface = "X-IIoT-Route-Surface";
}
