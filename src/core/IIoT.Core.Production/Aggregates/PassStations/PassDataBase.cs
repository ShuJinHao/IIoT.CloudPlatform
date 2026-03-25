using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.PassStations;

/// <summary>
/// 过站数据基类：所有工序的过站表共享相同的公共字段
/// 新增工序时继承此基类，在子类中添加该工序专属的检测参数和追溯标识字段
/// 写入/修改/删除走 EF Core，查询走 Dapper
/// </summary>
public abstract class PassDataBase : IAggregateRoot
{
    protected PassDataBase()
    {
    }

    protected PassDataBase(Guid deviceId, string cellResult, DateTime completedTime)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        CellResult = cellResult;
        CompletedTime = completedTime;
        ReceivedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 过站记录全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 产出该数据的设备 UUID (关联 devices 表，建索引)
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// 检测结果 (OK / NG)
    /// </summary>
    public string CellResult { get; set; } = null!;

    /// <summary>
    /// 完成检测的时间 (边缘端时间戳，TimescaleDB 分区键)
    /// </summary>
    public DateTime CompletedTime { get; set; }

    /// <summary>
    /// 云端接收到该条数据的时间 (服务端自动打戳)
    /// </summary>
    public DateTime ReceivedAt { get; set; }
}