using System.Collections.Immutable;
using IIoT.CloudPlatform.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace IIoT.CloudPlatform.AnalyzerTests;

public sealed class CloudArchitectureAnalyzerTests
{
    private const string AiReadPrelude = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace IIoT.Services.Contracts
        {
            public interface IHumanRequest<out T> { }
            public interface IDeviceRequest<out T> { }
            public interface IAnonymousBootstrapRequest<out T> { }
            public interface IPublicRequest<out T> { }
            public interface IAiReadRequest<out T> { }
            public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
        }

        namespace IIoT.Services.CrossCutting.Attributes
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
            public sealed class AuthorizeAiReadAttribute : Attribute
            {
                public AuthorizeAiReadAttribute(string permission) { }
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
            public sealed class AuthorizeRequirementAttribute : Attribute
            {
                public AuthorizeRequirementAttribute(string permission) { }
            }

            [AttributeUsage(AttributeTargets.Class, Inherited = true)]
            public sealed class AdminOnlyAttribute : Attribute { }
        }

        namespace IIoT.SharedKernel.Messaging
        {
            public interface IQueryHandler<in TQuery, TResponse>
            {
                Task<TResponse> Handle(TQuery request, CancellationToken cancellationToken);
            }

            public interface ICommand<out TResponse> { }
        }

        namespace IIoT.SharedKernel.Architecture
        {
            public interface IReadOnlyQueryPort { }
        }

        namespace IIoT.SharedKernel.Repository
        {
            public interface IReadRepository<T> : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                Task<T?> GetAsync();
            }

