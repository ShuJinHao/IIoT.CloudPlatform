using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

public record PermissionGroupDto(
    string GroupName,
    List<string> Permissions
);

[AuthorizeRequirement(CloudPermissionCatalog.Role.Define)]
public record GetAllDefinedPermissionsQuery() : IHumanQuery<Result<List<PermissionGroupDto>>>;

public class GetAllDefinedPermissionsHandler
    : IQueryHandler<GetAllDefinedPermissionsQuery, Result<List<PermissionGroupDto>>>
{
    public Task<Result<List<PermissionGroupDto>>> Handle(
        GetAllDefinedPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grouped = CloudPermissionCatalog.RoleAdminAssignable
            .OrderBy(permission => permission)
            .GroupBy(permission => permission.Contains('.') ? permission.Split('.')[0] : "Other")
            .Select(group => new PermissionGroupDto(group.Key, group.ToList()))
            .OrderBy(group => group.GroupName)
            .ToList();

        return Task.FromResult(Result.Success(grouped));
    }
}
