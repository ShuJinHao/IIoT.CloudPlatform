using System.Security.Cryptography;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.GenerateInstaller)]
public sealed record GetEdgeInstallerPackageByGenerationQuery(
    Guid GenerationId)
    : IHumanQuery<Result<EdgeInstallerPackageDto>>;

public sealed class GetEdgeInstallerPackageByGenerationHandler(
    IEdgeInstallerGenerationStore generationStore,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IOptions<EdgeInstallerArtifactOptions> options)
    : IQueryHandler<
        GetEdgeInstallerPackageByGenerationQuery,
        Result<EdgeInstallerPackageDto>>
{
    public async Task<Result<EdgeInstallerPackageDto>> Handle(
        GetEdgeInstallerPackageByGenerationQuery request,
        CancellationToken cancellationToken)
    {
        if (request.GenerationId == Guid.Empty)
            return Result.Invalid("安装包记录号不能为空。");

        var record = await generationStore.GetByIdAsync(
            request.GenerationId,
            cancellationToken);
        if (record is null)
            return Result.NotFound("安装包记录不存在。");

        EdgeInstallerGenerationBindingFact[] bindings;
        try
        {
            bindings = JsonSerializer.Deserialize<
                           EdgeInstallerGenerationBindingFact[]>(
                           record.BindingsJson)
                       ?? [];
        }
        catch (JsonException)
        {
            return Result.Failure("安装包绑定证据无效。");
        }

        if (bindings.Length == 0)
            return Result.Failure("安装包绑定证据为空。");

        var access = await currentUserDeviceAccessService
            .GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!access.IsSuccess)
            return Result.Forbidden("用户设备权限无法确认。");
        if (access.Value is { } allowedDeviceIds
            && bindings.Any(binding =>
                !allowedDeviceIds.Contains(binding.DeviceId)))
        {
            return Result.Forbidden("安装包包含当前用户无权访问的设备。");
        }

        if (!await generationStore.HasDownloadablePendingAsync(
                request.GenerationId,
                DateTime.UtcNow,
                cancellationToken))
        {
            return Result.Invalid("该安装包已全部激活或已过期。");
        }

        var directory = Path.GetFullPath(Path.Combine(
            options.Value.RootPath,
            "generated"));
        var path = Path.GetFullPath(Path.Combine(
            directory,
            $"{request.GenerationId:N}.exe"));
        if (!path.StartsWith(
                directory + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || !File.Exists(path))
        {
            return Result.NotFound("安装包字节已不存在。");
        }

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        if (stream.Length != record.PackageSize
            || !string.Equals(
                hash,
                record.PackageSha256,
                StringComparison.Ordinal))
        {
            await stream.DisposeAsync();
            return Result.Failure("安装包字节完整性校验失败。");
        }

        stream.Position = 0;
        return Result.Success(new EdgeInstallerPackageDto(
            record.FileName,
            "application/vnd.microsoft.portable-executable",
            stream,
            record.Id));
    }
}
