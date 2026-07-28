using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

[AuthorizeAiRead(AiReadPermissions.IdentityStatus)]
public sealed record GetCloudIdentityStatusQuery(
    Guid CloudUserId,
    string? TenantId) : IAiReadQuery<Result<CloudIdentityStatusDto>>;

public sealed class GetCloudIdentityStatusHandler(
    ICloudOidcUserProfileService profileService)
    : IQueryHandler<GetCloudIdentityStatusQuery, Result<CloudIdentityStatusDto>>
{
    public async Task<Result<CloudIdentityStatusDto>> Handle(
        GetCloudIdentityStatusQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = NormalizeTenantId(request.TenantId);
        if (!string.Equals(tenantId, CloudIdentityTenants.Default, StringComparison.Ordinal))
        {
            return Result.NotFound("Cloud identity tenant was not found.");
        }

        var profile = await profileService.GetByUserIdAsync(request.CloudUserId, cancellationToken);
        if (profile is null)
        {
            return Result.NotFound("Cloud identity was not found.");
        }

        if (string.IsNullOrWhiteSpace(profile.StatusVersion))
        {
            return Result.Failure("Cloud identity status version is unavailable.");
        }

        return Result.Success(
            new CloudIdentityStatusDto(
                profile.UserId,
                tenantId,
                profile.AccountEnabled,
                profile.EmployeeActive,
                profile.StatusVersion,
                DateTime.UtcNow));
    }

    private static string NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? CloudIdentityTenants.Default
            : tenantId.Trim();
    }
}
