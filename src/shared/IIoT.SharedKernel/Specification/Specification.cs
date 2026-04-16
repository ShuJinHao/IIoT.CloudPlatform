using IIoT.SharedKernel.Domain;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace IIoT.SharedKernel.Specification;

public abstract class Specification<T> : ISpecification<T> where T : class, IEntity
{
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<string> _includeStrings = [];

    public Expression<Func<T, bool>>? FilterCondition { get; protected init; }
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _includes;
    public IReadOnlyList<string> IncludeStrings => _includeStrings;
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
