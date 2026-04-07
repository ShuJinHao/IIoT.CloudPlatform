using System.Linq.Expressions;
using IIoT.EntityFrameworkCore.Specification;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Repository;

/// <summary>
/// 聚合根的只读仓储。
/// 查询入口只暴露 Specification 和退化谓词两种,不暴露 IQueryable,
/// 避免基础设施细节泄漏到上层。
/// </summary>
public class EfReadRepository<T>(IIoTDbContext dbContext) : IReadRepository<T>
    where T : class, IAggregateRoot
{
    // ── Specification 路径 ──────────────────────────────────────

    public async Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .ToListAsync(cancellationToken);
    }

    public async Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .AnyAsync(cancellationToken);
    }

    // ── 退化查询路径(命令端快捷方式)───────────────────────────

    public async Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>()
            .AsNoTracking()
            .AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> CountAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>()
            .AsNoTracking()
            .CountAsync(predicate, cancellationToken);
    }
}