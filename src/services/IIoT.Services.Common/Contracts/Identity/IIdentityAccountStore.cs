using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.SharedKernel.Result;

namespace IIoT.Services.Common.Contracts;

public interface IIdentityAccountStore
{
    Task<Result<IdentityAccount>> CreateAsync(IdentityAccount account, CancellationToken cancellationToken = default);

    Task<IdentityAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IdentityAccount?> GetByEmployeeNoAsync(string employeeNo, CancellationToken cancellationToken = default);

    Task<Result<bool>> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<bool>> AssignRoleAsync(Guid id, string roleName, CancellationToken cancellationToken = default);

    Task<IList<string>> GetRolesAsync(Guid id, CancellationToken cancellationToken = default);
}
