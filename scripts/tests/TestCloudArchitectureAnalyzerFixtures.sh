#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
analyzer_project="$repo_root/src/analyzers/IIoT.CloudPlatform.Analyzers/IIoT.CloudPlatform.Analyzers.csproj"
if [[ -n "${CLOUD_ARCH_FIXTURE_ROOT:-}" ]]; then
    fixture_root="$CLOUD_ARCH_FIXTURE_ROOT"
    rm -rf "$fixture_root"
    mkdir -p "$fixture_root"
else
    fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/iiot-cloud-architecture-fixtures.XXXXXX")"
    trap 'rm -rf "$fixture_root"' EXIT
fi
fixture_root="$(cd "$fixture_root" && pwd -P)"

cp "$repo_root/.globalconfig" "$fixture_root/.globalconfig"

cat > "$fixture_root/Directory.Build.props" <<EOF
<Project>
  <PropertyGroup>
    <RestoreDisableParallel>true</RestoreDisableParallel>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <CloudArchitectureRepositoryRoot>$fixture_root/</CloudArchitectureRepositoryRoot>
    <CloudArchitectureProjectIdentity>\$([MSBuild]::MakeRelative('\$(CloudArchitectureRepositoryRoot)', '\$(MSBuildProjectFullPath)'))</CloudArchitectureProjectIdentity>
  </PropertyGroup>
  <ItemGroup Condition="'\$(AttachCloudArchitectureAnalyzer)' == 'true'">
    <ProjectReference Include="$analyzer_project"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <CompilerVisibleProperty Include="CloudArchitectureProjectIdentity" />
  </ItemGroup>
  <Target Name="WriteCloudArchitectureManagedProjectReferences"
          DependsOnTargets="ResolveProjectReferences"
          BeforeTargets="CoreCompile"
          Condition="'\$(AttachCloudArchitectureAnalyzer)' == 'true'">
    <PropertyGroup>
      <_CloudArchitectureManagedReferencesFile>\$(IntermediateOutputPath)CloudArchitectureManagedProjectReferences.txt</_CloudArchitectureManagedReferencesFile>
    </PropertyGroup>
    <ItemGroup>
      <_CloudArchitectureManagedReference Include="@(_ResolvedProjectReferencePaths)"
                                          Condition="'%(MSBuildSourceProjectFile)' != ''">
        <StableProjectIdentity>\$([MSBuild]::MakeRelative('\$(CloudArchitectureRepositoryRoot)', '%(MSBuildSourceProjectFile)'))</StableProjectIdentity>
      </_CloudArchitectureManagedReference>
    </ItemGroup>
    <WriteLinesToFile File="\$(_CloudArchitectureManagedReferencesFile)"
                      Lines="@(_CloudArchitectureManagedReference->'%(ReferenceAssembly)&#x9;%(FullPath)&#x9;%(StableProjectIdentity)')"
                      Overwrite="true"
                      WriteOnlyWhenDifferent="true" />
    <ItemGroup>
      <AdditionalFiles Include="\$(_CloudArchitectureManagedReferencesFile)" />
    </ItemGroup>
  </Target>
</Project>
EOF

write_project() {
    local directory="$1"
    local assembly_name="$2"
    local attach_analyzer="$3"
    local project_references="${4:-}"
    local project_properties="${5:-}"

    mkdir -p "$fixture_root/$directory"
    cat > "$fixture_root/$directory/$directory.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$assembly_name</AssemblyName>
    <AttachCloudArchitectureAnalyzer>$attach_analyzer</AttachCloudArchitectureAnalyzer>
    $project_properties
  </PropertyGroup>
  $project_references
</Project>
EOF
}

build_valid() {
    local project="$1"
    local output
    if ! output="$(dotnet build "$project" -c Release --disable-build-servers --nologo 2>&1)"; then
        printf '%s\n' "$output" >&2
        printf 'valid architecture fixture unexpectedly failed: %s\n' "$project" >&2
        exit 1
    fi

    if grep -Eq 'CLOUDARCH[0-9]{3}' <<<"$output"; then
        printf '%s\n' "$output" >&2
        printf 'valid architecture fixture emitted CLOUDARCH diagnostic: %s\n' "$project" >&2
        exit 1
    fi
}

build_invalid() {
    local project="$1"
    local expected_id="$2"
    local output
    local status

    set +e
    output="$(dotnet build "$project" -c Release --disable-build-servers --nologo 2>&1)"
    status=$?
    set -e

    if [[ $status -eq 0 ]]; then
        printf '%s\n' "$output" >&2
        printf 'invalid architecture fixture unexpectedly succeeded: %s\n' "$project" >&2
        exit 1
    fi

    local actual_ids
    actual_ids="$(grep -Eo 'CLOUDARCH[0-9]{3}' <<<"$output" | sort -u | paste -sd, -)"
    if [[ "$actual_ids" != "$expected_id" ]] || ! grep -q "error $expected_id" <<<"$output"; then
        printf '%s\n' "$output" >&2
        printf 'invalid fixture expected only %s but observed %s: %s\n' \
            "$expected_id" "${actual_ids:-<none>}" "$project" >&2
        exit 1
    fi
}

write_project "Valid" "IIoT.ProductionService.FixtureValid" "true"
cat > "$fixture_root/Valid/Valid.cs" <<'EOF'
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
    public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
}

namespace IIoT.Services.CrossCutting.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAiReadAttribute(string permission) : Attribute;
}

namespace IIoT.SharedKernel.Messaging
{
    public interface IQueryHandler<in TQuery, TResponse>
    {
        Task<TResponse> Handle(TQuery request, CancellationToken cancellationToken);
    }
}

namespace IIoT.SharedKernel.Architecture
{
    public interface IReadOnlyQueryPort { }
}

namespace Fixture
{
    [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
    public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }

    public interface IReadPort : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
    {
        Task<int> CountAsync(CancellationToken cancellationToken);
    }

