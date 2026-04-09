using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;

namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 跨聚合的轻量只读查询入口。
///
/// 暴露 4 个聚合根的 IQueryable,Application 层 Handler 直接用 LINQ 表达跨聚合存在性/计数校验,
/// 通用执行方法负责物化(走 EF Core 的 EntityFrameworkQueryableExtensions)。
///
/// 设计取舍:
/// - 暴露 IQueryable 是务实的选择 — Handler 写法简洁、不需要为每种校验建 Spec 或方法
/// - 仅限聚合根。记录类(DeviceLog / HourlyCapacity / PassDataInjection)由
///   Dapper 那一侧的 IDeviceLogQueryService / ICapacityQueryService / IPassStationQueryService 负责
/// - 写入路径仍然必须经过 IRepository<T> 走 EF Core Change Tracking,不能用本服务做写
/// </summary>
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