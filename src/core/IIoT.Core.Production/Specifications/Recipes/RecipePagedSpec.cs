using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;
using System;
using System.Collections.Generic;

namespace IIoT.Core.Production.Specifications.Recipes;

/// <summary>
/// 专用查询规约：查询配方分页列表 (完美融合工序/机台双维 ABAC 过滤与关键字搜索)
/// </summary>
public class RecipePagedSpec : Specification<Recipe>
{
    /// <summary>
    /// 构造配方分页查询规约
    /// </summary>
    /// <param name="skip">跳过条数</param>
    /// <param name="take">获取条数</param>
    /// <param name="allowedProcessIds">允许的工序管辖集合 (null代表上帝视角)</param>
    /// <param name="allowedDeviceIds">允许的机台管辖集合 (null代表上帝视角)</param>
    /// <param name="keyword">关键字 (匹配配方名称)</param>
    /// <param name="isPaging">是否启用分页</param>
    public RecipePagedSpec(
        int skip,
        int take,
        List<Guid>? allowedProcessIds = null,
        List<Guid>? allowedDeviceIds = null,
        string? keyword = null,
        bool isPaging = true)
    {
        // 🌟 核心业务灵魂：双维 ABAC 数据级拦截
        FilterCondition = r =>
            // 1. 权限拦截条件
            (
                (allowedProcessIds == null && allowedDeviceIds == null) // Admin 上帝视角，全量放行
                ||
                (!r.DeviceId.HasValue && allowedProcessIds != null && allowedProcessIds.Contains(r.ProcessId)) // 通用配方：校验工序管辖权
                ||
                (r.DeviceId.HasValue && allowedDeviceIds != null && allowedDeviceIds.Contains(r.DeviceId.Value)) // 特调配方：校验具体机台管辖权
            )
            &&
            // 2. 模糊搜索条件
            (string.IsNullOrEmpty(keyword) || r.RecipeName.Contains(keyword));

        // 默认按创建时间或名称排序 (此处假设按配方名称排序，保证分页稳定性)
        SetOrderBy(r => r.RecipeName);

        // 如果开启分页，则装载分页参数
        if (isPaging)
        {
            SetPaging(skip, take);
        }
    }
}