    public sealed class Handler(IReadPort readPort)
        : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
    {
        public Task<int> Handle(Query request, CancellationToken cancellationToken)
            => readPort.CountAsync(cancellationToken);
    }
}
EOF

write_project "OuterService" "IIoT.ProductionService.FixtureDependency" "false"
printf '%s\n' 'namespace Fixture; public sealed class ServiceMarker { }' > "$fixture_root/OuterService/ServiceMarker.cs"
write_project \
    "Invalid001" \
    "IIoT.Core.FixtureInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../OuterService/OuterService.csproj" /></ItemGroup>'
printf '%s\n' 'namespace Fixture; public sealed class CoreType { }' > "$fixture_root/Invalid001/CoreType.cs"

write_project "Invalid002" "IIoT.ProductionService.FixtureInvalid002" "true"
cat > "$fixture_root/Invalid002/WrongAggregate.cs" <<'EOF'
namespace IIoT.SharedKernel.Domain
{
    public interface IAggregateRoot { }
}

namespace Fixture
{
    public sealed class ProjectionPretendingToBeAggregate : IIoT.SharedKernel.Domain.IAggregateRoot { }
}
EOF

write_project "DapperStub" "Dapper" "false"
cat > "$fixture_root/DapperStub/SqlMapper.cs" <<'EOF'
namespace Dapper;
public static class SqlMapper
{
    public static int Execute(object connection, string sql) => 0;
    public static object Query(object connection, string sql) => new();
}
EOF

write_project \
    "ValidDataWorker" \
    "IIoT.DataWorker" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/ValidDataWorker/Program.cs" <<'EOF'
public static class Program
{
    public static int Check(object connection) => Dapper.SqlMapper.Execute(connection, "select 1");
}
EOF

write_project \
    "ValidReadOnlySql" \
    "IIoT.Dapper.FixtureValidReadOnlySql" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/ValidReadOnlySql/ReadOnlyHandler.cs" <<'EOF'
namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
}

namespace IIoT.SharedKernel.Messaging
{
    public interface IQueryHandler<in TQuery, TResponse>
    {
        System.Threading.Tasks.Task<TResponse> Handle(TQuery request, System.Threading.CancellationToken cancellationToken);
    }
}

public sealed class ReadOnlyHandler<TQuery>(object connection)
    : IIoT.SharedKernel.Messaging.IQueryHandler<TQuery, int>
    where TQuery : IIoT.Services.Contracts.IAiReadRequest<int>
{
    public System.Threading.Tasks.Task<int> Handle(TQuery request, System.Threading.CancellationToken cancellationToken)
    {
        _ = Dapper.SqlMapper.Query(connection, "/* DELETE */ WITH source AS (SELECT 1) SELECT * FROM source;");
        return System.Threading.Tasks.Task.FromResult(1);
    }
}
EOF

write_project \
    "ValidMigrationHost" \
    "IIoT.MigrationWorkApp" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/ValidMigrationHost/MigrationRunner.cs" <<'EOF'
namespace IIoT.MigrationWorkApp;
public sealed class MigrationRunner
{
    public int Run(object connection) => Dapper.SqlMapper.Execute(connection, "select 1");
}
EOF

write_project \
    "ValidHttpApiAdapter" \
    "IIoT.HttpApi" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/ValidHttpApiAdapter/PostgresReadinessHealthCheck.cs" <<'EOF'
namespace IIoT.HttpApi.Infrastructure;
public sealed class PostgresReadinessHealthCheck
{
    public int Check(object connection) => Dapper.SqlMapper.Execute(connection, "select 1");
}
EOF

write_project \
    "Invalid007" \
    "IIoT.DataWorker" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/Invalid007/AdjacentWorker.cs" <<'EOF'
namespace IIoT.DataWorker;
public sealed class AdjacentWorker
{
    public int Run(object connection) => Dapper.SqlMapper.Execute(connection, "select 1");
}
EOF

write_project \
    "Invalid008" \
    "IIoT.HttpApi" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/Invalid008/AdjacentController.cs" <<'EOF'
namespace IIoT.HttpApi;
public sealed class AdjacentController
{
    public int Run(object connection) => Dapper.SqlMapper.Execute(connection, "select 1");
}
EOF
write_project \
    "Invalid003" \
    "IIoT.ProductionService.FixtureInvalid003" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/Invalid003/DirectDatabaseAccess.cs" <<'EOF'
using ExecuteApi = Dapper.SqlMapper;
namespace Fixture;
public sealed class DirectDatabaseAccess
{
    public int Run(object connection) => ExecuteApi.Execute(connection, "delete from devices");
}
EOF

write_project \
    "Invalid004" \
    "IIoT.Dapper.FixtureInvalid004" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/Invalid004/Contracts.cs" <<'EOF'
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
    public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
}

namespace IIoT.Services.CrossCutting.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAiReadAttribute(string permission) : Attribute;
}

namespace IIoT.SharedKernel.Messaging
{
    public interface IQueryHandler<in TQuery, TResponse>
    {
        Task<TResponse> Handle(TQuery request, CancellationToken cancellationToken);
    }
}

namespace IIoT.SharedKernel.Repository
{
    public interface IRepository<T>
    {
        Task<int> SaveChangesAsync();
    }
}
EOF
cat > "$fixture_root/Invalid004/Handler.cs" <<'EOF'
using System.Threading;
using System.Threading.Tasks;

[IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }

public sealed class Handler(
    WriterHelper helper,
    IIoT.SharedKernel.Repository.IRepository<object> repository)
    : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
{
    private readonly System.Func<Task<int>> fieldWrite = () => helper.Persist(repository);
    private System.Func<Task<int>> PropertyWrite { get; } = () => helper.Persist(repository);

    public async Task<int> Handle(Query request, CancellationToken cancellationToken)
    {
        _ = await fieldWrite();
        _ = await PropertyWrite();
        System.Func<Task<int>> localWrite = () => helper.Persist(repository);
        _ = await localWrite();
        _ = await Task.Run(() => helper.Persist(repository));
        System.Delegate dynamicWrite = new System.Func<Task<int>>(
            () => helper.Persist(repository));
        _ = dynamicWrite.DynamicInvoke();
        _ = helper.RawWrite(new object());
        _ = helper.QuotedFunctionWrite(new object());
        return 1;
    }
}

