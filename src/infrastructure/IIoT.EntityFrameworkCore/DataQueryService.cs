using IIoT.Application.Contracts;
using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using Microsoft.EntityFrameworkCore;

namespace IIoT.Infrastructure.EntityFrameworkCore;

// 完美保留主构造函数语法
public class DataQueryService(IIoTDbContext dbContext) : IDataQueryService
{
    // 替换为 IIoT 域的 4 个核心聚合根
    public IQueryable<Employee> Employees => dbContext.Employees.AsNoTracking();

    public IQueryable<MfgProcess> MfgProcesses => dbContext.MfgProcesses.AsNoTracking();
    public IQueryable<Device> Devices => dbContext.Devices.AsNoTracking();
    public IQueryable<Recipe> Recipes => dbContext.Recipes.AsNoTracking();

    // 完美保留你的通用查询封装方法，一行不改！
    public async Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> queryable) where T : class
    {
        return await queryable.AsNoTracking().FirstOrDefaultAsync();
    }

    public async Task<IList<T>> ToListAsync<T>(IQueryable<T> queryable) where T : class
    {
        return await queryable.AsNoTracking().ToListAsync();
    }

    public async Task<bool> AnyAsync<T>(IQueryable<T> queryable) where T : class
    {
        return await queryable.AsNoTracking().AnyAsync();
    }
}