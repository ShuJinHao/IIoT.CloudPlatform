using System.Linq.Expressions;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class SharedKernelGuardTests
{
    [Fact]
    public void Result_ImplicitFailureConversion_ShouldKeepStatusAndUseDefaultValue()
    {
        Result<int> result = Result.Failure("failed");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Error, result.Status);
        Assert.Equal(0, result.Value);
        Assert.Equal("failed", Assert.Single(result.Errors!));
    }

    [Fact]
    public void Pagination_ShouldClampLowerAndUpperBounds()
    {
        var pagination = new Pagination
        {
            PageNumber = 0,
            PageSize = 0
        };

        Assert.Equal(1, pagination.PageNumber);
        Assert.Equal(1, pagination.PageSize);

        pagination.PageSize = 500;

        Assert.Equal(100, pagination.PageSize);
    }

    [Fact]
    public void Specification_ShouldExposeReadOnlyIncludeCollections()
    {
        var specification = new FakeEntitySpecification();

        Assert.IsAssignableFrom<IReadOnlyList<Expression<Func<FakeEntity, object>>>>(specification.Includes);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(specification.IncludeStrings);
        Assert.Single(specification.Includes);
        Assert.Single(specification.IncludeStrings);
    }

    private sealed class FakeEntitySpecification : Specification<FakeEntity>
    {
        public FakeEntitySpecification()
        {
            AddInclude(entity => entity.Name);
            AddInclude("Children");
        }
    }

    private sealed class FakeEntity : IEntity<Guid>
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}