public sealed class GenericHandler<TQuery>(IIoT.SharedKernel.Repository.IRepository<object> repository)
    : IIoT.SharedKernel.Messaging.IQueryHandler<TQuery, int>
    where TQuery : IIoT.Services.Contracts.IAiReadRequest<int>
{
    public Task<int> Handle(TQuery request, CancellationToken cancellationToken)
        => repository.SaveChangesAsync();
}
EOF
cat > "$fixture_root/Invalid004/WriterHelper.cs" <<'EOF'
using System.Threading.Tasks;
public sealed class WriterHelper
{
    public Task<int> Persist(IIoT.SharedKernel.Repository.IRepository<object> repository)
        => repository.SaveChangesAsync();

    public object RawWrite(object connection)
        => Dapper.SqlMapper.Query(connection, "WITH changed AS (DELETE FROM device RETURNING id) SELECT * FROM changed");

    public object QuotedFunctionWrite(object connection)
        => Dapper.SqlMapper.Query(connection, "SELECT \"custom_schema\".\"mutate_business_state\"()");
}
EOF

write_project "CrossProjectAiReadContracts" "IIoT.ProductionService.FixtureCrossProjectContracts" "true"
cat > "$fixture_root/CrossProjectAiReadContracts/Contracts.cs" <<'EOF'
namespace IIoT.SharedKernel.Architecture
{
    public interface IReadOnlyQueryPort { }
}

namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
    public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
}

namespace IIoT.Services.CrossCutting.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAiReadAttribute(string permission) : System.Attribute;
}

namespace IIoT.SharedKernel.Messaging
{
    public interface IQueryHandler<in TQuery, TResponse>
    {
        System.Threading.Tasks.Task<TResponse> Handle(
            TQuery request,
            System.Threading.CancellationToken cancellationToken);
    }
}

namespace Fixture
{
    public interface IExternalReadPort : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
    {
        System.Threading.Tasks.Task<int> ReadAsync(
            System.Threading.CancellationToken cancellationToken);
    }

    [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
    public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }

    public sealed class Handler(IExternalReadPort port)
        : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
    {
        public System.Threading.Tasks.Task<int> Handle(
            Query request,
            System.Threading.CancellationToken cancellationToken)
            => port.ReadAsync(cancellationToken);
    }
}
EOF

write_project \
    "InvalidCrossProjectAiRead" \
    "IIoT.Dapper.FixtureCrossProjectInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidCrossProjectAiRead/ExternalReadPort.cs" <<'EOF'
namespace Fixture;

public sealed class ExternalReadPort(object connection) : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
    {
        var affected = Dapper.SqlMapper.Execute(connection, "delete from devices");
        return System.Threading.Tasks.Task.FromResult(affected);
    }
}
EOF

write_project \
    "CrossAssemblyStaticWriter" \
    "IIoT.Infrastructure.FixtureCrossAssemblyWriter" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/CrossAssemblyStaticWriter/StaticWriter.cs" <<'EOF'
namespace IIoT.SharedKernel.Messaging
{
    public interface ICommand<out TResponse> { }
}

namespace CrossAssembly
{
public static class SafeStaticField
{
    public static readonly int Value = 1;
}

public static class UnsafeStaticField
{
    public static readonly int Value =
        Dapper.SqlMapper.Execute(new object(), "delete from direct_static_field");
}

public static class NativeBoundary
{
    [System.Runtime.InteropServices.DllImport("native-writer")]
    public static extern int Mutate();

    public static int EmptyBody() { return 0; }

    public static int AutoValue { get; set; }
}

public interface IOpenReader
{
    int Read();
}

public sealed class SafeOpenReader : IOpenReader
{
    public int Read() => 1;
}

public static class StaticWriter
{
    public static int Write(object connection)
        => Dapper.SqlMapper.Execute(connection, "delete from devices");

    public static int InterfaceWrite(object connection)
        => RunInterface(new DapperWriter(connection));

    public static int VirtualWrite(object connection)
        => RunVirtual(new DerivedWriter(connection));

    public static async System.Threading.Tasks.Task<int> CallbackWrite(object connection)
        => await System.Threading.Tasks.Task.Run(
            () => Dapper.SqlMapper.Execute(connection, "delete from callback"));

    public static object SqlFunctionWrite(object connection)
        => Dapper.SqlMapper.Query(
            connection,
            "SELECT \"custom_schema\".\"mutate_business_state\"()");

    public static int CommandWrite()
        => CommandSender.Send(new MutatingCommand());

    public static int CollectionInitializerWrite(object connection)
        => new EvilCollection(connection) { 1 }.Count;

    public static int InitializerWrite(object connection)
        => new InitializerWriter(connection).Value;

    public static int StaticInitializerWrite()
        => StaticInitializerWriter.Value;

    public static int SafeRead() => 1;

    public static int SafeDispatchRead()
        => RunSafeInterface(new SafeReader()) + RunSafeVirtual(new SafeDerivedReader());

    public static int OpenDispatchRead(IOpenReader reader)
        => reader.Read();

    private static int RunInterface(IWriter writer) => writer.Write();
    private static int RunVirtual(WriterBase writer) => writer.Write();
    private static int RunSafeInterface(ISafeReader reader) => reader.Read();
    private static int RunSafeVirtual(SafeReaderBase reader) => reader.Read();

    private interface IWriter { int Write(); }
    private sealed class DapperWriter(object connection) : IWriter
    {
        public int Write()
            => Dapper.SqlMapper.Execute(connection, "delete from interface_dispatch");
    }

    private abstract class WriterBase
    {
        public abstract int Write();
    }

    private sealed class DerivedWriter(object connection) : WriterBase
    {
        public override int Write()
            => Dapper.SqlMapper.Execute(connection, "delete from virtual_dispatch");
    }

