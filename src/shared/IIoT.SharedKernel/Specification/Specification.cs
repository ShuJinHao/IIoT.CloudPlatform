using System.Collections.ObjectModel;
using System.Linq.Expressions;
using IIoT.SharedKernel.Domain;

namespace IIoT.SharedKernel.Specification;

public abstract class Specification<T> : ISpecification<T> where T : class, IEntity
{
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<string> _includeStrings = [];
    private readonly ReadOnlyCollection<Expression<Func<T, object>>> _readOnlyIncludes;
    private readonly ReadOnlyCollection<string> _readOnlyIncludeStrings;

    protected Specification()
    {
        _readOnlyIncludes = _includes.AsReadOnly();
        _readOnlyIncludeStrings = _includeStrings.AsReadOnly();
    }

    public Expression<Func<T, bool>>? FilterCondition { get; protected init; }
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _readOnlyIncludes;
    public IReadOnlyList<string> IncludeStrings => _readOnlyIncludeStrings;
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public Expression<Func<T, object>>? GroupBy { get; private set; }
    public int Take { get; private set; }
    public int Skip { get; private set; }
    public bool IsPagingEnabled { get; private set; }

    protected void AddInclude(Expression<Func<T, object>> includeExpression)
    {
        _includes.Add(includeExpression);
    }

    protected void AddInclude(string includeString)
    {
        _includeStrings.Add(includeString);
    }

    protected void SetPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }

    protected void SetOrderBy(Expression<Func<T, object>> orderByExpression)
    {
        OrderBy = orderByExpression;
    }

    protected void SetOrderByDescending(Expression<Func<T, object>> orderByDescExpression)
    {
        OrderByDescending = orderByDescExpression;
    }

    protected void SetGroupBy(Expression<Func<T, object>> groupByExpression)
    {
        GroupBy = groupByExpression;
    }
}
