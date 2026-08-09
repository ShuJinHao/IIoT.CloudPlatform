using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement(DevicePermissions.Create)]
[AdminOnly]
public sealed record GetAvailableDevicePluginSeriesQuery(Guid ProcessId)
    : IHumanQuery<Result<IReadOnlyList<AvailableDevicePluginSeriesItem>>>;

public sealed class GetAvailableDevicePluginSeriesHandler(
    IProcessReadQueryService processReadQueryService,
    IDevicePluginBindingQueryService bindingQueryService)
    : IQueryHandler<GetAvailableDevicePluginSeriesQuery,
        Result<IReadOnlyList<AvailableDevicePluginSeriesItem>>>
{
    public async Task<Result<IReadOnlyList<AvailableDevicePluginSeriesItem>>> Handle(
        GetAvailableDevicePluginSeriesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessId == Guid.Empty)
            return Result.Invalid("工序不能为空。");

        var process = (await processReadQueryService.GetByIdsAsync(
            [request.ProcessId],
            cancellationToken)).SingleOrDefault();
        if (process is null)
            return Result.NotFound("工序不存在。");

        return Result.Success(
            await bindingQueryService.GetAvailableAsync(
                process.ProcessCode,
                cancellationToken));
    }
}
