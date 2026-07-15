using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Specification;

/// <summary>
/// 规约模式的 EF Core 解析引擎
/// </summary>
public static class SpecificationEvaluator
{
    /// <summary>
    /// 获取查询对象
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="inputQuery">输入查询对象</param>
    /// <param name="specification">查询规范</param>
    /// <returns>查询对象</returns>
    public static IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, ISpecification<T>? specification)
        where T : class, IEntity
    {
        // 允许传入空规约，直接返回原查询。EF 执行器只接受 SharedKernel
        // 的标准 Specification 基类，避免通过开放接口 getter 执行不可审计逻辑。
        if (specification == null) return inputQuery;
        if (specification is not Specification<T> executableSpecification)
        {
            throw new InvalidOperationException(
                $"Specification '{specification.GetType().FullName}' must derive from {typeof(Specification<T>).FullName}.");
        }

        var query = inputQuery;

        // 应用过滤条件
        if (executableSpecification.FilterCondition != null)
            query = query.Where(executableSpecification.FilterCondition);

        // 应用包含关系
        query = executableSpecification.Includes.Aggregate(query, (current, include) => current.Include(include));

        // 应用字符串包含关系
        query = executableSpecification.IncludeStrings.Aggregate(query, (current, include) => current.Include(include));

        // 应用排序
        if (executableSpecification.OrderBy != null)
            query = query.OrderBy(executableSpecification.OrderBy);
        else if (executableSpecification.OrderByDescending != null)
            query = query.OrderByDescending(executableSpecification.OrderByDescending);

        // 应用分组
        if (executableSpecification.GroupBy != null)
            query = query.GroupBy(executableSpecification.GroupBy).SelectMany(x => x);

        // 应用分页
        if (executableSpecification.IsPagingEnabled)
            query = query.Skip(executableSpecification.Skip).Take(executableSpecification.Take);

        return query;
    }
}
