using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations;

[AuthorizeRequirement("Device.Read")]
public sealed record GetPassStationTypesQuery
    : IHumanQuery<Result<IReadOnlyList<PassStationTypeDefinitionDto>>>;

public sealed class GetPassStationTypesHandler(IPassStationSchemaProvider schemaProvider)
    : IQueryHandler<GetPassStationTypesQuery, Result<IReadOnlyList<PassStationTypeDefinitionDto>>>
{
    public Task<Result<IReadOnlyList<PassStationTypeDefinitionDto>>> Handle(
        GetPassStationTypesQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(schemaProvider.GetAll()));
    }
}

[AuthorizeRequirement("Device.Read")]
public sealed record GetPassStationListByTypeQuery(
    PassStationQueryRequest Request
) : IHumanQuery<Result<PagedList<PassStationListItemDto>>>;

public sealed class GetPassStationListByTypeHandler(
    IPassStationSchemaProvider schemaProvider,
    IPassStationRecordQueryService queryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IProcessReadQueryService processReadQueryService)
    : IQueryHandler<GetPassStationListByTypeQuery, Result<PagedList<PassStationListItemDto>>>
{
    public async Task<Result<PagedList<PassStationListItemDto>>> Handle(
        GetPassStationListByTypeQuery request,
        CancellationToken cancellationToken)
    {
        var typeKey = PassStationQueryRuntime.NormalizeTypeKey(request.Request.TypeKey);
        var definition = schemaProvider.Find(typeKey);
        if (definition is null)
            return Result.NotFound($"过站类型 [{request.Request.TypeKey}] 不存在。");

        var normalizedRequest = request.Request with { TypeKey = definition.TypeKey };
        var validation = PassStationQueryRuntime.ValidateListRequest(normalizedRequest, definition);
        if (validation is not null)
            return validation;

        var allowedDeviceIds = await PassStationQueryRuntime.ResolveAllowedDeviceIdsAsync(
            normalizedRequest,
            currentUserDeviceAccessService,
            processReadQueryService,
            cancellationToken);
        if (!allowedDeviceIds.IsSuccess)
            return Result.Invalid(allowedDeviceIds.ErrorMessage!);
        if (allowedDeviceIds.ShouldReturnEmpty)
            return Result.Success(new PagedList<PassStationListItemDto>([], 0, normalizedRequest.Pagination));

        var (items, totalCount) = await queryService.GetByConditionAsync(
            normalizedRequest,
            allowedDeviceIds.DeviceIds,
            cancellationToken);

        return Result.Success(new PagedList<PassStationListItemDto>(items, totalCount, normalizedRequest.Pagination));
    }
}

[AuthorizeRequirement("Device.Read")]
public sealed record GetPassStationDetailByTypeQuery(
    string TypeKey,
    Guid Id
) : IHumanQuery<Result<PassStationDetailDto>>;

public sealed class GetPassStationDetailByTypeHandler(
    IPassStationSchemaProvider schemaProvider,
    IPassStationRecordQueryService queryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService)
    : IQueryHandler<GetPassStationDetailByTypeQuery, Result<PassStationDetailDto>>
{
    public async Task<Result<PassStationDetailDto>> Handle(
        GetPassStationDetailByTypeQuery request,
        CancellationToken cancellationToken)
    {
        var definition = schemaProvider.Find(request.TypeKey);
        if (definition is null)
            return Result.NotFound($"过站类型 [{request.TypeKey}] 不存在。");

        var detail = await queryService.GetDetailAsync(definition.TypeKey, request.Id, cancellationToken);
        if (detail is null)
            return Result.NotFound("未找到该过站记录。");

        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
            return Result.Invalid(scope.Errors?.ToArray() ?? ["用户凭证异常。"]);

        if (scope.Value is null || scope.Value.Contains(detail.DeviceId))
            return Result.Success(detail);

        return Result.Forbidden();
    }
}

