using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

// 查询是不需要入参的
public record GetAllRolesQuery() : IQuery<Result<IList<string>>>;

public class GetAllRolesHandler(IIdentityService identityService) : IQueryHandler<GetAllRolesQuery, Result<IList<string>>>
{
    public async Task<Result<IList<string>>> Handle(GetAllRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await identityService.GetAllRolesAsync();
        return Result.Success(roles);
    }
}