    private interface ISafeReader { int Read(); }
    private sealed class SafeReader : ISafeReader { public int Read() => 1; }

    private class SafeReaderBase { public virtual int Read() => 1; }
    private sealed class SafeDerivedReader : SafeReaderBase { public override int Read() => 2; }

    private sealed class InitializerWriter(object connection)
    {
        private readonly int initialized =
            Dapper.SqlMapper.Execute(connection, "delete from instance_initializer");
        public int Value => initialized;
    }

    private static class StaticInitializerWriter
    {
        internal static readonly int Value =
            Dapper.SqlMapper.Execute(new object(), "delete from static_initializer");
    }

    private sealed class MutatingCommand : IIoT.SharedKernel.Messaging.ICommand<int>;

    private sealed class EvilCollection(object connection) : System.Collections.IEnumerable
    {
        internal int Count { get; private set; }

        public void Add(int value)
        {
            _ = Dapper.SqlMapper.Execute(connection, "delete from collection_initializer");
            Count += value;
        }

        public System.Collections.IEnumerator GetEnumerator()
            => System.Array.Empty<int>().GetEnumerator();
    }

    private static class CommandSender
    {
        internal static int Send<TResponse>(IIoT.SharedKernel.Messaging.ICommand<TResponse> command)
            => 0;
    }
}
}
EOF

write_project \
    "InvalidCrossAssemblySummary" \
    "IIoT.Dapper.FixtureCrossAssemblySummaryInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../CrossAssemblyStaticWriter/CrossAssemblyStaticWriter.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidCrossAssemblySummary/ExternalReadPort.cs" <<'EOF'
namespace Fixture;

public sealed class CrossAssemblyReadPort(object connection) : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.Write(connection));
}
EOF

write_cross_summary_consumer() {
    local directory="$1"
    local assembly_name="$2"
    local return_expression="$3"

    write_project \
        "$directory" \
        "$assembly_name" \
        "true" \
        '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../CrossAssemblyStaticWriter/CrossAssemblyStaticWriter.csproj" /></ItemGroup>'
    cat > "$fixture_root/$directory/ExternalReadPort.cs" <<EOF
namespace Fixture;

public sealed class ${directory}ReadPort(object connection) : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => $return_expression;
}
EOF
}

write_cross_summary_consumer \
    "InvalidCrossAssemblyDispatchSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyDispatchInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.InterfaceWrite(connection))'
write_cross_summary_consumer \
    "InvalidCrossAssemblyVirtualSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyVirtualInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.VirtualWrite(connection))'
write_cross_summary_consumer \
    "InvalidCrossAssemblyCallbackSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyCallbackInvalid" \
    'CrossAssembly.StaticWriter.CallbackWrite(connection)'
write_cross_summary_consumer \
    "InvalidCrossAssemblySqlSummary" \
    "IIoT.Dapper.FixtureCrossAssemblySqlInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.SqlFunctionWrite(connection) is null ? 0 : 1)'
write_cross_summary_consumer \
    "InvalidCrossAssemblyCommandSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyCommandInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.CommandWrite())'
write_cross_summary_consumer \
    "InvalidCrossAssemblyCollectionInitializerSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyCollectionInitializerInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.CollectionInitializerWrite(connection))'
write_cross_summary_consumer \
    "InvalidCrossAssemblyInitializerSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyInitializerInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.InitializerWrite(connection))'
write_cross_summary_consumer \
    "InvalidCrossAssemblyStaticInitializerSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyStaticInitializerInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.StaticInitializerWrite())'
write_cross_summary_consumer \
    "InvalidCrossAssemblyDirectStaticFieldSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyDirectStaticFieldInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.UnsafeStaticField.Value)'
write_cross_summary_consumer \
    "InvalidCrossAssemblyNativeBoundarySummary" \
    "IIoT.Dapper.FixtureCrossAssemblyNativeBoundaryInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.NativeBoundary.Mutate())'
write_cross_summary_consumer \
    "InvalidCrossAssemblyOpenDispatchSummary" \
    "IIoT.Dapper.FixtureCrossAssemblyOpenDispatchInvalid" \
    'System.Threading.Tasks.Task.FromResult(CrossAssembly.StaticWriter.OpenDispatchRead(new CrossAssembly.SafeOpenReader()))'

write_project \
    "ValidCrossAssemblySummary" \
    "IIoT.Dapper.FixtureCrossAssemblySummaryValid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../CrossAssemblyStaticWriter/CrossAssemblyStaticWriter.csproj" /></ItemGroup>'
cat > "$fixture_root/ValidCrossAssemblySummary/ExternalReadPort.cs" <<'EOF'
namespace Fixture;

public sealed class SafeSummaryReadPort : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(
            CrossAssembly.StaticWriter.SafeRead() +
            CrossAssembly.StaticWriter.SafeDispatchRead() +
            CrossAssembly.NativeBoundary.EmptyBody() +
            CrossAssembly.NativeBoundary.AutoValue +
            CrossAssembly.SafeStaticField.Value);
}
EOF

write_project "MissingEffectSummary" "IIoT.Infrastructure.FixtureMissingEffectSummary" "false"
cat > "$fixture_root/MissingEffectSummary/StaticReader.cs" <<'EOF'
namespace MissingSummary;
public static class StaticReader
{
    public static int Read() => 1;
}
EOF

write_project \
    "InvalidMissingEffectSummary" \
    "IIoT.Dapper.FixtureMissingEffectSummaryInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../MissingEffectSummary/MissingEffectSummary.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidMissingEffectSummary/ExternalReadPort.cs" <<'EOF'
namespace Fixture;
public sealed class MissingSummaryReadPort : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(MissingSummary.StaticReader.Read());
}
EOF

