using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Devices.ValueObjects;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Core.Production.Specifications.EdgeHosts;
using IIoT.ProductionService.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.EdgeHosts;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record CreateEdgeHostCommand(
    Guid DeviceId,
    string ClientCode,
    string HostName,
    string? Remark = null) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record UpdateEdgeHostCommand(
    Guid EdgeHostId,
    string HostName,
    string? Remark = null) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record EnableEdgeHostCommand(Guid EdgeHostId) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record DisableEdgeHostCommand(Guid EdgeHostId) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record DeleteEdgeHostCommand(Guid EdgeHostId) : IHumanCommand<Result>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record AddEdgeHostPlcBindingCommand(
    Guid EdgeHostId,
    string PlcCode,
    string PlcName,
    Guid? ProcessId = null,
    Guid? BusinessDeviceId = null,
    string? StationCode = null,
    string? Protocol = null,
    string? Address = null,
    int DisplayOrder = 0,
    string? Remark = null,
    bool Enabled = true) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record UpdateEdgeHostPlcBindingCommand(
    Guid EdgeHostId,
    Guid BindingId,
    string PlcName,
    Guid? ProcessId = null,
    Guid? BusinessDeviceId = null,
    string? StationCode = null,
    string? Protocol = null,
    string? Address = null,
    int DisplayOrder = 0,
    string? Remark = null) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record EnableEdgeHostPlcBindingCommand(
    Guid EdgeHostId,
    Guid BindingId) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record DisableEdgeHostPlcBindingCommand(
    Guid EdgeHostId,
    Guid BindingId) : IHumanCommand<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Manage)]
public sealed record RemoveEdgeHostPlcBindingCommand(
    Guid EdgeHostId,
    Guid BindingId) : IHumanCommand<Result<EdgeHostDto>>;

public sealed class CreateEdgeHostHandler(
    IRepository<EdgeHost> edgeHostRepository,
    IReadRepository<Device> deviceRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<CreateEdgeHostCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        CreateEdgeHostCommand request,
        CancellationToken cancellationToken)
    {
        var targetKey = $"{request.DeviceId}:{request.ClientCode?.Trim()}";
        var validation = await ValidateCreateAsync(request, cancellationToken);
        if (!validation.IsSuccess)
        {
            await WriteAuditAsync(
                EdgeHostAudit.Create,
                targetKey,
                false,
                $"创建上位机 {request.HostName?.Trim()}（{request.ClientCode?.Trim()}）。",
                validation.Errors?.FirstOrDefault(),
                cancellationToken);
            return Result.From(validation);
        }

        var device = validation.Value!.Device;
        var clientCode = validation.Value.ClientCode;
        EdgeHost host;
        try
        {
            host = new EdgeHost(device.Id, clientCode, request.HostName, request.Remark);
        }
        catch (ArgumentException ex)
        {
            await WriteAuditAsync(
                EdgeHostAudit.Create,
                targetKey,
                false,
                $"创建上位机 {request.HostName?.Trim()}（{clientCode}）。",
                ex.Message,
                cancellationToken);
            return Result.Invalid(ex.Message);
        }

        edgeHostRepository.Add(host);
        var affected = await edgeHostRepository.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(
            EdgeHostAudit.Create,
            host.Id.ToString(),
            affected > 0,
            $"创建上位机 {host.HostName}（{host.ClientCode}），绑定设备 {host.DeviceId}。",
            affected > 0 ? null : "保存上位机失败。",
            cancellationToken);

        return Result.Success(EdgeHostMapping.ToDto(host));
    }

    private async Task<Result<CreateValidation>> ValidateCreateAsync(
        CreateEdgeHostCommand request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
        {
            return Result.Invalid("上位机绑定设备不能为空。");
        }

        var clientCodeResult = EdgeHostCommandHelper.NormalizeClientCode(request.ClientCode);
        if (!clientCodeResult.IsSuccess)
        {
            return Result.From(clientCodeResult);
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);
        if (device is null)
        {
            return Result.NotFound("上位机绑定设备不存在。");
        }

        if (!string.Equals(device.Code, clientCodeResult.Value, StringComparison.Ordinal))
        {
            return Result.Invalid("ClientCode 与绑定设备的寻址码不一致。");
        }

        if (await edgeHostRepository.AnyAsync(host => host.DeviceId == request.DeviceId, cancellationToken))
        {
            return Result.Invalid("该设备已绑定上位机。");
        }

        if (await edgeHostRepository.AnyAsync(host => host.ClientCode == clientCodeResult.Value, cancellationToken))
        {
            return Result.Invalid("该 ClientCode 已绑定上位机。");
        }

        return Result.Success(new CreateValidation(device, clientCodeResult.Value!));
    }

    private Task WriteAuditAsync(
        string operationType,
        string targetIdOrKey,
        bool succeeded,
        string summary,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        return auditTrailService.TryWriteAsync(
            EdgeHostAudit.Entry(currentUser, operationType, targetIdOrKey, succeeded, summary, failureReason),
            cancellationToken);
    }

    private sealed record CreateValidation(Device Device, string ClientCode);
}

