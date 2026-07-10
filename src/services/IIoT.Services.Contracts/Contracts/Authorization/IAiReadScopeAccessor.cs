namespace IIoT.Services.Contracts.Authorization;

public enum AiReadScopeKind
{
    Global,
    Delegated,
    Invalid
}

public interface IAiReadScopeAccessor
{
    string Caller { get; }

    AiReadScopeKind ScopeKind { get; }

    Guid? DelegatedUserId { get; }

    IReadOnlyCollection<Guid>? DelegatedDeviceIds { get; }
}
