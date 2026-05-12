using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Repository;

// 保留极简的主构造函数与基类调用
public class EfRepository<T>(IIoTDbContext dbContext) : EfReadRepository<T>(dbContext), IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    private readonly IIoTDbContext _dbContext = dbContext;

    public T Add(T entity)
    {
        _dbContext.Set<T>().Add(entity);
        return entity;
    }

    public void Update(T entity)
    {
        var entry = _dbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        throw new InvalidOperationException(
            "Detached aggregate updates are not supported. Load the aggregate in the current DbContext before calling Update to avoid full-entity updates.");
    }

    public void Delete(T entity)
    {
        _dbContext.Set<T>().Remove(entity);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