public sealed class UpdateEdgeHostHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<UpdateEdgeHostCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        UpdateEdgeHostCommand request,
        CancellationToken cancellationToken)
    {
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(request.EdgeHostId),
            cancellationToken);
        if (host is null)
        {
            await WriteAuditAsync(
                EdgeHostAudit.Update,
                request.EdgeHostId.ToString(),
                false,
                $"修改上位机 {request.EdgeHostId}。",
                "上位机不存在。",
                cancellationToken);
            return Result.NotFound("上位机不存在。");
        }

        try
        {
            host.Rename(request.HostName);
            host.UpdateRemark(request.Remark);
        }
        catch (ArgumentException ex)
        {
            await WriteAuditAsync(
                EdgeHostAudit.Update,
                host.Id.ToString(),
                false,
                $"修改上位机 {host.ClientCode}。",
                ex.Message,
                cancellationToken);
            return Result.Invalid(ex.Message);
        }

        edgeHostRepository.Update(host);
        var affected = await edgeHostRepository.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(
            EdgeHostAudit.Update,
            host.Id.ToString(),
            affected > 0,
            $"修改上位机 {host.HostName}（{host.ClientCode}）。",
            affected > 0 ? null : "保存上位机失败。",
            cancellationToken);

        return Result.Success(EdgeHostMapping.ToDto(host));
    }

    private Task WriteAuditAsync(
        string operationType,
        string targetIdOrKey,
        bool succeeded,
        string summary,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        return auditTrailService.TryWriteAsync(
            EdgeHostAudit.Entry(currentUser, operationType, targetIdOrKey, succeeded, summary, failureReason),
            cancellationToken);
    }
}

public sealed class EnableEdgeHostHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<EnableEdgeHostCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(EnableEdgeHostCommand request, CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangeHostEnabledAsync(
            edgeHostRepository,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            enabled: true,
            cancellationToken);
    }
}

public sealed class DisableEdgeHostHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<DisableEdgeHostCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(DisableEdgeHostCommand request, CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangeHostEnabledAsync(
            edgeHostRepository,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            enabled: false,
            cancellationToken);
    }
}

public sealed class DeleteEdgeHostHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<DeleteEdgeHostCommand, Result>
{
    public async Task<Result> Handle(DeleteEdgeHostCommand request, CancellationToken cancellationToken)
    {
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(request.EdgeHostId),
            cancellationToken);
        if (host is null)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(
                    currentUser,
                    EdgeHostAudit.Delete,
                    request.EdgeHostId.ToString(),
                    false,
                    $"删除上位机 {request.EdgeHostId}。",
                    "上位机不存在。"),
                cancellationToken);
            return Result.NotFound("上位机不存在。");
        }

        edgeHostRepository.Delete(host);
        var affected = await edgeHostRepository.SaveChangesAsync(cancellationToken);
        await auditTrailService.TryWriteAsync(
            EdgeHostAudit.Entry(
                currentUser,
                EdgeHostAudit.Delete,
                host.Id.ToString(),
                affected > 0,
                $"删除上位机 {host.HostName}（{host.ClientCode}），同时删除 {host.PlcBindings.Count} 个 PLC 绑定配置。",
                affected > 0 ? null : "删除上位机失败。"),
            cancellationToken);

        return Result.Success();
    }
}

public sealed class AddEdgeHostPlcBindingHandler(
    IRepository<EdgeHost> edgeHostRepository,
    IDeviceReadQueryService deviceReadQueryService,
    IProcessReadQueryService processReadQueryService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<AddEdgeHostPlcBindingCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        AddEdgeHostPlcBindingCommand request,
        CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangePlcBindingAsync(
            edgeHostRepository,
            deviceReadQueryService,
            processReadQueryService,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            request.PlcCode,
            EdgeHostAudit.PlcBindingAdd,
            $"新增 PLC 绑定 {request.PlcCode?.Trim()}。",
            host => host.AddPlcBinding(
                request.PlcCode ?? string.Empty,
                request.PlcName,
                request.ProcessId,
                request.BusinessDeviceId,
                request.StationCode,
                request.Protocol,
                request.Address,
                request.DisplayOrder,
                request.Remark,
                request.Enabled),
            request.ProcessId,
            request.BusinessDeviceId,
            cancellationToken);
    }
}

