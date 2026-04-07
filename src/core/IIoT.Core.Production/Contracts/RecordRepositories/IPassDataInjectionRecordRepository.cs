using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Core.Production.Contracts.RecordRepositories;

public interface IPassDataInjectionRecordRepository
{
    Task InsertAsync(
        PassDataInjectionWriteModel item,
        CancellationToken cancellationToken = default);
}

public sealed record PassDataInjectionWriteModel(
    Guid Id,
    Guid DeviceId,
    string MacAddress,
    string ClientCode,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt,
    string Barcode,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume);
