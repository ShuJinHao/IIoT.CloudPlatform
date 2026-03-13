using IIoT.SharedKernel.Result;

namespace IIoT.Services.Common.Contracts;

public interface IAccountService
{
    Task<Result> CreateUserAsync(Guid id, string employeeNo, string password);
    Task<Result> DeleteUserAsync(Guid userId);
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<Result<bool>> ResetPasswordAsync(Guid userId, string newPassword);
    Task<Result<bool>> CheckPasswordAsync(string employeeNo, string password);
    Task<Guid?> GetUserIdByEmployeeNoAsync(string employeeNo);
    Task<Result<bool>> AssignRoleToUserAsync(string employeeNo, string roleName);
    Task<IList<string>> GetRolesAsync(string employeeNo);
}
