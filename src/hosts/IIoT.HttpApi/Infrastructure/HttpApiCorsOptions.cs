namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpApiCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "iiot-httpapi";

    public string[] AllowedOrigins { get; set; } = [];

    public void Validate()
    {
        if (AllowedOrigins.Any(origin => string.Equals(origin, "*", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Wildcard origins are not allowed for HttpApi CORS.");
        }
    }
}
