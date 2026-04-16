using Microsoft.Extensions.Configuration;

namespace IIoT.MigrationWorkApp.SeedData;

public sealed record SeedAdminOptions(
    string EmployeeNo,
    string? Password,
    string RealName)
{
    public const string EmployeeNoKey = "SEED_ADMIN_NO";
    public const string PasswordKey = "SEED_ADMIN_PASSWORD";
    public const string RealNameKey = "SEED_ADMIN_REAL_NAME";

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
            string.IsNullOrWhiteSpace(realName) ? "\u7CFB\u7EDF\u7BA1\u7406\u5458" : realName.Trim());
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
}