internal sealed record AllowedDeviceResolution(
    IReadOnlyCollection<Guid>? DeviceIds,
    bool ShouldReturnEmpty,
    string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null;
}

internal static class PassStationQueryRuntime
{
    public static Result<PagedList<PassStationListItemDto>>? ValidateListRequest(
        PassStationQueryRequest request,
        PassStationTypeDefinitionDto definition)
    {
        if (!PassStationQueryModes.All.Contains(request.Mode))
            return Result.Invalid($"过站查询模式 [{request.Mode}] 无效。");

        if (!definition.SupportedModes.Contains(request.Mode))
            return Result.Invalid($"过站查询模式 [{request.Mode}] 不支持类型 [{request.TypeKey}]。");

        if (string.Equals(request.Mode, PassStationQueryModes.BarcodeProcess, StringComparison.Ordinal)
            && (request.ProcessId is null || string.IsNullOrWhiteSpace(request.Barcode)))
        {
            return Result.Invalid("查询模式 barcode-process 需要提供工序和条码。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.TimeProcess, StringComparison.Ordinal)
            && (request.ProcessId is null || request.StartTime is null || request.EndTime is null))
        {
            return Result.Invalid("查询模式 time-process 需要提供工序、开始时间和结束时间。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.DeviceBarcode, StringComparison.Ordinal)
            && (request.DeviceId is null || string.IsNullOrWhiteSpace(request.Barcode)))
        {
            return Result.Invalid("查询模式 device-barcode 需要提供设备和条码。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.DeviceTime, StringComparison.Ordinal)
            && (request.DeviceId is null || request.StartTime is null || request.EndTime is null))
        {
            return Result.Invalid("查询模式 device-time 需要提供设备、开始时间和结束时间。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.DeviceLatest, StringComparison.Ordinal)
            && request.DeviceId is null)
        {
            return Result.Invalid("查询模式 device-latest 需要提供设备。");
        }

        if (request.StartTime is not null && request.EndTime is not null && request.StartTime > request.EndTime)
            return Result.Invalid("开始时间不能晚于结束时间。");

        return null;
    }

    public static async Task<AllowedDeviceResolution> ResolveAllowedDeviceIdsAsync(
        PassStationQueryRequest request,
        ICurrentUserDeviceAccessService currentUserDeviceAccessService,
        IProcessReadQueryService processReadQueryService,
        CancellationToken cancellationToken)
    {
        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
            return new AllowedDeviceResolution(null, false, scope.Errors?.FirstOrDefault() ?? "Current user identity is invalid.");

        IReadOnlyCollection<Guid>? allowedDeviceIds = scope.Value;
        if (request.DeviceId.HasValue
            && allowedDeviceIds is not null
            && !allowedDeviceIds.Contains(request.DeviceId.Value))
        {
            return new AllowedDeviceResolution(null, false, "Unauthorized device access.");
        }

        if (request.ProcessId.HasValue)
        {
            var processDeviceIds = (await processReadQueryService.GetDeviceIdsAsync(
                request.ProcessId.Value,
                cancellationToken)).ToList();

            if (processDeviceIds.Count == 0)
                return new AllowedDeviceResolution(null, false, "The selected process does not have any devices.");

            if (allowedDeviceIds is not null)
            {
                processDeviceIds = processDeviceIds.Intersect(allowedDeviceIds).ToList();
                if (processDeviceIds.Count == 0)
                    return new AllowedDeviceResolution([], true, null);
            }

            return new AllowedDeviceResolution(processDeviceIds, false, null);
        }

        if (!request.DeviceId.HasValue && allowedDeviceIds is { Count: 0 })
            return new AllowedDeviceResolution([], true, null);

        return new AllowedDeviceResolution(allowedDeviceIds, false, null);
    }

    public static string NormalizeTypeKey(string rawTypeKey)
    {
        return rawTypeKey?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