old_method_id='OldSummary.StaticReader::Read`0()->System.Int32'
old_effect_digest="$(printf '%s\tsafe' "$old_method_id" | shasum -a 256 | awk '{print $1}')"
old_source_identity="$(printf '%s' 'OldEffectSummary/OldEffectSummary.csproj' | shasum -a 256 | awk '{print $1}')"
write_project "OldEffectSummary" "IIoT.Infrastructure.FixtureOldEffectSummary" "false"
cat > "$fixture_root/OldEffectSummary/OldSummary.cs" <<EOF
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Manifest",
    "1|1|$old_effect_digest")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.SourceIdentity",
    "$old_source_identity")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Method",
    "$old_method_id\tsafe")]

namespace OldSummary;
public static class StaticReader
{
    public static int Read() => 1;
}
EOF

write_project \
    "InvalidOldEffectSummary" \
    "IIoT.Dapper.FixtureOldEffectSummaryInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../OldEffectSummary/OldEffectSummary.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidOldEffectSummary/ExternalReadPort.cs" <<'EOF'
namespace Fixture;
public sealed class OldSummaryReadPort : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(OldSummary.StaticReader.Read());
}
EOF

spoof_method_id='SpoofSummary.StaticWriter::Write`0(0:System.Object)->System.Int32'
spoof_effect_digest="$(printf '%s\tsafe' "$spoof_method_id" | shasum -a 256 | awk '{print $1}')"
spoof_source_identity="$(printf '%s' 'SpoofedEffectSummary/SpoofedEffectSummary.csproj' | shasum -a 256 | awk '{print $1}')"
write_project \
    "SpoofedEffectSummary" \
    "IIoT.Infrastructure.FixtureSpoofedEffectSummary" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/SpoofedEffectSummary/SpoofedSummary.cs" <<EOF
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Manifest",
    "2|1|$spoof_effect_digest")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.SourceIdentity",
    "$spoof_source_identity")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Method",
    "$spoof_method_id\tsafe")]

namespace SpoofSummary;
public static class StaticWriter
{
    public static int Write(object connection)
        => Dapper.SqlMapper.Execute(connection, "delete from spoofed_summary");
}
EOF

write_project \
    "InvalidSpoofedEffectSummary" \
    "IIoT.Dapper.FixtureSpoofedEffectSummaryInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../SpoofedEffectSummary/SpoofedEffectSummary.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidSpoofedEffectSummary/ExternalReadPort.cs" <<'EOF'
namespace Fixture;
public sealed class SpoofedSummaryReadPort(object connection) : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(SpoofSummary.StaticWriter.Write(connection));
}
EOF

precompiled_method_id='PrecompiledSpoof.StaticWriter::Write`0(0:System.Object)->System.Int32'
precompiled_effect_digest="$(printf '%s\tsafe' "$precompiled_method_id" | shasum -a 256 | awk '{print $1}')"
write_project \
    "PrecompiledSpoof" \
    "IIoT.Infrastructure.FixturePrecompiledSpoof" \
    "false" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/PrecompiledSpoof/PrecompiledSpoof.cs" <<EOF
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Manifest",
    "2|1|$precompiled_effect_digest")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.SourceIdentity",
    "0000000000000000000000000000000000000000000000000000000000000000")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Method",
    "$precompiled_method_id\tsafe")]

namespace PrecompiledSpoof;
public static class StaticWriter
{
    public static int Write(object connection)
        => Dapper.SqlMapper.Execute(connection, "delete from unmanaged_precompiled_spoof");
}
EOF

write_project \
    "InvalidPrecompiledSpoof" \
    "IIoT.Dapper.FixturePrecompiledSpoofInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><Reference Include="IIoT.Infrastructure.FixturePrecompiledSpoof"><HintPath>../PrecompiledSpoof/bin/Release/net10.0/IIoT.Infrastructure.FixturePrecompiledSpoof.dll</HintPath></Reference></ItemGroup>'
cat > "$fixture_root/InvalidPrecompiledSpoof/ExternalReadPort.cs" <<'EOF'
namespace Fixture;
public sealed class PrecompiledSpoofReadPort(object connection) : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(PrecompiledSpoof.StaticWriter.Write(connection));
}
EOF

corrupt_source_identity="$(printf '%s' 'CorruptEffectSummary/CorruptEffectSummary.csproj' | shasum -a 256 | awk '{print $1}')"
write_project "CorruptEffectSummary" "IIoT.Infrastructure.FixtureCorruptEffectSummary" "false"
cat > "$fixture_root/CorruptEffectSummary/CorruptSummary.cs" <<EOF
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Manifest",
    "2|1|corrupt")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.SourceIdentity",
    "$corrupt_source_identity")]
[assembly: System.Reflection.AssemblyMetadata(
    "IIoT.CloudArchitecture.EffectSummary.Method",
    "corrupt\tsafe")]

namespace CorruptSummary
{
    public static class StaticReader
    {
        public static int Read() => 1;
    }
}
EOF

write_project \
    "InvalidCorruptEffectSummary" \
    "IIoT.Dapper.FixtureCorruptEffectSummaryInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../CorruptEffectSummary/CorruptEffectSummary.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidCorruptEffectSummary/ExternalReadPort.cs" <<'EOF'
namespace Fixture;
public sealed class CorruptSummaryReadPort : IExternalReadPort
{
    public System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(CorruptSummary.StaticReader.Read());
}
EOF

write_project \
    "InvalidImplicitAiRead" \
    "IIoT.Dapper.FixtureImplicitAiReadInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../CrossProjectAiReadContracts/CrossProjectAiReadContracts.csproj" /><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidImplicitAiRead/ImplicitReadPort.cs" <<'EOF'
namespace Fixture;

public sealed class ImplicitWriter
{
    private readonly object connection;

    public ImplicitWriter(object connection)
    {
        this.connection = connection;
        _ = Dapper.SqlMapper.Execute(connection, "delete from ctor");
    }

    public int Value
    {
        get => Dapper.SqlMapper.Execute(connection, "delete from getter");
        set => _ = Dapper.SqlMapper.Execute(connection, "delete from setter");
    }

    public int this[int index]
    {
        get => Dapper.SqlMapper.Execute(connection, "delete from index_getter");
        set => _ = Dapper.SqlMapper.Execute(connection, "delete from index_setter");
    }

