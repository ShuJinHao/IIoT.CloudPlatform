using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;
using System;
using System.Collections.Generic;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// 专用查询规约：查询设备分页列表 (支持双维管辖权并集过滤 + 关键字搜索)
/// </summary>
public class DevicePagedSpec : Specification<Device>
{
    /// <summary>
    /// 构造设备分页查询规约
    /// </summary>
    /// <param name="skip">跳过条数</param>
    /// <param name="take">获取条数</param>
    /// <param name="allowedProcessIds">允许查询的工序ID集合 (若为null则代表上帝视角查全库)</param>
    /// <param name="allowedDeviceIds">允许查询的设备ID集合 (单独分配的设备管辖权)</param>
    /// <param name="keyword">关键字 (匹配名称或编号)</param>
    /// <param name="isPaging">是否启用分页 (查总数时传 false)</param>
    public DevicePagedSpec(
        int skip,
        int take,
        List<Guid>? allowedProcessIds = null,
        List<Guid>? allowedDeviceIds = null,
        string? keyword = null,
        bool isPaging = true)
    {
        // 核心：双维管辖权并集过滤 + 模糊搜索
        // 可见设备 = 我管辖的工序下的所有设备 ∪ 我被单独分配的设备
        FilterCondition = d =>
            (
                (allowedProcessIds == null && allowedDeviceIds == null) // Admin 上帝视角，全量放行
                ||
                (allowedProcessIds != null && allowedProcessIds.Contains(d.ProcessId)) // 工序维度：该设备所属工序在我的管辖列表中
                ||
                (allowedDeviceIds != null && allowedDeviceIds.Contains(d.Id)) // 设备维度：该设备被单独分配给我
            )
            &&
            (string.IsNullOrEmpty(keyword) || d.DeviceCode.Contains(keyword) || d.DeviceName.Contains(keyword));

        // 默认按设备编号排序
        SetOrderBy(d => d.DeviceCode);

        if (isPaging)
        {
            SetPaging(skip, take);
        }
    }
}
