namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleasePublishLock
{
    public const string Resource = "iiot:lock:client-release:publish";

    public const int AcquireTimeoutSeconds = 5;
}
