using IIoT.Services.Contracts.AiRead;

namespace IIoT.Services.Contracts.Identity;

public sealed record CloudIdentityStatusDto(
    Guid CloudUserId,
    string TenantId,
    bool AccountEnabled,
    bool EmployeeActive,
    string StatusVersion,
    DateTime IssuedAtUtc) : IAiReadResponseMetadata
{
    DateTimeOffset IAiReadResponseMetadata.AsOfUtc => IssuedAtUtc;

    string IAiReadResponseMetadata.Source => "cloud-identity-status";

    string IAiReadResponseMetadata.QueryScope => $"{TenantId}:{CloudUserId:N}";

    int IAiReadResponseMetadata.RowCount => 1;

    bool IAiReadResponseMetadata.Truncated => false;

    string? IAiReadResponseMetadata.NextCursor => null;
}
