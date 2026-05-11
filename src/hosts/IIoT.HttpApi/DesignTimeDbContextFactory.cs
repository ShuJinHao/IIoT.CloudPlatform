using IIoT.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenIddict.EntityFrameworkCore;

namespace IIoT.HttpApi;

/// <summary>
/// EF Core 设计时工厂：仅用于 dotnet ef migrations 命令
/// 绕过 Program.cs 的完整 DI 注册，避免设计时缺少连接字符串和服务报错
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IIoTDbContext>
{
    public IIoTDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IIoTDbContext>();
        optionsBuilder.UseNpgsql(DesignTimeConnectionStringResolver.Resolve());
        optionsBuilder.UseOpenIddict<Guid>();

        return new IIoTDbContext(optionsBuilder.Options);
    }
}
