#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
analyzer_project="$repo_root/src/analyzers/IIoT.CloudPlatform.Analyzers/IIoT.CloudPlatform.Analyzers.csproj"
fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/iiot-cloud-architecture-fixtures.XXXXXX")"
trap 'rm -rf "$fixture_root"' EXIT

cp "$repo_root/.globalconfig" "$fixture_root/.globalconfig"

cat > "$fixture_root/Directory.Build.props" <<EOF
<Project>
  <PropertyGroup>
    <RestoreDisableParallel>true</RestoreDisableParallel>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup Condition="'\$(AttachCloudArchitectureAnalyzer)' == 'true'">
    <ProjectReference Include="$analyzer_project"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
EOF

write_project() {
    local directory="$1"
    local assembly_name="$2"
    local attach_analyzer="$3"
    local project_references="${4:-}"

    mkdir -p "$fixture_root/$directory"
    cat > "$fixture_root/$directory/$directory.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$assembly_name</AssemblyName>
    <AttachCloudArchitectureAnalyzer>$attach_analyzer</AttachCloudArchitectureAnalyzer>
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

namespace Fixture
{
    [IIoT.Services.CrossCutting.Attributes.AuthorizeAiRead("AiRead.Device")]
    public sealed class Query : IIoT.Services.Contracts.IAiReadQuery<int> { }

    public interface IReadPort
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

write_project "Invalid004" "IIoT.ProductionService.FixtureInvalid004" "true"
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
    public Task<int> Handle(Query request, CancellationToken cancellationToken)
        => helper.Persist(repository);
}
EOF
cat > "$fixture_root/Invalid004/WriterHelper.cs" <<'EOF'
using System.Threading.Tasks;
public sealed class WriterHelper
{
    public Task<int> Persist(IIoT.SharedKernel.Repository.IRepository<object> repository)
        => repository.SaveChangesAsync();
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

write_project "TestKitStub" "IIoT.CloudPlatform.TestKit" "false"
printf '%s\n' 'namespace Fixture; public sealed class FakeDeviceFactory { }' > "$fixture_root/TestKitStub/FakeDeviceFactory.cs"
write_project \
    "Invalid006" \
    "IIoT.ProductionService.FixtureInvalid006" \
    "true" \
    '<ItemGroup><ProjectReference Include="../TestKitStub/TestKitStub.csproj" /></ItemGroup>'
printf '%s\n' 'namespace Fixture; public sealed class ProductionType { }' > "$fixture_root/Invalid006/ProductionType.cs"

build_valid "$fixture_root/Valid/Valid.csproj"
build_valid "$fixture_root/ValidDataWorker/ValidDataWorker.csproj"
build_valid "$fixture_root/ValidMigrationHost/ValidMigrationHost.csproj"
build_valid "$fixture_root/ValidHttpApiAdapter/ValidHttpApiAdapter.csproj"
build_invalid "$fixture_root/Invalid001/Invalid001.csproj" "CLOUDARCH001"
build_invalid "$fixture_root/Invalid002/Invalid002.csproj" "CLOUDARCH002"
build_invalid "$fixture_root/Invalid003/Invalid003.csproj" "CLOUDARCH003"
build_invalid "$fixture_root/Invalid004/Invalid004.csproj" "CLOUDARCH004"
build_invalid "$fixture_root/Invalid005/Invalid005.csproj" "CLOUDARCH005"
build_invalid "$fixture_root/Invalid006/Invalid006.csproj" "CLOUDARCH006"
build_invalid "$fixture_root/Invalid007/Invalid007.csproj" "CLOUDARCH003"
build_invalid "$fixture_root/Invalid008/Invalid008.csproj" "CLOUDARCH003"

printf '%s\n' \
    'ARCHITECTURE_FIXTURES_OK valid=4 invalid=8 diagnostics=CLOUDARCH001,CLOUDARCH002,CLOUDARCH003,CLOUDARCH004,CLOUDARCH005,CLOUDARCH006'
