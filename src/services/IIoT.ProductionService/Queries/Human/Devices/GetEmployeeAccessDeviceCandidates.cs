using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

public sealed record EmployeeAccessDeviceCandidateDto(
    Guid Id,
    string DeviceName);

[AuthorizeRequirement(CloudPermissionCatalog.Employee.UpdateAccess)]
public sealed record GetEmployeeAccessDeviceCandidatesQuery()
    : IHumanQuery<Result<List<EmployeeAccessDeviceCandidateDto>>>;

public sealed class GetEmployeeAccessDeviceCandidatesHandler(
    IReadRepository<Device> deviceRepository)
    : IQueryHandler<
        GetEmployeeAccessDeviceCandidatesQuery,
        Result<List<EmployeeAccessDeviceCandidateDto>>>
{
    public async Task<Result<List<EmployeeAccessDeviceCandidateDto>>> Handle(
        GetEmployeeAccessDeviceCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var devices = await deviceRepository.GetListAsync(
            cancellationToken: cancellationToken);

        var candidates = devices
            .OrderBy(device => device.DeviceName, StringComparer.Ordinal)
            .ThenBy(device => device.Id)
            .Select(device => new EmployeeAccessDeviceCandidateDto(
                device.Id,
                device.DeviceName))
            .ToList();

        return Result.Success(candidates);
    }
}