    public event System.Action Changed
    {
        add => _ = Dapper.SqlMapper.Execute(connection, "delete from event_add");
        remove => _ = Dapper.SqlMapper.Execute(connection, "delete from event_remove");
    }

    public static ImplicitWriter operator +(ImplicitWriter left, ImplicitWriter right)
    {
        _ = Dapper.SqlMapper.Execute(left.connection, "delete from operator");
        return left;
    }

    public static explicit operator int(ImplicitWriter writer)
        => Dapper.SqlMapper.Execute(writer.connection, "delete from conversion");
}

public sealed class EvilEnumerable(object connection)
{
    public EvilEnumerator GetEnumerator()
    {
        _ = Dapper.SqlMapper.Execute(connection, "delete from get_enumerator");
        return new EvilEnumerator();
    }
}

public sealed class EvilEnumerator
{
    public int Current => 1;
    public bool MoveNext() => false;
}

public sealed class EvilDisposable(object connection) : System.IDisposable
{
    public void Dispose()
        => _ = Dapper.SqlMapper.Execute(connection, "delete from dispose");
}

public sealed class EvilAwaitable(object connection)
{
    public EvilAwaiter GetAwaiter()
    {
        _ = Dapper.SqlMapper.Execute(connection, "delete from get_awaiter");
        return new EvilAwaiter();
    }
}

public sealed class EvilAwaiter : System.Runtime.CompilerServices.INotifyCompletion
{
    public bool IsCompleted => true;
    public int GetResult() => 1;
    public void OnCompleted(System.Action continuation) => continuation();
}

public sealed class EvilDeconstructable(object connection)
{
    public void Deconstruct(out int left, out int right)
    {
        _ = Dapper.SqlMapper.Execute(connection, "delete from deconstruct");
        left = 1;
        right = 2;
    }
}

public sealed class ImplicitReadPort(object connection) : IExternalReadPort
{
    private readonly int initialized =
        Dapper.SqlMapper.Execute(connection, "delete from instance_initializer");
    private static readonly int staticInitialized =
        Dapper.SqlMapper.Execute(new object(), "delete from static_initializer");

    public async System.Threading.Tasks.Task<int> ReadAsync(
        System.Threading.CancellationToken cancellationToken)
    {
        var writer = new ImplicitWriter(connection);
        _ = writer.Value;
        writer.Value = 2;
        _ = writer[0];
        writer[0] = 2;
        writer.Changed += OnChanged;
        writer.Changed -= OnChanged;
        _ = writer + writer;
        _ = (int)writer;
        foreach (var value in new EvilEnumerable(connection))
            _ = value;
        using var disposable = new EvilDisposable(connection);
        _ = await new EvilAwaitable(connection);
        var (left, right) = new EvilDeconstructable(connection);
        return initialized + staticInitialized + left + right;
    }

    private static void OnChanged() { }
}
EOF

write_project \
    "InvalidDefaultInterfaceAiRead" \
    "IIoT.Dapper.FixtureDefaultInterfaceInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidDefaultInterfaceAiRead/DefaultPort.cs" <<'EOF'
namespace IIoT.SharedKernel.Architecture
{
    public interface IReadOnlyQueryPort { }
}

namespace Fixture
{
    public interface IDefaultReadPort : IIoT.SharedKernel.Architecture.IReadOnlyQueryPort
    {
        int Read(object connection)
            => Dapper.SqlMapper.Execute(connection, "delete from default_interface");
    }
}
EOF

write_project \
    "InvalidDefaultInterfaceHandler" \
    "IIoT.Infrastructure.FixtureDefaultHandlerInvalid" \
    "true" \
    '<ItemGroup><ProjectReference Include="../DapperStub/DapperStub.csproj" /></ItemGroup>'
cat > "$fixture_root/InvalidDefaultInterfaceHandler/DefaultHandler.cs" <<'EOF'
namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
    public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
}

namespace IIoT.Services.CrossCutting.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAiReadAttribute(string permission) : System.Attribute;
}

namespace IIoT.SharedKernel.Messaging
{
    public interface IQueryHandler<in TQuery, TResponse>
    {
        System.Threading.Tasks.Task<TResponse> Handle(
            TQuery request,
            System.Threading.CancellationToken cancellationToken);
    }
}

namespace Fixture
{
    [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
    public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }

    public interface IDefaultHandler
        : IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>
    {
        System.Threading.Tasks.Task<int> IIoT.SharedKernel.Messaging.IQueryHandler<Query, int>.Handle(
            Query request,
            System.Threading.CancellationToken cancellationToken)
        {
            var affected = Dapper.SqlMapper.Execute(new object(), "delete from default_handler");
            return System.Threading.Tasks.Task.FromResult(affected);
        }
    }
}
EOF

write_project "Invalid005" "IIoT.ProductionService.FixtureInvalid005" "true"
cat > "$fixture_root/Invalid005/MissingAuthorization.cs" <<'EOF'
namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
    public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
}

namespace IIoT.Services.CrossCutting.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAiReadAttribute(string permission) : System.Attribute;
}

public sealed class MissingAuthorization : IIoT.Services.Contracts.IAiReadQuery<int> { }
EOF

write_project "PortFakesStub" "IIoT.CloudPlatform.PortFakes" "false"
printf '%s\n' 'namespace Fixture; public sealed class FakeDeviceFactory { }' > "$fixture_root/PortFakesStub/FakeDeviceFactory.cs"
write_project \
    "Invalid006" \
    "IIoT.ProductionService.FixtureInvalid006" \
    "true" \
    '<ItemGroup><ProjectReference Include="../PortFakesStub/PortFakesStub.csproj" /></ItemGroup>'
printf '%s\n' 'namespace Fixture; public sealed class ProductionType { }' > "$fixture_root/Invalid006/ProductionType.cs"

