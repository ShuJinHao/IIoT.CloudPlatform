namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 用户查询服务接口 (保安科 - 查询组)
/// 职责：用户档案的只读查询
/// </summary>
public interface IUserQueryService
{
    /// <summary>
    /// 纯粹的身份认证返回模型 (绝对不含任何业务数据)
    /// </summary>
    Task<IList<IdentityUserDto>> GetAllUsersAsync();

    Task<IdentityUserDto?> GetUserByEmployeeNoAsync(string employeeNo);
}
