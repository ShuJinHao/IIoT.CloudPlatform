using System.Security.Cryptography;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
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
    string Code);

public class RegisterDeviceHandler(
    IRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService,
    IDeviceReadQueryService deviceReadQueryService,
    ICacheService cacheService
) : ICommandHandler<RegisterDeviceCommand, Result<CreateDeviceResultDto>>
{
    public async Task<Result<CreateDeviceResultDto>> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var deviceName = request.DeviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceName))
            return Result.Failure("DeviceName cannot be empty.");
        if (request.ProcessId == Guid.Empty)
            return Result.Failure("ProcessId cannot be empty.");

        var processExists = await processReadQueryService.ExistsAsync(
            request.ProcessId,
            cancellationToken);

        if (!processExists)
            return Result.Failure("Device creation failed: the specified process does not exist.");

        var code = await GenerateUniqueCodeAsync(deviceReadQueryService, cancellationToken);
        if (code is null)
            return Result.Failure("Device creation failed: unable to allocate a unique device code.");

        var device = new Device(deviceName, code, request.ProcessId);

        deviceRepository.Add(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(CacheKeys.AllDevices(), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.DevicesByProcess(device.ProcessId), cancellationToken);
        }

        return Result.Success(new CreateDeviceResultDto(device.Id, device.Code));
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
