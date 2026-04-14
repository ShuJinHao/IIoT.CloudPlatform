namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 用户只读查询服务。
/// </summary>
public interface IUserQueryService
{
    Task<IList<IdentityAccountDto>> GetAllUsersAsync();

    Task<IdentityAccountDto?> GetUserByEmployeeNoAsync(string employeeNo);
}