public sealed class UpdateEdgeHostPlcBindingHandler(
    IRepository<EdgeHost> edgeHostRepository,
    IDeviceReadQueryService deviceReadQueryService,
    IProcessReadQueryService processReadQueryService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<UpdateEdgeHostPlcBindingCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        UpdateEdgeHostPlcBindingCommand request,
        CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangePlcBindingAsync(
            edgeHostRepository,
            deviceReadQueryService,
            processReadQueryService,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            request.BindingId.ToString(),
            EdgeHostAudit.PlcBindingUpdate,
            $"修改 PLC 绑定 {request.BindingId}。",
            host =>
            {
                if (host.FindPlcBinding(request.BindingId) is null)
                {
                    throw new KeyNotFoundException("PLC 绑定不存在。");
                }

                host.UpdatePlcBinding(
                    request.BindingId,
                    request.PlcName,
                    request.ProcessId,
                    request.BusinessDeviceId,
                    request.StationCode,
                    request.Protocol,
                    request.Address,
                    request.DisplayOrder,
                    request.Remark);
            },
            request.ProcessId,
            request.BusinessDeviceId,
            cancellationToken);
    }
}

public sealed class EnableEdgeHostPlcBindingHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<EnableEdgeHostPlcBindingCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        EnableEdgeHostPlcBindingCommand request,
        CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangeExistingPlcBindingAsync(
            edgeHostRepository,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            request.BindingId,
            EdgeHostAudit.PlcBindingEnable,
            host => host.EnablePlcBinding(request.BindingId),
            cancellationToken);
    }
}

public sealed class DisableEdgeHostPlcBindingHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<DisableEdgeHostPlcBindingCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        DisableEdgeHostPlcBindingCommand request,
        CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangeExistingPlcBindingAsync(
            edgeHostRepository,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            request.BindingId,
            EdgeHostAudit.PlcBindingDisable,
            host => host.DisablePlcBinding(request.BindingId),
            cancellationToken);
    }
}

public sealed class RemoveEdgeHostPlcBindingHandler(
    IRepository<EdgeHost> edgeHostRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<RemoveEdgeHostPlcBindingCommand, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        RemoveEdgeHostPlcBindingCommand request,
        CancellationToken cancellationToken)
    {
        return await EdgeHostCommandHelper.ChangeExistingPlcBindingAsync(
            edgeHostRepository,
            currentUser,
            auditTrailService,
            request.EdgeHostId,
            request.BindingId,
            EdgeHostAudit.PlcBindingRemove,
            host => host.RemovePlcBinding(request.BindingId),
            cancellationToken);
    }
}

internal static class EdgeHostCommandHelper
{
    public static Result<string> NormalizeClientCode(string clientCode)
    {
        try
        {
            return Result.Success(DeviceCode.From(clientCode).Value);
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(ex.Message);
        }
    }

