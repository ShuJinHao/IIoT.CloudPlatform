namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseAuditActor
{
    public static Guid? ParseId(string? value)
        => Guid.TryParse(value, out var actorUserId) ? actorUserId : null;
}
