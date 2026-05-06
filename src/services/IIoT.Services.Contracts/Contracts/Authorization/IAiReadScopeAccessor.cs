namespace IIoT.Services.Contracts.Authorization;

public interface IAiReadScopeAccessor
{
    string Caller { get; }

    Guid? DelegatedUserId { get; }

    IReadOnlyCollection<Guid>? DelegatedDeviceIds { get; }
}
