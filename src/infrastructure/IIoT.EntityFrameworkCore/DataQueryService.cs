using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Capacities;
using IIoT.Core.Production.Aggregates.DeviceLogs;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.PassStations;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Contracts;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore;

public class DataQueryService(IIoTDbContext dbContext) : IDataQueryService
{
    public IQueryable<Employee> Employees => dbContext.Employees.AsNoTracking();
    public IQueryable<MfgProcess> MfgProcesses => dbContext.MfgProcesses.AsNoTracking();
    public IQueryable<Device> Devices => dbContext.Devices.AsNoTracking();
    public IQueryable<Recipe> Recipes => dbContext.Recipes.AsNoTracking();
    public IQueryable<PassDataInjection> PassDataInjection => dbContext.PassDataInjection.AsNoTracking();
    public IQueryable<HourlyCapacity> HourlyCapacities => dbContext.HourlyCapacities.AsNoTracking();
    public IQueryable<DeviceLog> DeviceLogs => dbContext.DeviceLogs.AsNoTracking();

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