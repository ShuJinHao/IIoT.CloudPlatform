using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore; // 🌟 修复那堆红线报错的唯一关键！
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;

namespace IIoT.Infrastructure.EntityFrameworkCore.Repository;

// 保留极简的主构造函数写法！
public class EfReadRepository<T>(IIoTDbContext dbContext) : IReadRepository<T>
    where T : class, IAggregateRoot
{
    public IQueryable<T> GetQueryable()
    {
        return dbContext.Set<T>().AsQueryable();
    }

    public async Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        // 保留极简的集合表达式 [id]
        return await dbContext.Set<T>().FindAsync([id], cancellationToken);
    }

    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>().Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>().Where(expression).CountAsync(cancellationToken);
    }

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<T>().AsQueryable();

        if (includes != null)
        {
            foreach (var include in includes)
            {
                query = query.Include(include); // Include 报错就是因为没引用 EF Core 命名空间
            }
        }

        return await query.FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<T>().AsQueryable();

        if (includes != null)
        {
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
        }

        return await query.Where(expression).ToListAsync(cancellationToken);
    }
}