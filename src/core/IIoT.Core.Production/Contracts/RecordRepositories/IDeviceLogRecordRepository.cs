using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Core.Production.Contracts.RecordRepositories;

public interface IDeviceLogRecordRepository
{
    Task InsertBatchAsync(
        IReadOnlyCollection<DeviceLogWriteModel> items,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceLogWriteModel(
    Guid Id,
    Guid DeviceId,
    string MacAddress,
    string ClientCode,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);
