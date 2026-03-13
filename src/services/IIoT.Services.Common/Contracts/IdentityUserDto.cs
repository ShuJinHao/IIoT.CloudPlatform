namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 纯粹的身份认证返回模型 (绝对不含任何业务数据)
/// </summary>
public record IdentityUserDto(Guid Id, string EmployeeNo, IList<string> Roles);
