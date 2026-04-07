using System.Linq.Expressions;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Specification;

namespace IIoT.SharedKernel.Repository;

/// <summary>
/// 聚合根只读仓储。
/// 
/// 查询入口分两类:
/// 1. Specification 重载 - 用于有业务语义的查询,查询意图应当被命名和复用
/// 2. Expression 重载    - 仅用于命令端的退化查询(存在性检查、计数),
///                         返回类型限定为 bool/int/T?,不暴露 IQueryable
/// </summary>
public interface IReadRepository<T> where T : class, IAggregateRoot
{
    // ── Specification 路径(取出实体)─────────────────────────────

    Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    // ── 退化查询路径(命令端快捷方式,仅返回 bool/int)────────────

    /// <summary>
    /// 命令端快捷存在性检查。不返回实体,只返回是否存在。
    /// 用于 Handler 里守护"不重复"之类的不变量,避免为每个一次性检查建 Spec。
    /// 需要取出实体时,必须走 Specification 重载。
    /// </summary>
    Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 命令端快捷计数。
    /// </summary>
    Task<int> CountAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);
}