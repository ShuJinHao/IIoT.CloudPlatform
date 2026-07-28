using System.Security.Cryptography;
using System.Text;
using IIoT.SharedKernel.Architecture;

namespace IIoT.Services.Contracts.Identity;

public interface ICloudOidcUserProfileService : IReadOnlyQueryPort
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
        uint employeeRowVersion,
        string? accountSecurityStamp)
    {
        var normalizedSecurityStamp = string.IsNullOrWhiteSpace(accountSecurityStamp)
            ? "legacy-uninitialized"
            : accountSecurityStamp;
        var source = FormattableString.Invariant(
            $"{cloudUserId:N}|{accountEnabled}|{employeeActive}|{employeeRowVersion}|{normalizedSecurityStamp}");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"v2:{Convert.ToHexString(digest)}";
    }
}
