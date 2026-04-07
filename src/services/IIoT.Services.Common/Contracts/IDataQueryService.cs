using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;

namespace IIoT.Services.Common.Contracts;

public interface IDataQueryService
{
    IQueryable<Employee> Employees { get; }
    IQueryable<MfgProcess> MfgProcesses { get; }
    IQueryable<Device> Devices { get; }
    IQueryable<Recipe> Recipes { get; }

    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> queryable) where T : class;
    Task<IList<T>> ToListAsync<T>(IQueryable<T> queryable) where T : class;
    Task<bool> AnyAsync<T>(IQueryable<T> queryable) where T : class;
}