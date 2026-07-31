namespace IIoT.EntityFrameworkCore.Uploads;

public sealed class UploadReceiveObservation
{
    private UploadReceiveObservation()
    {
    }

    public Guid Id { get; private init; }

    public Guid RegistrationId { get; private init; }

    public DateTimeOffset SeenAtUtc { get; private init; }

    public static UploadReceiveObservation Create(
        Guid observationId,
        Guid registrationId,
        DateTimeOffset seenAtUtc)
        => new()
        {
            Id = observationId,
            RegistrationId = registrationId,
            SeenAtUtc = seenAtUtc
        };
}
