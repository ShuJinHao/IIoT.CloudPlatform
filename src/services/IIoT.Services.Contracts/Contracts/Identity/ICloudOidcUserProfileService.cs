namespace IIoT.Services.Contracts.Identity;

public interface ICloudOidcUserProfileService
{
    Task<CloudOidcUserProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<CloudOidcUserProfile?> GetByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken = default);
}

public sealed record CloudOidcUserProfile(
    Guid UserId,
    string EmployeeNo,
    string RealName,
    bool AccountEnabled,
    bool EmployeeActive,
    string? TenantId = null,
    string? StatusVersion = null);

public static class CloudIdentityTenants
{
    public const string Default = "default";
}

public static class CloudIdentityStatusVersions
{
    public static string Create(
        Guid cloudUserId,
        bool accountEnabled,
        bool employeeActive,
        uint employeeRowVersion)
    {
        var accountState = accountEnabled ? "enabled" : "disabled";
        var employeeState = employeeActive ? "active" : "inactive";
        return FormattableString.Invariant(
            $"v1:{cloudUserId:N}:{accountState}:{employeeState}:{employeeRowVersion}");
    }
}
