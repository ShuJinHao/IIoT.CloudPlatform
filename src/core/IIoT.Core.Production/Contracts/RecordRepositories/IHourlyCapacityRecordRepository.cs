using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Core.Production.Contracts.RecordRepositories;

public interface IHourlyCapacityRecordRepository
{
    Task UpsertAsync(
        HourlyCapacityWriteModel item,
        CancellationToken cancellationToken = default);
}

public sealed record HourlyCapacityWriteModel(
    Guid Id,
    Guid DeviceId,
    string MacAddress,
    string ClientCode,
    DateOnly Date,
    string ShiftCode,
    int Hour,
    int Minute,
    string TimeLabel,
    int TotalCount,
    int OkCount,
    int NgCount,
    string PlcName,
    DateTime ReportedAt);
