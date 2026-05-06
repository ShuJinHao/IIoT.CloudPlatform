using System.Security.Cryptography;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.ProductionService.Security;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Create")]
[DistributedLock("iiot:lock:device-create:{ProcessId}:{DeviceName}", TimeoutSeconds = 5)]
public record RegisterDeviceCommand(
    string DeviceName,
    Guid ProcessId
) : IHumanCommand<Result<CreateDeviceResultDto>>;

public sealed record CreateDeviceResultDto(
    Guid Id,
    string Code,
    string BootstrapSecret);

public class RegisterDeviceHandler(
    ICurrentUser currentUser,
    IRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService,
    IDeviceReadQueryService deviceReadQueryService,
    IAuditTrailService auditTrailService
) : ICommandHandler<RegisterDeviceCommand, Result<CreateDeviceResultDto>>
{
    public async Task<Result<CreateDeviceResultDto>> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
            return await FailAsync(request, "只有管理员可以注册设备", cancellationToken);

        var deviceName = request.DeviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceName))
            return await FailAsync(request, "设备名称不能为空", cancellationToken);
        if (request.ProcessId == Guid.Empty)
            return await FailAsync(request, "工序不能为空", cancellationToken);

        var processExists = await processReadQueryService.ExistsAsync(
            request.ProcessId,
            cancellationToken);

        if (!processExists)
            return await FailAsync(request, "设备注册失败：指定工序不存在", cancellationToken);

        var code = await GenerateUniqueCodeAsync(deviceReadQueryService, cancellationToken);
        if (code is null)
            return await FailAsync(request, "设备注册失败：无法生成唯一设备寻址码", cancellationToken);

        var bootstrapSecret = BootstrapSecretGenerator.Generate();
        var device = new Device(deviceName, code, request.ProcessId);
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(bootstrapSecret));

        deviceRepository.Add(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Register",
                "Device",
                device.Id.ToString(),
                DateTime.UtcNow,
                affected > 0,
                $"注册设备 {device.DeviceName}（{device.Code}）到工序 {device.ProcessId}。",
                affected > 0 ? null : "保存设备注册记录失败。"),
            cancellationToken);

        return Result.Success(new CreateDeviceResultDto(device.Id, device.Code, bootstrapSecret));
    }

    private async Task<Result<CreateDeviceResultDto>> FailAsync(
        RegisterDeviceCommand request,
        string message,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Register",
                "Device",
                $"{request.ProcessId}:{request.DeviceName?.Trim()}",
                DateTime.UtcNow,
                false,
                $"注册设备 {request.DeviceName?.Trim()}。",
                message),
            cancellationToken);

        return Result.Failure(message);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
    {
        return Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
    }

    private static async Task<string?> GenerateUniqueCodeAsync(
        IDeviceReadQueryService deviceReadQueryService,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 20;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = DeviceCodeGenerator.Generate();
            var occupied = await deviceReadQueryService.CodeExistsAsync(
                candidate,
                cancellationToken: cancellationToken);
            if (!occupied)
            {
                return candidate;
            }
        }

        return null;
    }
}

internal static class DeviceCodeGenerator
{
    private const string Prefix = "DEV-";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int RandomPartLength = 10;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[RandomPartLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return string.Concat(Prefix, new string(chars));
    }
}
