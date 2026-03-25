using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates;
using IIoT.Core.Production.Aggregates.Capacities;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.PassStations;
using IIoT.Core.Production.Aggregates.Recipes;

namespace IIoT.Application.Contracts; // 注意替换成你实际的命名空间

public interface IDataQueryService
{
    // 替换为 IIoT 域的 4 个核心聚合根
    public IQueryable<Employee> Employees { get; }

    public IQueryable<MfgProcess> MfgProcesses { get; }

    public IQueryable<Device> Devices { get; }

    public IQueryable<Recipe> Recipes { get; }

    public IQueryable<PassDataInjection> PassDataInjection { get; }

    public IQueryable<DailyCapacity> DailyCapacities { get; }

    public IQueryable<DeviceLog> DeviceLogs { get; }

    // 完美保留你的通用查询封装方法，一行不改！
    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> queryable) where T : class;

    Task<IList<T>> ToListAsync<T>(IQueryable<T> queryable) where T : class;

    Task<bool> AnyAsync<T>(IQueryable<T> queryable) where T : class;
}