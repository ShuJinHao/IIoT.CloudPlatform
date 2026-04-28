using IIoT.SharedKernel.Configuration;

namespace IIoT.HttpApi;

public static class DesignTimeConnectionStringResolver
{
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__" + ConnectionResourceNames.IiotDatabase;

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
