namespace IIoT.Services.Contracts.Uploads;

public sealed record EdgeUploadAcceptedResponse(
    string Code,
    bool DuplicateAccepted,
    Guid? OutboxMessageId)
{
    public static EdgeUploadAcceptedResponse Accepted(Guid? outboxMessageId)
    {
        return new EdgeUploadAcceptedResponse("accepted", false, outboxMessageId);
    }

    public static EdgeUploadAcceptedResponse Duplicate(Guid? outboxMessageId)
    {
        return new EdgeUploadAcceptedResponse("duplicate_accepted", true, outboxMessageId);
    }
}