    public static async Task<Result<EdgeHostDto>> ChangeHostEnabledAsync(
        IRepository<EdgeHost> edgeHostRepository,
        ICurrentUser currentUser,
        IAuditTrailService auditTrailService,
        Guid edgeHostId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var operation = enabled ? EdgeHostAudit.Enable : EdgeHostAudit.Disable;
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(edgeHostId),
            cancellationToken);
        if (host is null)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(
                    currentUser,
                    operation,
                    edgeHostId.ToString(),
                    false,
                    $"{(enabled ? "启用" : "禁用")}上位机 {edgeHostId}。",
                    "上位机不存在。"),
                cancellationToken);
            return Result.NotFound("上位机不存在。");
        }

        if (enabled)
        {
            host.Enable();
        }
        else
        {
            host.Disable();
        }

        edgeHostRepository.Update(host);
        var affected = await edgeHostRepository.SaveChangesAsync(cancellationToken);
        await auditTrailService.TryWriteAsync(
            EdgeHostAudit.Entry(
                currentUser,
                operation,
                host.Id.ToString(),
                affected > 0,
                $"{(enabled ? "启用" : "禁用")}上位机 {host.HostName}（{host.ClientCode}）。",
                affected > 0 ? null : "保存上位机状态失败。"),
            cancellationToken);

        return Result.Success(EdgeHostMapping.ToDto(host));
    }

    public static async Task<Result<EdgeHostDto>> ChangePlcBindingAsync(
        IRepository<EdgeHost> edgeHostRepository,
        IDeviceReadQueryService deviceReadQueryService,
        IProcessReadQueryService processReadQueryService,
        ICurrentUser currentUser,
        IAuditTrailService auditTrailService,
        Guid edgeHostId,
        string targetIdOrKey,
        string operation,
        string summary,
        Action<EdgeHost> apply,
        Guid? processId,
        Guid? businessDeviceId,
        CancellationToken cancellationToken)
    {
        var relationValidation = await ValidateBindingRelationsAsync(
            deviceReadQueryService,
            processReadQueryService,
            processId,
            businessDeviceId,
            cancellationToken);
        if (!relationValidation.IsSuccess)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(
                    currentUser,
                    operation,
                    targetIdOrKey,
                    false,
                    summary,
                    relationValidation.Errors?.FirstOrDefault()),
                cancellationToken);
            return relationValidation;
        }

        return await ChangeExistingPlcBindingAsync(
            edgeHostRepository,
            currentUser,
            auditTrailService,
            edgeHostId,
            null,
            operation,
            apply,
            cancellationToken,
            targetIdOrKey,
            summary);
    }

    public static async Task<Result<EdgeHostDto>> ChangeExistingPlcBindingAsync(
        IRepository<EdgeHost> edgeHostRepository,
        ICurrentUser currentUser,
        IAuditTrailService auditTrailService,
        Guid edgeHostId,
        Guid? bindingId,
        string operation,
        Action<EdgeHost> apply,
        CancellationToken cancellationToken,
        string? targetIdOrKeyOverride = null,
        string? summaryOverride = null)
    {
        var targetIdOrKey = targetIdOrKeyOverride ?? $"{edgeHostId}:{bindingId}";
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(edgeHostId),
            cancellationToken);
        if (host is null)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(
                    currentUser,
                    operation,
                    targetIdOrKey,
                    false,
                    summaryOverride ?? $"维护上位机 {edgeHostId} 的 PLC 绑定 {bindingId}。",
                    "上位机不存在。"),
                cancellationToken);
            return Result.NotFound("上位机不存在。");
        }

        if (bindingId.HasValue && host.FindPlcBinding(bindingId.Value) is null)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(
                    currentUser,
                    operation,
                    targetIdOrKey,
                    false,
                    $"维护上位机 {host.ClientCode} 的 PLC 绑定 {bindingId}。",
                    "PLC 绑定不存在。"),
                cancellationToken);
            return Result.NotFound("PLC 绑定不存在。");
        }

        try
        {
            apply(host);
        }
        catch (KeyNotFoundException ex)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(currentUser, operation, targetIdOrKey, false, summaryOverride ?? ex.Message, ex.Message),
                cancellationToken);
            return Result.NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(currentUser, operation, targetIdOrKey, false, summaryOverride ?? ex.Message, ex.Message),
                cancellationToken);
            return Result.Invalid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            await auditTrailService.TryWriteAsync(
                EdgeHostAudit.Entry(currentUser, operation, targetIdOrKey, false, summaryOverride ?? ex.Message, ex.Message),
                cancellationToken);
            return Result.Invalid(ex.Message);
        }

        edgeHostRepository.Update(host);
        var affected = await edgeHostRepository.SaveChangesAsync(cancellationToken);
        await auditTrailService.TryWriteAsync(
            EdgeHostAudit.Entry(
                currentUser,
                operation,
                targetIdOrKey,
                affected > 0,
                summaryOverride ?? $"维护上位机 {host.ClientCode} 的 PLC 绑定。",
                affected > 0 ? null : "保存 PLC 绑定失败。"),
            cancellationToken);

        return Result.Success(EdgeHostMapping.ToDto(host));
    }

    private static async Task<Result> ValidateBindingRelationsAsync(
        IDeviceReadQueryService deviceReadQueryService,
        IProcessReadQueryService processReadQueryService,
        Guid? processId,
        Guid? businessDeviceId,
        CancellationToken cancellationToken)
    {
        if (processId == Guid.Empty)
        {
            return Result.Invalid("PLC 绑定工序不能为空 Guid。");
        }

        if (businessDeviceId == Guid.Empty)
        {
            return Result.Invalid("PLC 绑定业务设备不能为空 Guid。");
        }

        if (processId.HasValue
            && !await processReadQueryService.ExistsAsync(processId.Value, cancellationToken))
        {
            return Result.NotFound("PLC 绑定工序不存在。");
        }

        if (businessDeviceId.HasValue
            && !await deviceReadQueryService.ExistsAsync(businessDeviceId.Value, cancellationToken))
        {
            return Result.NotFound("PLC 绑定业务设备不存在。");
        }

        if (processId.HasValue
            && businessDeviceId.HasValue
            && !await deviceReadQueryService.ExistsInProcessAsync(
                businessDeviceId.Value,
                processId.Value,
                cancellationToken))
        {
            return Result.Invalid("PLC 绑定业务设备不属于指定工序。");
        }

        return Result.Success();
    }
}