write_project "Invalid009" "IIoT.EntityFrameworkCore.FixtureInvalid009" "true"
cat > "$fixture_root/Invalid009/CachedPermissionProvider.cs" <<'EOF'
namespace IIoT.Services.Contracts
{
    public interface ICacheService
    {
        System.Threading.Tasks.Task<int> GetAsync(string key);
    }
}

namespace IIoT.Services.Contracts.Authorization
{
    public interface IPermissionProvider
    {
        System.Threading.Tasks.Task<int> ReadAsync();
    }
}

public sealed class CachedPermissionProvider(
    IIoT.Services.Contracts.ICacheService cache)
    : IIoT.Services.Contracts.Authorization.IPermissionProvider
{
    private readonly System.Func<System.Threading.Tasks.Task<int>> fieldRead =
        () => cache.GetAsync("permissions-field");
    private System.Func<System.Threading.Tasks.Task<int>> PropertyRead { get; } =
        () => cache.GetAsync("permissions-property");

    public async System.Threading.Tasks.Task<int> ReadAsync()
    {
        _ = await fieldRead();
        _ = await PropertyRead();
        System.Func<System.Threading.Tasks.Task<int>> localRead =
            () => cache.GetAsync("permissions-local");
        _ = await localRead();
        _ = await System.Threading.Tasks.Task.Run(() => cache.GetAsync("permissions-external"));
        System.Delegate dynamicRead =
            new System.Func<System.Threading.Tasks.Task<int>>(() => cache.GetAsync("permissions-dynamic"));
        _ = dynamicRead.DynamicInvoke();
        return 1;
    }
}
EOF

write_project "ValidDelegateContext" "IIoT.ProductionService.FixtureDelegateContext" "true"
cat > "$fixture_root/ValidDelegateContext/DelegateContext.cs" <<'EOF'
namespace IIoT.Services.Contracts
{
    public interface IAiReadRequest<out T> { }
    public interface IAiReadQuery<out T> : IAiReadRequest<T> { }
    public interface ICacheService
    {
        System.Threading.Tasks.Task<int> GetAsync(string key);
    }
}

namespace IIoT.Services.CrossCutting.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeAiReadAttribute(string permission) : System.Attribute;
}

namespace IIoT.SharedKernel.Messaging
{
    public interface IQueryHandler<in TQuery, TResponse>
    {
        System.Threading.Tasks.Task<TResponse> Handle(
            TQuery request,
            System.Threading.CancellationToken cancellationToken);
    }
}

namespace IIoT.SharedKernel.Repository
{
    public interface IRepository<T>
    {
        System.Threading.Tasks.Task<int> SaveChangesAsync();
    }
}

namespace IIoT.Services.Contracts.Authorization
{
    public interface IPermissionProvider
    {
        System.Threading.Tasks.Task<int> ReadAsync();
    }
}

[IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
public sealed class ContextQuery : IIoT.Services.Contracts.IAiReadQuery<int> { }

public sealed class ContextHandler
    : IIoT.SharedKernel.Messaging.IQueryHandler<ContextQuery, int>
{
    public System.Threading.Tasks.Task<int> Handle(
        ContextQuery request,
        System.Threading.CancellationToken cancellationToken) => Invoke(Safe);

    internal static System.Threading.Tasks.Task<int> Invoke(
        System.Func<System.Threading.Tasks.Task<int>> callback) => callback();

    private static System.Threading.Tasks.Task<int> Safe()
        => System.Threading.Tasks.Task.FromResult(1);
}

public sealed class NonAiWriter(IIoT.SharedKernel.Repository.IRepository<object> repository)
{
    public System.Threading.Tasks.Task<int> WriteAsync()
        => ContextHandler.Invoke(() => repository.SaveChangesAsync());
}

public sealed class ContextPermissionProvider
    : IIoT.Services.Contracts.Authorization.IPermissionProvider
{
    public System.Threading.Tasks.Task<int> ReadAsync() => Invoke(Safe);

    internal static System.Threading.Tasks.Task<int> Invoke(
        System.Func<System.Threading.Tasks.Task<int>> callback) => callback();

    private static System.Threading.Tasks.Task<int> Safe()
        => System.Threading.Tasks.Task.FromResult(1);
}

public sealed class NonSecurityReader(IIoT.Services.Contracts.ICacheService cache)
{
    public System.Threading.Tasks.Task<int> ReadAsync()
        => ContextPermissionProvider.Invoke(() => cache.GetAsync("non-security"));
}
EOF

write_project "InvalidUnclassified" "IIoT.FutureProductionComponent" "true"
cat > "$fixture_root/InvalidUnclassified/FutureProductionType.cs" <<'EOF'
namespace Fixture;
public sealed class FutureProductionType { }
EOF

write_project "Invalid010" "IIoT.HttpApi.FixtureInvalid010" "true"
cat > "$fixture_root/Invalid010/UnsignedJwtReader.cs" <<'EOF'
namespace System.IdentityModel.Tokens.Jwt
{
    public sealed class JwtSecurityTokenHandler
    {
        public object ReadJwtToken(string token) => new object();
    }
}

public sealed class UnsignedJwtReader
{
    public object Read(string token)
        => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
}
EOF

write_project "Invalid011" "IIoT.ProductionService.FixtureInvalid011" "true"
cat > "$fixture_root/Invalid011/RetiredNamespace.cs" <<'EOF'
namespace IIoT.Services.Common.Legacy;
public sealed class ShadowCompatibilityAdapter { }
EOF

write_project "Invalid012" "IIoT.AppHost.FixtureInvalid012" "true"
cat > "$fixture_root/Invalid012/ConnectionResourceLiteral.cs" <<'EOF'
namespace Fixture;
public sealed class ConnectionResourceConsumer
{
    public string DatabaseName => "iiot-db";
}
EOF

write_project "InvalidGenerated" "IIoT.ProductionService.FixtureGenerated" "true"
cat > "$fixture_root/InvalidGenerated/GeneratedRetiredNamespace.g.cs" <<'EOF'
// <auto-generated/>
namespace IIoT.Services.Common.GeneratedCompatibility;
public sealed class GeneratedRetiredType { }
EOF

write_project \
    "SuppressedPragma" \
    "IIoT.ProductionService.SuppressedPragma" \
    "true"
cat > "$fixture_root/SuppressedPragma/SuppressedPragma.cs" <<'EOF'
#pragma warning disable CLOUDARCH009
namespace IIoT.Services.Common.PragmaSuppression;
public sealed class RetiredType { }
EOF

write_project \
    "SuppressedNoWarn" \
    "IIoT.ProductionService.SuppressedNoWarn" \
    "true" \
    "" \
    '<NoWarn>$(NoWarn);CLOUDARCH009</NoWarn>'
cat > "$fixture_root/SuppressedNoWarn/SuppressedNoWarn.cs" <<'EOF'
namespace IIoT.Services.Common.NoWarnSuppression;
public sealed class RetiredType { }
EOF

write_project \
    "SuppressedEditorConfig" \
    "IIoT.ProductionService.SuppressedEditorConfig" \
    "true"
cat > "$fixture_root/SuppressedEditorConfig/SuppressedEditorConfig.cs" <<'EOF'
namespace IIoT.Services.Common.EditorConfigSuppression;
public sealed class RetiredType { }
EOF
cat > "$fixture_root/SuppressedEditorConfig/.editorconfig" <<'EOF'
root = true

[*.cs]
dotnet_diagnostic.CLOUDARCH009.severity = none
EOF

build_valid "$fixture_root/Valid/Valid.csproj"
build_valid "$fixture_root/ValidDataWorker/ValidDataWorker.csproj"
build_valid "$fixture_root/ValidReadOnlySql/ValidReadOnlySql.csproj"
build_valid "$fixture_root/ValidMigrationHost/ValidMigrationHost.csproj"
build_valid "$fixture_root/ValidHttpApiAdapter/ValidHttpApiAdapter.csproj"
build_valid "$fixture_root/ValidDelegateContext/ValidDelegateContext.csproj"
build_valid "$fixture_root/CrossAssemblyStaticWriter/CrossAssemblyStaticWriter.csproj"
build_valid "$fixture_root/ValidCrossAssemblySummary/ValidCrossAssemblySummary.csproj"
build_valid "$fixture_root/PrecompiledSpoof/PrecompiledSpoof.csproj"
build_invalid "$fixture_root/Invalid001/Invalid001.csproj" "CLOUDARCH001"
build_invalid "$fixture_root/Invalid002/Invalid002.csproj" "CLOUDARCH002"
build_invalid "$fixture_root/Invalid003/Invalid003.csproj" "CLOUDARCH003"
build_invalid "$fixture_root/Invalid004/Invalid004.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossProjectAiRead/InvalidCrossProjectAiRead.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblySummary/InvalidCrossAssemblySummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyDispatchSummary/InvalidCrossAssemblyDispatchSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyVirtualSummary/InvalidCrossAssemblyVirtualSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyCallbackSummary/InvalidCrossAssemblyCallbackSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblySqlSummary/InvalidCrossAssemblySqlSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyCommandSummary/InvalidCrossAssemblyCommandSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyCollectionInitializerSummary/InvalidCrossAssemblyCollectionInitializerSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyInitializerSummary/InvalidCrossAssemblyInitializerSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyStaticInitializerSummary/InvalidCrossAssemblyStaticInitializerSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyDirectStaticFieldSummary/InvalidCrossAssemblyDirectStaticFieldSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyNativeBoundarySummary/InvalidCrossAssemblyNativeBoundarySummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCrossAssemblyOpenDispatchSummary/InvalidCrossAssemblyOpenDispatchSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidMissingEffectSummary/InvalidMissingEffectSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidOldEffectSummary/InvalidOldEffectSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidSpoofedEffectSummary/InvalidSpoofedEffectSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidPrecompiledSpoof/InvalidPrecompiledSpoof.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidCorruptEffectSummary/InvalidCorruptEffectSummary.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidImplicitAiRead/InvalidImplicitAiRead.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidDefaultInterfaceAiRead/InvalidDefaultInterfaceAiRead.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/InvalidDefaultInterfaceHandler/InvalidDefaultInterfaceHandler.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/Invalid005/Invalid005.csproj" "CLOUDARCH005"
build_invalid "$fixture_root/Invalid006/Invalid006.csproj" "CLOUDARCH006"
build_invalid "$fixture_root/Invalid007/Invalid007.csproj" "CLOUDARCH003"
build_invalid "$fixture_root/Invalid008/Invalid008.csproj" "CLOUDARCH003"
build_invalid "$fixture_root/Invalid009/Invalid009.csproj" "CLOUDARCH007"
build_invalid "$fixture_root/Invalid010/Invalid010.csproj" "CLOUDARCH008"
build_invalid "$fixture_root/Invalid011/Invalid011.csproj" "CLOUDARCH009"
build_invalid "$fixture_root/Invalid012/Invalid012.csproj" "CLOUDARCH010"
build_invalid "$fixture_root/InvalidUnclassified/InvalidUnclassified.csproj" "CLOUDARCH001"
build_invalid "$fixture_root/InvalidGenerated/InvalidGenerated.csproj" "CLOUDARCH009"
build_invalid "$fixture_root/SuppressedPragma/SuppressedPragma.csproj" "CLOUDARCH009"
build_invalid "$fixture_root/SuppressedNoWarn/SuppressedNoWarn.csproj" "CLOUDARCH009"
build_invalid "$fixture_root/SuppressedEditorConfig/SuppressedEditorConfig.csproj" "CLOUDARCH009"

printf '%s\n' \
    'ARCHITECTURE_FIXTURES_OK valid=8 invalid=15 callGraphBypass=20 suppressionBypass=3 diagnostics=CLOUDARCH001,CLOUDARCH002,CLOUDARCH003,CLOUDARCH004,CLOUDARCH005,CLOUDARCH006,CLOUDARCH007,CLOUDARCH008,CLOUDARCH009,CLOUDARCH010'
