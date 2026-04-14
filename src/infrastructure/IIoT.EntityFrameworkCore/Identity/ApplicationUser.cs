using Microsoft.AspNetCore.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// ASP.NET Identity 持久化模型。
/// 这里只承载身份基础设施需要的字段，不作为领域聚合根暴露。
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public bool IsEnabled { get; set; } = true;
}
