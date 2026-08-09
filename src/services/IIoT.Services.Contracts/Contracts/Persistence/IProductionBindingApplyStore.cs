using IIoT.Services.Contracts.Auditing;

namespace IIoT.Services.Contracts.Persistence;

public sealed record ProductionBindingApplyRequest(
    Guid BindingId,
    Guid DeviceId,
    Guid ComponentId,
    string ProcessType,
    DateTime BoundAtUtc,
    Guid AuditRecordId,
    AuditTrailEntry AuditEntry);

public enum ProductionBindingApplyResult
{
    Applied,
    Idempotent,
    Conflict,
    Unknown
}

public interface IProductionBindingApplyStore
{
    Task<ProductionBindingApplyResult> TryApplyAsync(
        ProductionBindingApplyRequest request,
        CancellationToken cancellationToken = default);
}
