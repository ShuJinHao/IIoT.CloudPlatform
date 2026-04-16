namespace IIoT.HttpApi;

public static class DesignTimeConnectionStringResolver
{
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__iiot-db";

    public static string Resolve(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var connectionString = getEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required environment variable '{ConnectionStringEnvironmentVariable}' for design-time DbContext creation.");
        }

        return connectionString;
    }
}
