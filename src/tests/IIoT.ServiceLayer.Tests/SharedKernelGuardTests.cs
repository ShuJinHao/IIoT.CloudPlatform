using System.Linq.Expressions;
using IIoT.Services.CrossCutting.Caching.Options;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.Configuration;
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

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(99, 99)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(500, 100)]
    public void Pagination_PageSize_ShouldClampBoundaryValues(int input, int expected)
    {
        var pagination = new Pagination
        {
            PageSize = input
        };

        Assert.Equal(expected, pagination.PageSize);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Pagination_PageNumber_ShouldClampLowerBoundaryValues(int input, int expected)
    {
        var pagination = new Pagination
        {
            PageNumber = input
        };

        Assert.Equal(expected, pagination.PageNumber);
    }

    [Fact]
    public void Specification_ShouldExposeReadOnlyIncludeCollections()
    {
        var specification = new FakeEntitySpecification();

        Assert.IsAssignableFrom<IReadOnlyList<Expression<Func<FakeEntity, object>>>>(specification.Includes);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(specification.IncludeStrings);
        Assert.Single(specification.Includes);
        Assert.Single(specification.IncludeStrings);
        Assert.False(specification.Includes is List<Expression<Func<FakeEntity, object>>>);
        Assert.False(specification.IncludeStrings is List<string>);

        var includes = Assert.IsAssignableFrom<ICollection<Expression<Func<FakeEntity, object>>>>(specification.Includes);
        var includeStrings = Assert.IsAssignableFrom<ICollection<string>>(specification.IncludeStrings);

        Assert.Throws<NotSupportedException>(() => includes.Add(entity => entity.Name));
        Assert.Throws<NotSupportedException>(() => includeStrings.Add("GrandChildren"));
    }

    [Fact]
    public void PermissionCacheOptions_ShouldPreferMinutes_ThenHours_ThenDefault()
    {
        var minutesPreferred = new PermissionCacheOptions
        {
            ExpirationMinutes = 10,
            ExpirationHours = 2
        };
        var legacyHoursOnly = new PermissionCacheOptions
        {
            ExpirationHours = 2
        };
        var defaultFallback = new PermissionCacheOptions
        {
            ExpirationMinutes = 0,
            ExpirationHours = 0
        };

        Assert.Equal(TimeSpan.FromMinutes(10), minutesPreferred.ResolveExpiration());
        Assert.Equal(TimeSpan.FromHours(2), legacyHoursOnly.ResolveExpiration());
        Assert.Equal(TimeSpan.FromMinutes(10), defaultFallback.ResolveExpiration());
    }

    [Fact]
    public void PermissionCacheOptions_ShouldRespectLegacyHourOnlyConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PermissionCacheOptions.SectionName}:ExpirationHours"] = "2",
            })
            .Build();

        var options = new PermissionCacheOptions();
        configuration.GetSection(PermissionCacheOptions.SectionName).Bind(options);

        Assert.Equal(0, options.ExpirationMinutes);
        Assert.Equal(2, options.ExpirationHours);
        Assert.Equal(TimeSpan.FromHours(2), options.ResolveExpiration());
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
