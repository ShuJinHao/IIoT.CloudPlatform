namespace IIoT.DataWorker.Consumers;

internal static class EventSchemaVersionGuard
{
    public const int CurrentSchemaVersion = 1;

    public static void EnsureSupported(int schemaVersion, string eventName)
    {
        if (schemaVersion is 0 or CurrentSchemaVersion)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{eventName} schema version {schemaVersion} is not supported.");
    }
}