            public interface IRepository<T> : IReadRepository<T>
            {
                T Add(T entity);
                Task<int> SaveChangesAsync();
            }
        }
        """;

    private const string DddPrelude = """
        namespace IIoT.SharedKernel.Domain
        {
            public interface IAggregateRoot { }
        }

        namespace IIoT.SharedKernel.Repository
        {
            public interface IReadRepository<T> { }
            public interface IRepository<T> : IReadRepository<T> { }
        }
        """;

    [Fact]
    public void SupportedDiagnostics_AreStableAndDefaultToError()
    {
        var diagnostics = new CloudArchitectureAnalyzer().SupportedDiagnostics;

        Assert.Equal(
            ["CLOUDARCH001", "CLOUDARCH002", "CLOUDARCH003", "CLOUDARCH004", "CLOUDARCH005", "CLOUDARCH006", "CLOUDARCH007", "CLOUDARCH008", "CLOUDARCH009", "CLOUDARCH010"],
            diagnostics.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal));
        Assert.All(diagnostics, descriptor =>
        {
            Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
            Assert.True(descriptor.IsEnabledByDefault);
            Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, descriptor.CustomTags);
        });
    }

    [Fact]
    public async Task CoreReferencingService_ReportsLayerDiagnostic()
    {
        var service = CreateReference("IIoT.ProductionService", "public sealed class ServiceMarker { }");

        var diagnostics = await AnalyzeAsync("IIoT.Core.Fixture", ["public sealed class CoreType { }"], service);
        var unclassifiedDiagnostics = await AnalyzeAsync(
            "IIoT.FutureProductionComponent",
            ["public sealed class FutureProductionType { }"]);

        AssertSingle(diagnostics, "CLOUDARCH001");
        AssertSingle(unclassifiedDiagnostics, "CLOUDARCH001");
    }

    [Fact]
    public async Task ServiceReferencingInfrastructure_ReportsLayerDiagnostic()
    {
        var infrastructure = CreateReference("IIoT.Infrastructure.Fixture", "public sealed class InfraMarker { }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture",
            ["public sealed class ServiceType { }"],
            infrastructure);

        AssertSingle(diagnostics, "CLOUDARCH001");
    }

    [Fact]
    public async Task HostReferencingInfrastructure_IsAllowedCompositionRoot()
    {
        var infrastructure = CreateReference("IIoT.Infrastructure.Fixture", "public sealed class InfraMarker { }");

        var diagnostics = await AnalyzeAsync("IIoT.HttpApi.Fixture", ["public sealed class HostType { }"], infrastructure);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AggregateDeclaredInService_ReportsAggregateBoundary()
    {
        var source = DddPrelude + """
            namespace Fixture
            {
                using IIoT.SharedKernel.Domain;
                public sealed class WrongAggregate : IAggregateRoot { }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH002");
    }

    [Fact]
    public async Task InheritedAggregateDeclaredInCore_IsAllowed()
    {
        var source = DddPrelude + """
            namespace Fixture
            {
                using IIoT.SharedKernel.Domain;
                public abstract class AggregateBase : IAggregateRoot { }
                public sealed class Device : AggregateBase { }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Core.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RepositoryAliasWithNonAggregate_ReportsAggregateBoundary()
    {
        var source = DddPrelude + """
            namespace Fixture
            {
                using ProjectionRepository = IIoT.SharedKernel.Repository.IRepository<Projection>;
                public sealed class Projection { }
                public sealed class Consumer
                {
                    private readonly ProjectionRepository repository;
                    public Consumer(ProjectionRepository repository) => this.repository = repository;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CLOUDARCH002");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id != "CLOUDARCH002");
    }

    [Fact]
    public async Task GenericRepositoryConstrainedToAggregate_IsAllowed()
    {
        var source = DddPrelude + """
            namespace Fixture
            {
                using IIoT.SharedKernel.Domain;
                using IIoT.SharedKernel.Repository;
                public sealed class Consumer<T> where T : IAggregateRoot
                {
                    private readonly IRepository<T> repository;
                    public Consumer(IRepository<T> repository) => this.repository = repository;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalGenericServiceResolutionWithNonAggregateRepository_ReportsAggregateBoundary()
    {
        var source = DddPrelude + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceProviderServiceExtensions
                {
                    public static T GetRequiredService<T>(this System.IServiceProvider provider) => default!;
                }
            }

            namespace Fixture
            {
                using IIoT.SharedKernel.Repository;
                using Microsoft.Extensions.DependencyInjection;

                public sealed class Projection { }
                public sealed class Consumer
                {
                    public void Run(System.IServiceProvider services)
                    {
                        var repository = services.GetRequiredService<IRepository<Projection>>();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH002");
    }

    [Fact]
    public async Task LocalGenericServiceResolutionWithAggregateRepository_IsAllowed()
    {
        var source = DddPrelude + """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceProviderServiceExtensions
                {
                    public static T GetRequiredService<T>(this System.IServiceProvider provider) => default!;
                }
            }

            namespace Fixture
            {
                using IIoT.SharedKernel.Domain;
                using IIoT.SharedKernel.Repository;
                using Microsoft.Extensions.DependencyInjection;

                public sealed class Device : IAggregateRoot { }
                public sealed class Consumer
                {
                    public void Run(System.IServiceProvider services)
                    {
                        var repository = services.GetRequiredService<IRepository<Device>>();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Core.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ServiceDapperAliasInvocation_ReportsDatabaseOwner()
    {
        var dapper = CreateReference(
            "Dapper",
            "namespace Dapper { public static class SqlMapper { public static int Execute(object db, string sql) => 0; } }");
        var source = """
            using ExecuteApi = Dapper.SqlMapper;
            public sealed class QueryService
            {
                public int Run(object db) => ExecuteApi.Execute(db, "update devices set name = 'x'");
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source], dapper);

        AssertSingle(diagnostics, "CLOUDARCH003");
    }

    [Fact]
    public async Task InfrastructureDapperInvocation_IsAllowedOwner()
    {
        var dapper = CreateReference(
            "Dapper",
            "namespace Dapper { public static class SqlMapper { public static int Execute(object db, string sql) => 0; } }");
        var source = "public sealed class Store { public int Run(object db) => Dapper.SqlMapper.Execute(db, \"sql\"); }";

        var diagnostics = await AnalyzeAsync("IIoT.Dapper.Fixture", [source], dapper);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ServiceDatabaseTypeParameter_ReportsDatabaseOwner()
    {
        var entityFramework = CreateReference(
            "Microsoft.EntityFrameworkCore",
            "namespace Microsoft.EntityFrameworkCore { public abstract class DbContext { } }");
        var source = "public sealed class Service { public void Run(Microsoft.EntityFrameworkCore.DbContext db) { } }";

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source], entityFramework);

        AssertSingle(diagnostics, "CLOUDARCH003");
    }

    [Fact]
    public async Task HostDatabaseAccessWithoutExactException_ReportsDatabaseOwner()
    {
        var dapper = CreateReference(
            "Dapper",
            "namespace Dapper { public static class SqlMapper { public static int Execute(object db, string sql) => 0; } }");
        var source = "public sealed class Controller { public int Run(object db) => Dapper.SqlMapper.Execute(db, \"sql\"); }";

        var diagnostics = await AnalyzeAsync("IIoT.HttpApi.Fixture", [source], dapper);

        AssertSingle(diagnostics, "CLOUDARCH003");
    }

    [Fact]
    public async Task AiReadRequestWithoutAuthorization_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            public sealed class MissingAuthorization : IIoT.Services.Contracts.IAiReadQuery<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task AiReadRequestWithoutAuthorizationAttributeAssembly_ReportsMetadataDiagnostic()
    {
        const string source = """
            namespace IIoT.Services.Contracts
            {
                public interface IAiReadRequest<out T> { }
            }

            public sealed class MissingAuthorization : IIoT.Services.Contracts.IAiReadRequest<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task NonAiReadRequestWithAiAuthorization_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
            public sealed class WrongRequest { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task AiReadRequestWithWrongPermission_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("Device.Read")]
            public sealed class WrongPermission : IIoT.Services.Contracts.IAiReadQuery<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task AiReadRequestWithBarePermissionPrefix_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.")]
            public sealed class BarePermissionPrefix : IIoT.Services.Contracts.IAiReadQuery<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task AiReadRequestMixingHumanAuthorization_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
            [IIoT.Services.CrossCutting.Attributes.AuthorizeRequirement("Device.Read")]
            public sealed class MixedRequest : IIoT.Services.Contracts.IAiReadQuery<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task InheritedAiReadAuthorization_IsRecognized()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
            public abstract class AuthorizedBase : IIoT.Services.Contracts.IAiReadQuery<int> { }
            public sealed class DerivedRequest : AuthorizedBase { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HumanRequestWithHumanAuthorization_IsAllowed()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeRequirement("Device.Read")]
            public sealed class HumanQuery : IIoT.Services.Contracts.IHumanRequest<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task PublicRequestWithHumanAuthorization_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeRequirement("Device.Read")]
            public sealed class PublicQuery : IIoT.Services.Contracts.IPublicRequest<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task DeviceRequestWithAdminOnly_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AdminOnly]
            public sealed class DeviceCommand : IIoT.Services.Contracts.IDeviceRequest<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task RequestWithMultipleKinds_ReportsMetadataDiagnostic()
    {
        var source = AiReadPrelude + """
            [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
            public sealed class MixedQuery :
                IIoT.Services.Contracts.IAiReadRequest<int>,
                IIoT.Services.Contracts.IHumanRequest<int> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH005");
    }

    [Fact]
    public async Task AiReadDirectRepositoryWrite_ReportsWritePath()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                private readonly dynamic dynamicRepository;
                private readonly System.Func<System.Threading.Tasks.Task<int>> fieldDeferred;
                private System.Func<System.Threading.Tasks.Task<int>> PropertyDeferred { get; }
                public Handler(
                    IIoT.SharedKernel.Repository.IRepository<object> repository,
                    dynamic dynamicRepository)
                {
                    this.repository = repository;
                    this.dynamicRepository = dynamicRepository;
                    fieldDeferred = PersistDirectly;
                    PropertyDeferred = PersistDirectly;
                }
                public async System.Threading.Tasks.Task<int> Handle(
                    Query request,
                    System.Threading.CancellationToken token)
                {
                    await PersistDirectly();
                    await InvokeCallback(() => repository.SaveChangesAsync());
                    System.Func<System.Threading.Tasks.Task<int>> deferred = () => repository.SaveChangesAsync();
                    await deferred();
                    await fieldDeferred();
                    await PropertyDeferred();
                    return await PersistDynamically();
                }
                private System.Threading.Tasks.Task<int> PersistDirectly()
                    => repository.SaveChangesAsync();
                private static System.Threading.Tasks.Task<int> InvokeCallback(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
                private System.Threading.Tasks.Task<int> PersistDynamically()
                    => dynamicRepository.SaveChangesAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture.WritePaths", [source]);

        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));

        var dapper = CreateReference(
            "Dapper",
            "namespace Dapper { public readonly struct CommandDefinition { public CommandDefinition(string sql) { } } public static class SqlMapper { public static object Query(object db, string sql) => new(); public static object Query(object db, CommandDefinition command) => new(); } }");
        var npgsql = CreateReference(
            "Npgsql",
            "namespace Npgsql { public sealed class NpgsqlCommand { public object ExecuteReader() => new(); public object ExecuteScalar() => new(); } }");
        var entityFramework = CreateReference(
            "Microsoft.EntityFrameworkCore.Relational",
            "namespace Microsoft.EntityFrameworkCore { public static class RelationalDatabaseFacadeExtensions { public static int ExecuteSqlRaw(object db, string sql) => 0; } }");
        var rawDatabaseSource = AiReadPrelude + AuthorizedQuery + """
            public sealed class RawDatabaseHandler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly object db;
                private readonly Npgsql.NpgsqlCommand command;
                private readonly string dynamicSql;
                public RawDatabaseHandler(object db, Npgsql.NpgsqlCommand command, string dynamicSql)
                {
                    this.db = db;
                    this.command = command;
                    this.dynamicSql = dynamicSql;
                }
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                {
                    _ = Dapper.SqlMapper.Query(db, "WITH changed AS (DELETE FROM device RETURNING id) SELECT * FROM changed");
                    _ = Dapper.SqlMapper.Query(db, dynamicSql);
                    _ = Dapper.SqlMapper.Query(db, new Dapper.CommandDefinition(dynamicSql));
                    _ = Dapper.SqlMapper.Query(db, "SELECT 1; SELECT 2");
                    _ = command.ExecuteReader();
                    _ = command.ExecuteScalar();
                    _ = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(db, "SELECT 1");
                    return System.Threading.Tasks.Task.FromResult(1);
                }
            }
            """;

        var rawDatabaseDiagnostics = await AnalyzeAsync(
            "IIoT.Dapper.Fixture.RawWritePaths",
            [rawDatabaseSource],
            dapper,
            npgsql,
            entityFramework);

        Assert.Equal(7, rawDatabaseDiagnostics.Length);
        Assert.All(rawDatabaseDiagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));

        var implicitBodySource = AiReadPrelude + AuthorizedQuery + """
            public sealed class ImplicitWriter
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;

                public ImplicitWriter(IIoT.SharedKernel.Repository.IRepository<object> repository)
                {
                    this.repository = repository;
                    _ = repository.SaveChangesAsync();
                }

                public int Value
                {
                    get { _ = repository.SaveChangesAsync(); return 1; }
                    set { _ = repository.SaveChangesAsync(); }
                }

                public int this[int index]
                {
                    get { _ = repository.SaveChangesAsync(); return index; }
                    set { _ = repository.SaveChangesAsync(); }
                }

                public event System.Action Changed
                {
                    add { _ = repository.SaveChangesAsync(); }
                    remove { _ = repository.SaveChangesAsync(); }
                }

                public static ImplicitWriter operator +(ImplicitWriter left, ImplicitWriter right)
                {
                    _ = left.repository.SaveChangesAsync();
                    return left;
                }

                public static explicit operator int(ImplicitWriter writer)
                {
                    _ = writer.repository.SaveChangesAsync();
                    return 1;
                }
            }

            public interface IDefaultReadPort : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                System.Threading.Tasks.Task<int> ReadAsync(
                    IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => repository.SaveChangesAsync();
            }

            public sealed class ImplicitBodyHandler
                : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;

                public ImplicitBodyHandler(IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => this.repository = repository;

                public System.Threading.Tasks.Task<int> Handle(
                    Query request,
                    System.Threading.CancellationToken token)
                {
                    var writer = new ImplicitWriter(repository);
                    _ = writer.Value;
                    writer.Value = 2;
                    _ = writer[0];
                    writer[0] = 2;
                    writer.Changed += OnChanged;
                    writer.Changed -= OnChanged;
                    _ = writer + writer;
                    _ = (int)writer;
                    return System.Threading.Tasks.Task.FromResult(1);
                }

                private static void OnChanged() { }
            }
            """;

        var implicitBodyDiagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.ImplicitBodies",
            [implicitBodySource]);

        Assert.Equal(10, implicitBodyDiagnostics.Length);
        Assert.All(implicitBodyDiagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));
    }

    [Fact]
    public async Task AiReadCompilerImplicitCallsAndHandlerLifecycle_ReportWritePaths()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class ConstructorHandler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public ConstructorHandler(IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => _ = repository.SaveChangesAsync();
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.FromResult(1);
            }

            public sealed class InitializerHandler(IIoT.SharedKernel.Repository.IRepository<object> repository)
                : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly System.Threading.Tasks.Task<int> initialized = repository.SaveChangesAsync();
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.FromResult(initialized.IsCompleted ? 1 : 0);
            }

            public sealed class StaticInitializerHandler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            { private static readonly System.Threading.Tasks.Task<int> initialized = ((IIoT.SharedKernel.Repository.IRepository<object>)null!).SaveChangesAsync(); public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.FromResult(initialized.IsCompleted ? 1 : 0); }

            public interface IDefaultHandler
                : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                System.Threading.Tasks.Task<int> IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>.Handle(Query request, System.Threading.CancellationToken token)
                    => ((IIoT.SharedKernel.Repository.IRepository<object>)null!).SaveChangesAsync();
            }

            public sealed class EvilEnumerable(
                IIoT.SharedKernel.Repository.IRepository<object> repository)
            {
                public EvilEnumerator GetEnumerator()
                {
                    _ = repository.SaveChangesAsync();
                    return new EvilEnumerator();
                }
            }

            public sealed class EvilEnumerator
            {
                public int Current => 1;
                public bool MoveNext() => false;
            }

            public sealed class EvilDisposable(
                IIoT.SharedKernel.Repository.IRepository<object> repository)
                : System.IDisposable
            {
                public void Dispose() => _ = repository.SaveChangesAsync();
            }

            public sealed class EvilAwaitable(
                IIoT.SharedKernel.Repository.IRepository<object> repository)
            {
                public EvilAwaiter GetAwaiter()
                {
                    _ = repository.SaveChangesAsync();
                    return new EvilAwaiter();
                }
            }

            public sealed class EvilAwaiter : System.Runtime.CompilerServices.INotifyCompletion
            {
                public bool IsCompleted => true;
                public int GetResult() => 1;
                public void OnCompleted(System.Action continuation) => continuation();
            }

            public sealed class EvilDeconstructable(
                IIoT.SharedKernel.Repository.IRepository<object> repository)
            {
                public void Deconstruct(out int left, out int right)
                {
                    _ = repository.SaveChangesAsync();
                    left = 1;
                    right = 2;
                }
            }

            public sealed class CompilerImplicitHandler(IIoT.SharedKernel.Repository.IRepository<object> repository)
                : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public async System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                {
                    foreach (var value in new EvilEnumerable(repository))
                        _ = value;
                    using var disposable = new EvilDisposable(repository);
                    _ = await new EvilAwaitable(repository);
                    var (left, right) = new EvilDeconstructable(repository);
                    return left + right;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.CompilerImplicit",
            [source]);

        Assert.True(
            diagnostics.Length == 8,
            string.Join(System.Environment.NewLine, diagnostics.Select(static item => item.ToString())));
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));
    }

    [Fact]
    public async Task AiReadUnresolvedNativeBoundary_FailsClosedWithoutFlaggingProvableSafeBodies()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public static class Boundary
            {
                [System.Runtime.InteropServices.DllImport("native-writer")]
                public static extern int Mutate();

                public static int EmptyBody() { return 0; }

                public static int AutoValue { get; set; }
            }

            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                {
                    _ = Boundary.EmptyBody();
                    Boundary.AutoValue = 1;
                    _ = Boundary.AutoValue;
                    return System.Threading.Tasks.Task.FromResult(Boundary.Mutate());
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.UnresolvedNative",
            [source]);

        AssertSingleById(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task ReadOnlyMarkerPropertyWithNestedAsyncDatabaseCapability_FailsClosed()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public interface IUnsafeCapabilityPort
                : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<System.Data.IDbConnection>> Connections { get; }
            }

            public sealed class Handler(IUnsafeCapabilityPort port) : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            { public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.FromResult(port.Connections.IsCompleted ? 1 : 0); }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.WritablePropertyCapability",
            [source]);

        AssertSingleById(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task ReadOnlyMarkerDelegateParameterReturningRepositoryCapability_FailsClosed()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public interface IUnsafeCapabilityPort
                : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                System.Threading.Tasks.Task<int> ReadAsync(
                    System.Func<IIoT.SharedKernel.Repository.IRepository<object>> capabilityFactory);
            }

            public sealed class Handler(IUnsafeCapabilityPort port, IIoT.SharedKernel.Repository.IRepository<object> repository)
                : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => port.ReadAsync(() => repository);
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.WritableDelegateCapability",
            [source]);

        AssertSingleById(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task ReadOnlyMarkerReturningCustomCapabilityWrapper_FailsClosed()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class CapabilityEnvelope
            {
                public System.Data.IDbCommand Command { get; init; } = default!;
            }

            public interface IUnsafeCapabilityPort
                : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                System.Threading.Tasks.Task<CapabilityEnvelope> ReadAsync();
            }

            public sealed class Handler(IUnsafeCapabilityPort port) : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public async System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => (await port.ReadAsync()).Command == null ? 0 : 1;
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.WritableWrapperCapability",
            [source]);

        AssertSingleById(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task AiReadScopeAccessorPureClaimsImplementation_IsAccepted()
    {
        var source = AiReadPrelude + AiReadScopeContract + """
            public sealed class ClaimsScopeAccessor : IIoT.Services.Contracts.Authorization.IAiReadScopeAccessor
            { public string Caller => "ai-service"; public IIoT.Services.Contracts.Authorization.AiReadScopeKind ScopeKind => IIoT.Services.Contracts.Authorization.AiReadScopeKind.Global; public System.Guid? DelegatedUserId => null; public System.Collections.Generic.IReadOnlyCollection<System.Guid>? DelegatedDeviceIds => null; }
            """;

        Assert.Empty(await AnalyzeAsync("IIoT.HttpApi.Fixture.PureAiReadScopeAccessor", [source]));
    }

    [Fact]
    public async Task AiReadScopeAccessorConstructorAndGetterStoredDelegateWrites_FailClosed()
    {
        var source = AiReadPrelude + AiReadScopeContract + """
            public sealed class UnsafeScopeAccessor
                : IIoT.Services.Contracts.Authorization.IAiReadScopeAccessor
            {
                private readonly System.Func<System.Threading.Tasks.Task<int>> write;

                public UnsafeScopeAccessor(
                    IIoT.SharedKernel.Repository.IRepository<object> repository)
                {
                    write = repository.SaveChangesAsync;
                    _ = write();
                }

                public string Caller
                {
                    get
                    {
                        _ = write();
                        return "unsafe";
                    }
                }

                public IIoT.Services.Contracts.Authorization.AiReadScopeKind ScopeKind => IIoT.Services.Contracts.Authorization.AiReadScopeKind.Global;
                public System.Guid? DelegatedUserId => null; public System.Collections.Generic.IReadOnlyCollection<System.Guid>? DelegatedDeviceIds => null;
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.HttpApi.Fixture.UnsafeAiReadScopeAccessor",
            [source]);
        var writeDiagnostics = diagnostics.Where(item => item.Id == "CLOUDARCH004").ToArray();

        Assert.Equal(2, writeDiagnostics.Length);
        Assert.All(writeDiagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public async Task SpecificationValueObjectIsNotTrustedAsAnOpenQueryPort()
    {
        var source = AiReadPrelude + AuthorizedQuery + SpecificationContract + """
            public sealed class SafeSpecification : ISpecification<object>
            {
                public System.Linq.Expressions.Expression<System.Func<object, bool>>? FilterCondition
                    => value => value != null;
            }

            public sealed class Handler(ISpecification<object> specification) : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int> { public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.FromResult(specification.FilterCondition == null ? 0 : 1); }
            """;

        AssertSingleById(await AnalyzeAsync("IIoT.ProductionService.Fixture.OpenSpecification", [source]), "CLOUDARCH004");
    }

    [Fact]
    public async Task ClosedSpecificationBaseWithPureProperties_IsAccepted()
    {
        var source = AiReadPrelude + AuthorizedQuery + SpecificationContract + """
            public abstract class Specification<T> : ISpecification<T>
            {
                public System.Linq.Expressions.Expression<System.Func<T, bool>>? FilterCondition { get; protected init; }
            }

            public sealed class SafeSpecification : Specification<object>
            {
                public SafeSpecification()
                {
                    FilterCondition = value => value != null;
                }
            }

            public sealed class Handler(SafeSpecification specification) : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            { public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.FromResult(specification.FilterCondition == null ? 0 : 1); }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.ClosedSpecification",
            [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AiReadPublicOpenDispatch_FailsClosedDespiteKnownSafeLocalImplementations()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public interface IOpenReader { int Read(); }

            public sealed class SafeReader : IOpenReader
            {
                public int Read() => 1;
            }

            public class OpenReaderBase
            {
                public virtual int Read() => 1;
            }

            public sealed class Handler(IOpenReader reader, OpenReaderBase virtualReader) : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.FromResult(reader.Read() + virtualReader.Read());
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.PublicOpenDispatch",
            [source]);

        Assert.Equal(["CLOUDARCH004", "CLOUDARCH004"], diagnostics.Select(static diagnostic => diagnostic.Id));
    }

    [Fact]
    public async Task ReadOnlyMarkerReturningWritableDatabaseCapability_FailsClosed()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public interface IUnsafeCapabilityPort
                : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                System.Data.IDbConnection Open();
            }

            public sealed class Handler(IUnsafeCapabilityPort port) : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.FromResult(port.Open().State == System.Data.ConnectionState.Open ? 1 : 0);
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture.WritableCapability",
            [source]);

        AssertSingleById(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task AiReadHelperWriteAcrossFiles_ReportsWritePath()
    {
        var requestAndHandler = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly WriterHelper helper;
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public Handler(WriterHelper helper, IIoT.SharedKernel.Repository.IRepository<object> repository)
                {
                    this.helper = helper;
                    this.repository = repository;
                }
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => helper.Persist(repository);
            }
            """;
        var helper = """
            public sealed class WriterHelper
            {
                public System.Threading.Tasks.Task<int> Persist(
                    IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => repository.SaveChangesAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [requestAndHandler, helper]);

        AssertSingle(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task AiReadInvokedDelegateArguments_ResolveConditionalCoalesceFactoryAndFailClosed()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                private readonly System.Func<System.Threading.Tasks.Task<int>> unresolved;
                public Handler(
                    IIoT.SharedKernel.Repository.IRepository<object> repository,
                    System.Func<System.Threading.Tasks.Task<int>> unresolved)
                {
                    this.repository = repository;
                    this.unresolved = unresolved;
                }
                public async System.Threading.Tasks.Task<int> Handle(
                    Query request,
                    System.Threading.CancellationToken token)
                {
                    _ = await Invoke(Create(request is not null) ?? Safe);
                    return await Invoke(unresolved);
                }
                private System.Func<System.Threading.Tasks.Task<int>> Create(bool write)
                    => write ? (() => repository.SaveChangesAsync()) : Safe;
                private static System.Threading.Tasks.Task<int> Safe()
                    => System.Threading.Tasks.Task.FromResult(1);
                private static System.Threading.Tasks.Task<int> Invoke(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture.DelegateUnsafe", [source]);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));
    }

    [Fact]
    public async Task AiReadInvokedDelegateArguments_WithProvenSafeFactories_DoNotReport()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> Handle(
                    Query request,
                    System.Threading.CancellationToken token)
                    => Invoke(Create(request is not null) ?? SafeOne);
                private static System.Func<System.Threading.Tasks.Task<int>> Create(bool first)
                    => first ? SafeOne : SafeTwo;
                private static System.Threading.Tasks.Task<int> SafeOne()
                    => System.Threading.Tasks.Task.FromResult(1);
                private static System.Threading.Tasks.Task<int> SafeTwo()
                    => System.Threading.Tasks.Task.FromResult(2);
                private static System.Threading.Tasks.Task<int> Invoke(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture.DelegateSafe", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AiReadExternalCallbacksAndDynamicInvoke_ResolveUnsafeAndSafeDelegates()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public Handler(IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => this.repository = repository;
                public async System.Threading.Tasks.Task<int> Handle(
                    Query request,
                    System.Threading.CancellationToken token)
                {
                    _ = await System.Threading.Tasks.Task.Run(() => repository.SaveChangesAsync());
                    System.Delegate unsafeDynamic =
                        new System.Func<System.Threading.Tasks.Task<int>>(() => repository.SaveChangesAsync());
                    _ = unsafeDynamic.DynamicInvoke();
                    _ = await System.Threading.Tasks.Task.Run(Safe);
                    System.Delegate safeDynamic =
                        new System.Func<System.Threading.Tasks.Task<int>>(Safe);
                    _ = safeDynamic.DynamicInvoke();
                    return 1;
                }
                private static System.Threading.Tasks.Task<int> Safe()
                    => System.Threading.Tasks.Task.FromResult(1);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture.ExternalCallback", [source]);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));
    }

    [Fact]
    public async Task AiReadDelegateActuals_AreBoundPerInvocationContext()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> Handle(
                    Query request,
                    System.Threading.CancellationToken token) => Invoke(Safe);
                private static System.Threading.Tasks.Task<int> Safe()
                    => System.Threading.Tasks.Task.FromResult(1);
                internal static System.Threading.Tasks.Task<int> Invoke(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
            }
            public sealed class NonAiWriter
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public NonAiWriter(IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => this.repository = repository;
                public System.Threading.Tasks.Task<int> Write()
                    => Handler.Invoke(() => repository.SaveChangesAsync());
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture.ContextSafe", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AiReadSelectFunctionsWithUnprovenSideEffects_ReportWritePath()
    {
        var dapper = CreateReference(
            "Dapper",
            "namespace Dapper { public static class SqlMapper { public static object Query(object db, string sql) => new(); } }");
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly object db;
                public Handler(object db) => this.db = db;
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                {
                    _ = Dapper.SqlMapper.Query(db, "SELECT nextval('sequence_name')");
                    _ = Dapper.SqlMapper.Query(db, "SELECT setval('sequence_name', 10)");
                    _ = Dapper.SqlMapper.Query(db, "SELECT lo_unlink(42)");
                    _ = Dapper.SqlMapper.Query(db, "SELECT mutate_business_state()");
                    _ = Dapper.SqlMapper.Query(db, "SELECT \"mutate_business_state\"()");
                    _ = Dapper.SqlMapper.Query(db, "SELECT \"custom_schema\".\"mutate_business_state\"()");
                    _ = Dapper.SqlMapper.Query(db, "SELECT COUNT(*), LOWER('SAFE') FROM devices");
                    _ = Dapper.SqlMapper.Query(db, "SELECT \"column\" FROM devices");
                    return System.Threading.Tasks.Task.FromResult(1);
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.Dapper.Fixture.FunctionSafety",
            [source],
            dapper);

        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));
    }

    [Fact]
    public async Task AiReadInterfaceHelperWriteAcrossFiles_ReportsWritePath()
    {
        var requestAndHandler = AiReadPrelude + AuthorizedQuery + """
            public interface IWriterHelper
            {
                System.Threading.Tasks.Task<int> Persist(
                    IIoT.SharedKernel.Repository.IRepository<object> repository);
            }
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IWriterHelper helper;
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public Handler(IWriterHelper helper, IIoT.SharedKernel.Repository.IRepository<object> repository)
                {
                    this.helper = helper;
                    this.repository = repository;
                }
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => helper.Persist(repository);
            }
            """;
        var helper = """
            public sealed class WriterHelper : IWriterHelper
            {
                public System.Threading.Tasks.Task<int> Persist(
                    IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => repository.SaveChangesAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [requestAndHandler, helper]);

        AssertSingle(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task AiReadGenericHelperWrite_ReportsWritePath()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public static class GenericWriter
            {
                public static System.Threading.Tasks.Task<int> Persist<T>(IIoT.SharedKernel.Repository.IRepository<T> repository)
                    => repository.SaveChangesAsync();
            }
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public Handler(IIoT.SharedKernel.Repository.IRepository<object> repository) => this.repository = repository;
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => GenericWriter.Persist(repository);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task InheritedAiReadHandlerWrite_ReportsWritePath()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public abstract class HandlerBase
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                protected HandlerBase(IIoT.SharedKernel.Repository.IRepository<object> repository) => this.repository = repository;
                public System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => repository.SaveChangesAsync();
            }
            public sealed class Handler : HandlerBase, IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                public Handler(IIoT.SharedKernel.Repository.IRepository<object> repository) : base(repository) { }
            }
            public sealed class GenericHandler<TQuery> : IIoT.SharedKernel.Messaging.IQueryHandler<TQuery, int>
                where TQuery : IIoT.Services.Contracts.IAiReadRequest<int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public GenericHandler(IIoT.SharedKernel.Repository.IRepository<object> repository)
                    => this.repository = repository;
                public System.Threading.Tasks.Task<int> Handle(TQuery request, System.Threading.CancellationToken token)
                    => repository.SaveChangesAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH004", diagnostic.Id));
    }

    [Fact]
    public async Task AiReadReadRepositoryCall_DoesNotReportWritePath()
    {
        var dapper = CreateReference(
            "Dapper",
            "namespace Dapper { public static class SqlMapper { public static object Query(object db, string sql) => new(); } }");
        var source = AiReadPrelude + AuthorizedQuery + """
            namespace Fixture
            {
                public static class SqlMapper
                {
                    public static object Query(object db, string sql) => new object();
                }
            }
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IReadRepository<object> repository;
                public Handler(IIoT.SharedKernel.Repository.IReadRepository<object> repository) => this.repository = repository;
                public async System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                {
                    _ = await repository.GetAsync();
                    _ = Dapper.SqlMapper.Query(repository, "/* DELETE is a comment */ SELECT 'UPDATE is data', 1;");
                    _ = Dapper.SqlMapper.Query(repository, "WITH source AS (SELECT 1) SELECT * FROM source");
                    _ = Fixture.SqlMapper.Query(repository, request.ToString()!);
                    return 1;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Dapper.Fixture.ReadPaths", [source], dapper);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NonAiReadHandlerRepositoryWrite_DoesNotReportAiReadDiagnostic()
    {
        var source = AiReadPrelude + """
            public sealed class RegularQuery { }
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<RegularQuery, int>
            {
                private readonly IIoT.SharedKernel.Repository.IRepository<object> repository;
                public Handler(IIoT.SharedKernel.Repository.IRepository<object> repository) => this.repository = repository;
                public System.Threading.Tasks.Task<int> Handle(RegularQuery request, System.Threading.CancellationToken token)
                    => repository.SaveChangesAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ProductionReferencesToKnownTestSupportAssemblies_ReportTestReference()
    {
        foreach (var assemblyName in new[]
                 {
                     "IIoT.CloudPlatform.TestKit",
                     "IIoT.CloudPlatform.PortFakes"
                 })
        {
            var testSupport = CreateReference(assemblyName, "public sealed class FakeDeviceFactory { }");

            var diagnostics = await AnalyzeAsync(
                "IIoT.ProductionService.Fixture",
                ["public sealed class ProductionType { }"],
                testSupport);

            AssertSingle(diagnostics, "CLOUDARCH006");
        }
    }

    [Fact]
    public async Task ProductionReferenceToTestingAssembly_ReportsTestReference()
    {
        var testing = CreateReference("IIoT.CloudPlatform.Testing", "public sealed class FakeDeviceFactory { }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.FutureComponent",
            ["public sealed class ProductionType { }"],
            testing);

        AssertSingle(diagnostics, "CLOUDARCH006");
    }

    [Fact]
    public async Task TestAssemblyReferenceIsNotAnalyzedAsProduction()
    {
        var testKit = CreateReference("IIoT.CloudPlatform.TestKit", "public sealed class FakeDeviceFactory { }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.CloudPlatform.WorkflowTests",
            ["public sealed class TestType { }"],
            testKit);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task PermissionProviderDirectCacheCall_ReportsSecurityReadCachePath()
    {
        var source = SecurityCachePrelude + """
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public PermissionProvider(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public System.Threading.Tasks.Task<int> ReadAsync() => cache.GetAsync("permissions");
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH007");
    }

    [Fact]
    public async Task DevicePermissionCacheAliasCall_ReportsSecurityReadCachePath()
    {
        var source = SecurityCachePrelude + """
            namespace Fixture
            {
                using CachePort = IIoT.Services.Contracts.ICacheService;
                public sealed class DevicePermissionService : IIoT.Services.Contracts.Authorization.IDevicePermissionService
                {
                    private readonly CachePort cache;
                    private readonly System.Func<System.Threading.Tasks.Task<int>> fieldRead;
                    private System.Func<System.Threading.Tasks.Task<int>> PropertyRead { get; }
                    public DevicePermissionService(CachePort cache)
                    {
                        this.cache = cache;
                        fieldRead = () => cache.GetAsync("device-permissions-field");
                        PropertyRead = () => cache.GetAsync("device-permissions-property");
                    }
                    public async System.Threading.Tasks.Task<int> ReadAsync()
                    {
                        _ = await fieldRead();
                        _ = await PropertyRead();
                        System.Func<System.Threading.Tasks.Task<int>> localRead =
                            () => cache.GetAsync("device-permissions-local");
                        return await localRead();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture", [source]);

        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH007", diagnostic.Id));
    }

    [Fact]
    public async Task DeviceIdentityHelperAcrossFiles_ReportsSecurityReadCachePath()
    {
        var root = SecurityCachePrelude + """
            public sealed class DeviceIdentityQueryService : IIoT.Services.Contracts.RecordQueries.IDeviceIdentityQueryService
            {
                private readonly CacheHelper helper;
                public DeviceIdentityQueryService(CacheHelper helper) => this.helper = helper;
                public System.Threading.Tasks.Task<int> ReadAsync() => helper.ReadAsync();
            }
            """;
        const string helper = """
            public sealed class CacheHelper
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public CacheHelper(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public System.Threading.Tasks.Task<int> ReadAsync() => cache.GetAsync("device-identity");
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Dapper.Fixture", [root, helper]);

        AssertSingle(diagnostics, "CLOUDARCH007");
    }

    [Fact]
    public async Task SecurityReadInvokedDelegateArguments_ResolveFactoryAndFailClosed()
    {
        var source = SecurityCachePrelude + """
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                private readonly System.Func<System.Threading.Tasks.Task<int>> unresolved;
                public PermissionProvider(
                    IIoT.Services.Contracts.ICacheService cache,
                    System.Func<System.Threading.Tasks.Task<int>> unresolved)
                {
                    this.cache = cache;
                    this.unresolved = unresolved;
                }
                public async System.Threading.Tasks.Task<int> ReadAsync()
                {
                    _ = await Invoke(Create(true) ?? Safe);
                    return await Invoke(unresolved);
                }
                private System.Func<System.Threading.Tasks.Task<int>> Create(bool useCache)
                    => useCache ? (() => cache.GetAsync("permission")) : Safe;
                private static System.Threading.Tasks.Task<int> Safe()
                    => System.Threading.Tasks.Task.FromResult(1);
                private static System.Threading.Tasks.Task<int> Invoke(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture.DelegateUnsafe", [source]);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH007", diagnostic.Id));
    }

    [Fact]
    public async Task SecurityReadInvokedDelegateArguments_WithProvenSafeFactories_DoNotReport()
    {
        var source = SecurityCachePrelude + """
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                public System.Threading.Tasks.Task<int> ReadAsync()
                    => Invoke(Create(true) ?? SafeOne);
                private static System.Func<System.Threading.Tasks.Task<int>> Create(bool first)
                    => first ? SafeOne : SafeTwo;
                private static System.Threading.Tasks.Task<int> SafeOne()
                    => System.Threading.Tasks.Task.FromResult(1);
                private static System.Threading.Tasks.Task<int> SafeTwo()
                    => System.Threading.Tasks.Task.FromResult(2);
                private static System.Threading.Tasks.Task<int> Invoke(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture.DelegateSafe", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SecurityReadExternalCallbacksAndDynamicInvoke_ResolveUnsafeAndSafeDelegates()
    {
        var source = SecurityCachePrelude + """
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public PermissionProvider(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public async System.Threading.Tasks.Task<int> ReadAsync()
                {
                    _ = await System.Threading.Tasks.Task.Run(() => cache.GetAsync("external-callback"));
                    System.Delegate unsafeDynamic =
                        new System.Func<System.Threading.Tasks.Task<int>>(() => cache.GetAsync("dynamic"));
                    _ = unsafeDynamic.DynamicInvoke();
                    _ = await System.Threading.Tasks.Task.Run(Safe);
                    System.Delegate safeDynamic =
                        new System.Func<System.Threading.Tasks.Task<int>>(Safe);
                    _ = safeDynamic.DynamicInvoke();
                    return 1;
                }
                private static System.Threading.Tasks.Task<int> Safe()
                    => System.Threading.Tasks.Task.FromResult(1);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture.ExternalCallback", [source]);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CLOUDARCH007", diagnostic.Id));
    }

    [Fact]
    public async Task SecurityReadDelegateActuals_AreBoundPerInvocationContext()
    {
        var source = SecurityCachePrelude + """
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                public System.Threading.Tasks.Task<int> ReadAsync() => Invoke(Safe);
                private static System.Threading.Tasks.Task<int> Safe()
                    => System.Threading.Tasks.Task.FromResult(1);
                internal static System.Threading.Tasks.Task<int> Invoke(
                    System.Func<System.Threading.Tasks.Task<int>> callback) => callback();
            }
            public sealed class NonSecurityReader
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public NonSecurityReader(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public System.Threading.Tasks.Task<int> Read()
                    => PermissionProvider.Invoke(() => cache.GetAsync("non-security"));
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture.ContextSafe", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SecurityReadInterfaceHelperAcrossFiles_ReportsSecurityReadCachePath()
    {
        var root = SecurityCachePrelude + """
            public interface IReadHelper { System.Threading.Tasks.Task<int> ReadAsync(); }
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                private readonly IReadHelper helper;
                public PermissionProvider(IReadHelper helper) => this.helper = helper;
                public System.Threading.Tasks.Task<int> ReadAsync() => helper.ReadAsync();
            }
            """;
        const string helper = """
            public sealed class ReadHelper : IReadHelper
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public ReadHelper(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public System.Threading.Tasks.Task<int> ReadAsync() => cache.GetAsync("permissions");
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture", [root, helper]);

        AssertSingle(diagnostics, "CLOUDARCH007");
    }

    [Fact]
    public async Task SecurityReadUsingAuthoritativeStore_DoesNotReportCachePath()
    {
        var source = SecurityCachePrelude + """
            public interface IAuthoritativeStore { System.Threading.Tasks.Task<int> ReadAsync(); }
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                private readonly IAuthoritativeStore store;
                public PermissionProvider(IAuthoritativeStore store) => this.store = store;
                public System.Threading.Tasks.Task<int> ReadAsync() => store.ReadAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NonSecurityServiceMayUseValueCache()
    {
        var source = SecurityCachePrelude + """
            public sealed class RecipeQuery
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public RecipeQuery(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public System.Threading.Tasks.Task<int> ReadAsync() => cache.GetAsync("recipes");
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SecurityReadGenericHelperAcrossFiles_ReportsSecurityReadCachePath()
    {
        var root = SecurityCachePrelude + """
            public interface IReadHelper<T> { System.Threading.Tasks.Task<T> ReadAsync(); }
            public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
            {
                private readonly IReadHelper<int> helper;
                public PermissionProvider(IReadHelper<int> helper) => this.helper = helper;
                public System.Threading.Tasks.Task<int> ReadAsync() => helper.ReadAsync();
            }
            """;
        const string helper = """
            public sealed class GenericReadHelper<T> : IReadHelper<T>
            {
                private readonly IIoT.Services.Contracts.ICacheService cache;
                public GenericReadHelper(IIoT.Services.Contracts.ICacheService cache) => this.cache = cache;
                public async System.Threading.Tasks.Task<T> ReadAsync()
                {
                    _ = await cache.GetAsync("permissions");
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture", [root, helper]);

        AssertSingle(diagnostics, "CLOUDARCH007");
    }

    [Fact]
    public async Task SameNamedNonContractCacheService_DoesNotReportSecurityReadCachePath()
    {
        var source = SecurityCachePrelude + """
            namespace Fixture
            {
                public interface ICacheService { System.Threading.Tasks.Task<int> GetAsync(string key); }
                public sealed class PermissionProvider : IIoT.Services.Contracts.Authorization.IPermissionProvider
                {
                    private readonly ICacheService cache;
                    public PermissionProvider(ICacheService cache) => this.cache = cache;
                    public System.Threading.Tasks.Task<int> ReadAsync() => cache.GetAsync("local-value");
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.EntityFrameworkCore.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ProductionReadJwtTokenCall_ReportsUnsignedJwtParsing()
    {
        var source = JwtPrelude + """
            public sealed class TokenReader
            {
                public object Read(string token)
                    => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.HttpApi.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH008");
    }

    [Fact]
    public async Task AliasedReadJwtTokenCall_ReportsUnsignedJwtParsing()
    {
        var source = JwtPrelude + """
            namespace Fixture
            {
                using TokenHandler = System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler;
                public static class TokenHelper
                {
                    public static object Read(string token)
                    {
                        var handler = new TokenHandler();
                        return handler.ReadJwtToken(token);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Infrastructure.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH008");
    }

    [Fact]
    public async Task TestAssemblyMayInspectServerIssuedJwt()
    {
        var source = JwtPrelude + """
            public sealed class TokenAssertion
            {
                public object Read(string token)
                    => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.CloudPlatform.EndToEndTests", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ValidatedJwtApiDoesNotReportUnsignedParsing()
    {
        var source = JwtPrelude + """
            public sealed class TokenReader
            {
                public object Read(string token)
                    => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(token);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.HttpApi.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SameNamedJwtHandlerOutsideIdentityModelNamespace_DoesNotReportUnsignedParsing()
    {
        var source = JwtPrelude + """
            namespace Fixture
            {
                public sealed class JwtSecurityTokenHandler
                {
                    public object ReadJwtToken(string token) => new object();
                }

                public sealed class TokenReader
                {
                    public object Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.HttpApi.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TypeInRetiredServicesCommonNamespace_ReportsRetiredNamespace()
    {
        const string source = "namespace IIoT.Services.Common.Legacy; public sealed class ShadowAdapter { }";

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH009");
    }

    [Fact]
    public async Task AdjacentServicesCommonName_DoesNotReportRetiredNamespace()
    {
        const string source = "namespace IIoT.Services.Commonplace; public sealed class CurrentService { }";

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ConnectionResourceLiteralOutsideAuthority_ReportsResourceLiteral()
    {
        const string source = "namespace Fixture; public sealed class ResourceConsumer { public string Name => \"iiot-db\"; }";

        var diagnostics = await AnalyzeAsync("IIoT.AppHost.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH010");
    }

    [Fact]
    public async Task ConnectionResourceAuthorityAndSameNamedAdjacentType_AreDistinguishedSemantically()
    {
        const string authority = """
            namespace IIoT.SharedKernel.Configuration;
            public static class ConnectionResourceNames
            {
                public const string IiotDatabase = "iiot-db";
                public const string EventBus = "eventbus";
            }
            """;
        const string adjacent = """
            namespace Fixture;
            public static class ConnectionResourceNames
            {
                public const string Value = "eventbus";
            }
            """;

        var validDiagnostics = await AnalyzeAsync("IIoT.SharedKernel.Fixture", [authority]);
        var invalidDiagnostics = await AnalyzeAsync("IIoT.SharedKernel.Fixture", [adjacent]);

        Assert.Empty(validDiagnostics);
        AssertSingle(invalidDiagnostics, "CLOUDARCH010");
    }

    private const string JwtPrelude = """
        namespace System.IdentityModel.Tokens.Jwt
        {
            public sealed class JwtSecurityTokenHandler
            {
                public object ReadJwtToken(string token) => new object();
                public object ValidateToken(string token) => new object();
            }
        }
        """;

    private const string SecurityCachePrelude = """
        namespace IIoT.Services.Contracts
        {
            public interface ICacheService
            {
                System.Threading.Tasks.Task<int> GetAsync(string key);
            }
        }

        namespace IIoT.Services.Contracts.Authorization
        {
            public interface IPermissionProvider { System.Threading.Tasks.Task<int> ReadAsync(); }
            public interface IDevicePermissionService { System.Threading.Tasks.Task<int> ReadAsync(); }
        }

        namespace IIoT.Services.Contracts.RecordQueries
        {
            public interface IDeviceIdentityQueryService { System.Threading.Tasks.Task<int> ReadAsync(); }
        }
        """;

    private const string AuthorizedQuery = """
        [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
        public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }
        """;

    private const string AiReadScopeContract = """
        namespace IIoT.Services.Contracts.Authorization
        {
            public enum AiReadScopeKind { Global, Delegated, Invalid }

            public interface IAiReadScopeAccessor
                : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
            {
                string Caller { get; }
                AiReadScopeKind ScopeKind { get; }
                System.Guid? DelegatedUserId { get; }
                System.Collections.Generic.IReadOnlyCollection<System.Guid>? DelegatedDeviceIds { get; }
            }
        }
        """;

    private const string SpecificationContract = """
        public interface ISpecification<T>
        {
            System.Linq.Expressions.Expression<System.Func<T, bool>>? FilterCondition { get; }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string assemblyName,
        IReadOnlyList<string> sources,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CreateCompilation(assemblyName, sources, additionalReferences);
        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilerErrors.Length == 0,
            "Fixture compiler errors:" + Environment.NewLine + string.Join(Environment.NewLine, compilerErrors.Select(item => item.ToString())));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new CloudArchitectureAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        IReadOnlyList<string> sources,
        params MetadataReference[] additionalReferences)
    {
        var syntaxTrees = sources.Select((source, index) =>
            CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: $"Fixture{index + 1}.cs"));
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            PlatformReferences.Value.AddRange(additionalReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference CreateReference(string assemblyName, string source)
    {
        var compilation = CreateCompilation(assemblyName, [source]);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            "Reference compiler errors:" + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new(() =>
    {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Contains(
                $"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}Microsoft.NETCore.App{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    });

    private static void AssertSingle(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static void AssertSingleById(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        var diagnostic = Assert.Single(diagnostics.Where(candidate => candidate.Id == id));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
