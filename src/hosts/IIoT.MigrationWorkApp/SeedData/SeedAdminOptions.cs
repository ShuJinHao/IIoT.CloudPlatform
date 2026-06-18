using Microsoft.Extensions.Configuration;

namespace IIoT.MigrationWorkApp.SeedData;

public sealed record SeedAdminOptions(
    string EmployeeNo,
    string? Password,
    string RealName,
    bool ResetPassword)
{
    public const string EmployeeNoKey = "SEED_ADMIN_NO";
    public const string PasswordKey = "SEED_ADMIN_PASSWORD";
    public const string RealNameKey = "SEED_ADMIN_REAL_NAME";
    public const string ResetPasswordKey = "SEED_ADMIN_RESET_PASSWORD";

    public static SeedAdminOptions Load(IConfiguration configuration)
    {
        var employeeNo = configuration[EmployeeNoKey]?.Trim();
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{EmployeeNoKey}' for admin seeding.");
        }

        var password = configuration[PasswordKey];
        var realName = configuration[RealNameKey];

        return new SeedAdminOptions(
            employeeNo,
            string.IsNullOrWhiteSpace(password) ? null : password,
            string.IsNullOrWhiteSpace(realName) ? "\u7CFB\u7EDF\u7BA1\u7406\u5458" : realName.Trim(),
            IsEnabled(configuration[ResetPasswordKey]));
    }

    public string RequirePassword()
    {
        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{PasswordKey}' for admin seeding.");
        }

        return Password;
    }

    public static bool IsPasswordResetRequested(IConfiguration configuration)
    {
        return IsEnabled(configuration[ResetPasswordKey]);
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
