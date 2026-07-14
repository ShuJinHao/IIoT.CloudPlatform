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

        namespace IIoT.SharedKernel.Repository
        {
            public interface IReadRepository<T>
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
            ["CLOUDARCH001", "CLOUDARCH002", "CLOUDARCH003", "CLOUDARCH004", "CLOUDARCH005", "CLOUDARCH006"],
            diagnostics.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal));
        Assert.All(diagnostics, descriptor =>
        {
            Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
            Assert.True(descriptor.IsEnabledByDefault);
        });
    }

    [Fact]
    public async Task CoreReferencingService_ReportsLayerDiagnostic()
    {
        var service = CreateReference("IIoT.ProductionService", "public sealed class ServiceMarker { }");

        var diagnostics = await AnalyzeAsync("IIoT.Core.Fixture", ["public sealed class CoreType { }"], service);

        AssertSingle(diagnostics, "CLOUDARCH001");
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
                public Handler(IIoT.SharedKernel.Repository.IRepository<object> repository) => this.repository = repository;
                public async System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                    => await repository.SaveChangesAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH004");
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
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

        AssertSingle(diagnostics, "CLOUDARCH004");
    }

    [Fact]
    public async Task AiReadReadRepositoryCall_DoesNotReportWritePath()
    {
        var source = AiReadPrelude + AuthorizedQuery + """
            public sealed class Handler : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
            {
                private readonly IIoT.SharedKernel.Repository.IReadRepository<object> repository;
                public Handler(IIoT.SharedKernel.Repository.IReadRepository<object> repository) => this.repository = repository;
                public async System.Threading.Tasks.Task<int> Handle(Query request, System.Threading.CancellationToken token)
                {
                    _ = await repository.GetAsync();
                    return 1;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.ProductionService.Fixture", [source]);

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
    public async Task ProductionReferenceToTestKit_ReportsTestReference()
    {
        var testKit = CreateReference("IIoT.CloudPlatform.TestKit", "public sealed class FakeDeviceFactory { }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.ProductionService.Fixture",
            ["public sealed class ProductionType { }"],
            testKit);

        AssertSingle(diagnostics, "CLOUDARCH006");
    }

    [Fact]
    public async Task ProductionReferenceToTestingAssembly_ReportsTestReference()
    {
        var testing = CreateReference("IIoT.CloudPlatform.Testing", "public sealed class FakeDeviceFactory { }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.FutureProductionComponent",
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

    private const string AuthorizedQuery = """
        [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
        public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }
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
}
