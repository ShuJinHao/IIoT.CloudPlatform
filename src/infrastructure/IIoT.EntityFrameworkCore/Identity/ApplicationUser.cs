using Microsoft.AspNetCore.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 工业云平台自定义身份验证用户 (纯粹的保安角色)
/// 仅仅把默认的 string 主键改成了 Guid，其他什么业务字段都不加！
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    // 删掉了 EmployeeNo 和 RealName
    // 因为 UserName 就可以存工号，而真实姓名去 Employee 表里查就行了！
}