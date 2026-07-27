using System.Text.Json;

namespace IIoT.IdentityService.Commands;

internal static class PermissionAuditSummary
{
    public static string Serialize(
        string action,
        IEnumerable<string>? beforePermissions,
        IEnumerable<string>? afterPermissions,
        IEnumerable<string>? requestedPermissions,
        string outcome,
        string? reasonCode = null,
        int rejectedPermissionCount = 0)
        => JsonSerializer.Serialize(new
        {
            action,
            beforePermissions = NormalizeForAudit(beforePermissions),
            afterPermissions = NormalizeForAudit(afterPermissions),
            requestedPermissions = NormalizeForAudit(requestedPermissions),
            rejectedPermissionCount,
            outcome,
            reasonCode
        });

    private static string[] NormalizeForAudit(IEnumerable<string>? permissions)
        => permissions?
            .Select(permission => permission?.Trim())
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
}
