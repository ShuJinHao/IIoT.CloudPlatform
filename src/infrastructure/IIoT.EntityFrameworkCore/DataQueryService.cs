using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Contracts;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore;

/// <summary>
/// IDataQueryService 的 EF Core 实现。
/// 4 个聚合根 DbSet 一律 AsNoTracking — 本服务只承担跨聚合只读查询,
/// 写入路径必须走 IRepository<T>。
/// </summary>
public class DataQueryService(IIoTDbContext dbContext) : IDataQueryService
{
    public IQueryable<Employee> Employees => dbContext.Employees.AsNoTracking();
    public IQueryable<MfgProcess> MfgProcesses => dbContext.MfgProcesses.AsNoTracking();
    public IQueryable<Device> Devices => dbContext.Devices.AsNoTracking();
    public IQueryable<Recipe> Recipes => dbContext.Recipes.AsNoTracking();

    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> queryable) where T : class
        => queryable.FirstOrDefaultAsync();

    public async Task<IList<T>> ToListAsync<T>(IQueryable<T> queryable) where T : class
        => await queryable.ToListAsync();

    public Task<bool> AnyAsync<T>(IQueryable<T> queryable) where T : class
        => queryable.AnyAsync();
}