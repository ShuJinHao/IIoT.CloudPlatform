using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentValidation.Results;
using IIoT.HttpApi.Controllers;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Validators;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class PassStationContractSnapshotTests
{
    [Fact]
    public void PassStationProviderContract_ShouldBindCanonicalSnapshotToControllerDtoValidatorAndLimits()
    {
        var snapshotBytes = File.ReadAllBytes(CloudRepositoryPath.Find(
            "scripts", "tests", "baselines", "cloud-pass-station-contract.json"));
        Assert.Equal(
            "86cc7ca5399d6af3524ce34f2cace3ea926625b808b72f30f83b3118b8fa6d81",
            Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant());
        using var snapshot = JsonDocument.Parse(snapshotBytes);
        var contract = snapshot.RootElement;

        var controllerRoute = typeof(EdgePassStationController).GetCustomAttribute<RouteAttribute>()
                              ?? throw new InvalidOperationException("Provider controller route is missing.");
        var action = typeof(EdgePassStationController).GetMethod(nameof(EdgePassStationController.ReceiveBatch))
                     ?? throw new InvalidOperationException("Provider action is missing.");
        var post = action.GetCustomAttribute<HttpPostAttribute>()
                   ?? throw new InvalidOperationException("Provider POST route is missing.");
        Assert.Equal(
            contract.GetProperty("http").GetProperty("routeTemplate").GetString(),
            $"/{controllerRoute.Template}/{post.Template}");
        Assert.Contains("POST", post.HttpMethods);
        var actionParameters = action.GetParameters();
        Assert.Equal(typeof(string), actionParameters[0].ParameterType);
        Assert.NotNull(actionParameters[0].GetCustomAttribute<FromRouteAttribute>());
        Assert.Equal(typeof(PassStationBatchUploadRequest), actionParameters[1].ParameterType);
        Assert.NotNull(actionParameters[1].GetCustomAttribute<FromBodyAttribute>());

        var nullability = new NullabilityInfoContext();
        Assert.Equal(
            NullabilityState.NotNull,
            nullability.Create(typeof(PassStationBatchUploadRequest).GetProperty(nameof(PassStationBatchUploadRequest.Items))!).ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PassStationBatchUploadRequest).GetProperty(nameof(PassStationBatchUploadRequest.RequestId))!).ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PassStationBatchUploadRequest).GetProperty(nameof(PassStationBatchUploadRequest.ProcessType))!).ReadState);
        Assert.Equal(
            NullabilityState.NotNull,
            nullability.Create(typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.Barcode))!).ReadState);
        Assert.Equal(
            NullabilityState.NotNull,
            nullability.Create(typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.CellResult))!).ReadState);
        Assert.Equal(typeof(Guid), typeof(PassStationBatchUploadRequest).GetProperty(nameof(PassStationBatchUploadRequest.DeviceId))!.PropertyType);
        Assert.Equal(typeof(DateTime), typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.CompletedTime))!.PropertyType);
        Assert.Equal(typeof(JsonElement), typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.Payload))!.PropertyType);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.CompletionId))!).ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.ClientCode))!).ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.TypeKey))!).ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PassStationItemInput).GetProperty(nameof(PassStationItemInput.PlcCode))!).ReadState);
        var uploadConstructor = Assert.Single(typeof(PassStationBatchUploadRequest).GetConstructors());
        Assert.Equal(1, uploadConstructor.GetParameters().Single(parameter => parameter.Name == "SchemaVersion").DefaultValue);

        var requestFields = contract.GetProperty("request").GetProperty("fields");
        var itemFields = contract.GetProperty("item").GetProperty("fields");
        Assert.Equal(UploadValidationLimits.MaxPassStationItems, requestFields.GetProperty("items").GetProperty("maxItems").GetInt32());
        Assert.Equal(UploadValidationLimits.MaxRequestIdLength, requestFields.GetProperty("requestId").GetProperty("maxLength").GetInt32());
        Assert.Equal(UploadValidationLimits.MaxShortCodeLength, requestFields.GetProperty("processType").GetProperty("maxLength").GetInt32());
        Assert.Equal(UploadValidationLimits.MaxMediumCodeLength, itemFields.GetProperty("barcode").GetProperty("maxLength").GetInt32());
        Assert.Equal(UploadValidationLimits.MaxShortCodeLength, itemFields.GetProperty("cellResult").GetProperty("maxLength").GetInt32());
        Assert.Equal(UploadValidationLimits.MaxPassStationPayloadFields, itemFields.GetProperty("payload").GetProperty("maxProperties").GetInt32());
        Assert.Equal(
            ["OK", "NG"],
            itemFields.GetProperty("cellResult").GetProperty("edgeEmittedValues")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());

        var validator = new ReceivePassStationBatchCommandValidator();
        var validItem = new PassStationItemInput(
            "CP-CLIP-001",
            "OK",
            DateTime.UtcNow,
            ParseJson("""
            {
              "plcCode": "P2-CP01",
              "plcName": "正极模切01",
              "clipSlot": "MG1",
              "startTime": "2026-07-24T00:00:00Z",
              "punchingQuantity": 120,
              "punchingSpeed": 1.25
            }
            """));
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            " CP ", Guid.NewGuid(), [validItem], "request-1", 1, "CP")).IsValid);
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            "cp", Guid.NewGuid(), [validItem], "request-2", 2, "cp")).IsValid);
        var compatibleItemWithoutClipSlot = validItem with
        {
            Payload = ParseJson("""
            {
              "plcCode": "P2-CP01",
              "plcName": "正极模切01",
              "startTime": "2026-07-24T00:00:00Z",
              "punchingQuantity": 120,
              "punchingSpeed": 1.25
            }
            """)
        };
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            "cp", Guid.NewGuid(), [compatibleItemWithoutClipSlot])).IsValid);

        AssertFailure(validator, new("cp", Guid.Empty, [validItem]), nameof(ReceivePassStationBatchCommand.DeviceId));
        AssertFailure(validator, new("cp", Guid.NewGuid(), null!), nameof(ReceivePassStationBatchCommand.Items));
        AssertFailure(validator, new("cp", Guid.NewGuid(), []), nameof(ReceivePassStationBatchCommand.Items));
        AssertFailure(validator, new("cp", Guid.NewGuid(), Enumerable.Repeat(validItem, 1001).ToList()), nameof(ReceivePassStationBatchCommand.Items));
        AssertFailure(validator, new("cp", Guid.NewGuid(), [validItem], new string('r', 129)), nameof(ReceivePassStationBatchCommand.RequestId));
        AssertFailure(validator, new("cp", Guid.NewGuid(), [validItem], SchemaVersion: 2), nameof(ReceivePassStationBatchCommand.ProcessType));
        AssertFailure(validator, new("cp", Guid.NewGuid(), [validItem], ProcessType: new string('p', 33)), nameof(ReceivePassStationBatchCommand.ProcessType));
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            "die-cutting-completion",
            Guid.NewGuid(),
            [validItem],
            SchemaVersion: 2,
            ProcessType: "diecut")).IsValid);
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            "future-capability",
            Guid.NewGuid(),
            [validItem])).IsValid);
        AssertItemFailure(validator, validItem with { Barcode = " " }, nameof(PassStationItemInput.Barcode));
        AssertItemFailure(validator, validItem with { Barcode = new string('b', 129) }, nameof(PassStationItemInput.Barcode));
        AssertItemFailure(validator, validItem with { CellResult = " " }, nameof(PassStationItemInput.CellResult));
        AssertItemFailure(validator, validItem with { CellResult = new string('c', 33) }, nameof(PassStationItemInput.CellResult));
        AssertItemFailure(validator, validItem with { CompletedTime = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc) }, nameof(PassStationItemInput.CompletedTime));
        AssertItemFailure(validator, validItem with { CompletedTime = DateTime.UtcNow.AddDays(2) }, nameof(PassStationItemInput.CompletedTime));
        AssertItemFailure(validator, validItem with { Payload = ParseJson("[]") }, nameof(PassStationItemInput.Payload));
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            "cp",
            Guid.NewGuid(),
            [validItem with
            {
                Payload = ParseJson("""
                {
                  "futurePluginField": "由实际安装插件能力动态校验"
                }
                """)
            }])).IsValid);
        var oversizedPayload = JsonSerializer.Serialize(Enumerable.Range(0, 65).ToDictionary(index => $"f{index}", index => index));
        AssertItemFailure(validator, validItem with { Payload = ParseJson(oversizedPayload) }, nameof(PassStationItemInput.Payload));

        AssertFailure(
            validator,
            new ReceivePassStationBatchCommand(
                "cp",
                Guid.NewGuid(),
                [validItem with { CellResult = "PASS" }],
                SchemaVersion: 2,
                ProcessType: "cp"),
            $"Items[0].{nameof(PassStationItemInput.CellResult)}");
        AssertFailure(
            validator,
            new ReceivePassStationBatchCommand(
                "cp",
                Guid.NewGuid(),
                [validItem with { CompletedTime = DateTime.SpecifyKind(validItem.CompletedTime, DateTimeKind.Unspecified) }],
                SchemaVersion: 2,
                ProcessType: "cp"),
            $"Items[0].{nameof(PassStationItemInput.CompletedTime)}");
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            "cp",
            Guid.NewGuid(),
            [compatibleItemWithoutClipSlot],
            SchemaVersion: 2,
            ProcessType: "cp")).IsValid);
    }

    [Fact]
    public void StrictV2ProviderExample_ShouldMatchEdgeConsumerSnapshot()
    {
        var snapshotBytes = File.ReadAllBytes(CloudRepositoryPath.Find(
            "scripts", "tests", "baselines", "cloud-pass-station-contract-v2.json"));
        Assert.Equal(
            "3ecfd0c47605dbc099a84c0b5b91ee8e53b4b45e2d4dec4bad3f7d24c5b23e40",
            Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant());
        using var snapshot = JsonDocument.Parse(snapshotBytes);
        var contract = snapshot.RootElement;

        Assert.Equal(2, contract.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            [1, 2],
            contract.GetProperty("compatibility").GetProperty("providerContinuesToAccept")
                .EnumerateArray()
                .Select(item => item.GetInt32())
                .ToArray());
        Assert.Equal(
            CloudWriteConflictException.Code,
            contract.GetProperty("request").GetProperty("rules").GetProperty("requestIdDifferentContentCode").GetString());

        var example = contract.GetProperty("example");
        var item = Assert.Single(example.GetProperty("items").EnumerateArray());
        var command = new ReceivePassStationBatchCommand(
            "cp",
            example.GetProperty("deviceId").GetGuid(),
            [new PassStationItemInput(
                item.GetProperty("barcode").GetString()!,
                item.GetProperty("cellResult").GetString()!,
                item.GetProperty("completedTime").GetDateTime(),
                item.GetProperty("payload").Clone())],
            example.GetProperty("requestId").GetString(),
            example.GetProperty("schemaVersion").GetInt32(),
            example.GetProperty("processType").GetString());

        Assert.True(new ReceivePassStationBatchCommandValidator()
            .Validate(command)
            .IsValid);
    }

    [Fact]
    public void StrictV3ProviderExample_ShouldMatchEdgeConsumerSnapshot()
    {
        var snapshotBytes = File.ReadAllBytes(CloudRepositoryPath.Find(
            "scripts", "tests", "baselines", "cloud-pass-station-contract-v3.json"));
        Assert.Equal(
            "a4ab8dabc09d7f6d72d1f4b7c28efdfc39900c90a67f96325eeb768be2325cd2",
            Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant());
        using var snapshot = JsonDocument.Parse(snapshotBytes);
        var contract = snapshot.RootElement;

        Assert.Equal(3, contract.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            [1, 2, 3],
            contract.GetProperty("compatibility").GetProperty("providerContinuesToAccept")
                .EnumerateArray()
                .Select(item => item.GetInt32())
                .ToArray());
        Assert.Equal(
            CloudWriteConflictException.Code,
            contract.GetProperty("request").GetProperty("rules").GetProperty("requestIdDifferentContentCode").GetString());

        var example = contract.GetProperty("example");
        var item = Assert.Single(example.GetProperty("items").EnumerateArray());
        var command = new ReceivePassStationBatchCommand(
            example.GetProperty("routeTypeKey").GetString()!,
            example.GetProperty("deviceId").GetGuid(),
            [new PassStationItemInput(
                item.GetProperty("barcode").GetString()!,
                item.GetProperty("cellResult").GetString()!,
                item.GetProperty("completedTime").GetDateTime(),
                item.GetProperty("payload").Clone(),
                item.GetProperty("completionId").GetString(),
                item.GetProperty("clientCode").GetString(),
                item.GetProperty("typeKey").GetString(),
                item.GetProperty("plcCode").GetString())],
            example.GetProperty("requestId").GetString(),
            example.GetProperty("schemaVersion").GetInt32(),
            example.GetProperty("processType").GetString(),
            example.GetProperty("clientCode").GetString());

        Assert.True(new ReceivePassStationBatchCommandValidator()
            .Validate(command)
            .IsValid);
    }

    [Fact]
    public void CurrentProductionPassStationCatalog_ShouldExposeDieCuttingCompletionWithLegacyAliases()
    {
        var catalogBytes = File.ReadAllBytes(CloudRepositoryPath.Find(
            "src", "hosts", "IIoT.HttpApi", "config", "pass-station-types.json"));
        using var document = JsonDocument.Parse(catalogBytes);
        var configuredOptions = JsonSerializer.Deserialize<PassStationTypesOptions>(
            document.RootElement.GetProperty("PassStationTypes").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(configuredOptions);
        configuredOptions.Validate();

        var types = document.RootElement
            .GetProperty("PassStationTypes")
            .GetProperty("types")
            .EnumerateArray()
            .ToArray();

        var currentType = Assert.Single(
            types,
            type => type.GetProperty("typeKey").GetString() == "die-cutting-completion");
        Assert.Equal(
            ["ap", "cp"],
            currentType.GetProperty("legacyTypeKeys")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            ["plcCode", "plcName", "clipSlot", "startTime", "punchingQuantity", "punchingSpeed"],
            currentType.GetProperty("fields")
                .EnumerateArray()
                .Select(field => field.GetProperty("key").GetString()!)
                .ToArray());
        var clipSlot = currentType.GetProperty("fields")
            .EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == "clipSlot");
        Assert.Equal("enum", clipSlot.GetProperty("type").GetString());
        Assert.True(clipSlot.GetProperty("required").GetBoolean());
        Assert.Equal(
            ["MG1", "MG2"],
            clipSlot.GetProperty("options")
                .EnumerateArray()
                .Select(option => option.GetString()!)
                .ToArray());
        Assert.Contains("clipSlot", currentType.GetProperty("listColumns").EnumerateArray().Select(column => column.GetString()!));
        Assert.Contains(
            currentType.GetProperty("detailSections").EnumerateArray(),
            section => section.GetProperty("fields").EnumerateArray().Any(field => field.GetString() == "clipSlot"));
    }

    [Fact]
    public void ProductionPassStationIndexes_ShouldCoverTypePlcAndCompletedTime()
    {
        var schemaSql = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.Dapper", "Production", "Sql", "Schemas", "004_pass_station_records.sql"));

        Assert.Contains(
            "on pass_station_records (type_key, (payload_jsonb ->> 'plcCode'), completed_time desc)",
            schemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "on pass_station_records (type_key, (payload_jsonb ->> 'plcName'), completed_time desc)",
            schemaSql,
            StringComparison.Ordinal);

        var completionSchemaSql = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.Dapper", "Production", "Sql", "Schemas", "008_pass_station_completion_id.sql"));
        Assert.Contains(
            "create table if not exists pass_station_completion_claims",
            completionSchemaSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "create unique index if not exists uq_pass_station_records_device_type_completion",
            completionSchemaSql,
            StringComparison.OrdinalIgnoreCase);

        var repositorySource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.Dapper", "Production", "Repositories", "PassStations", "PassStationRecordRepository.cs"));
        Assert.Contains("with completion_claim as", repositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "insert into pass_station_completion_claims",
            repositorySource,
            StringComparison.Ordinal);
    }

    private static void AssertFailure(
        ReceivePassStationBatchCommandValidator validator,
        ReceivePassStationBatchCommand command,
        string propertyName)
    {
        Assert.Contains(validator.Validate(command).Errors, error => error.PropertyName == propertyName);
    }

    private static void AssertItemFailure(
        ReceivePassStationBatchCommandValidator validator,
        PassStationItemInput item,
        string itemProperty)
    {
        AssertFailure(
            validator,
            new ReceivePassStationBatchCommand("cp", Guid.NewGuid(), [item]),
            $"Items[0].{itemProperty}");
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static PassStationSchemaProvider CreateSchemaProvider()
    {
        var options = new PassStationTypesOptions
        {
            Types =
            [
                new PassStationTypeDefinitionDto
                {
                    TypeKey = "cp",
                    DisplayName = "正极模切",
                    Description = "Provider contract fixture",
                    Fields =
                    [
                        new PassStationFieldDefinitionDto { Key = "plcCode", Label = "PLC 编码", Type = PassStationFieldTypes.String, Required = true },
                        new PassStationFieldDefinitionDto { Key = "plcName", Label = "PLC 名称", Type = PassStationFieldTypes.String, Required = true },
                        new PassStationFieldDefinitionDto
                        {
                            Key = "clipSlot",
                            Label = "弹夹位",
                            Type = PassStationFieldTypes.Enum,
                            Required = true,
                            Options = ["MG1", "MG2"]
                        },
                        new PassStationFieldDefinitionDto { Key = "startTime", Label = "开始时间", Type = PassStationFieldTypes.DateTime, Required = true },
                        new PassStationFieldDefinitionDto { Key = "punchingQuantity", Label = "冲切数量", Type = PassStationFieldTypes.Integer, Required = true, Min = 0 },
                        new PassStationFieldDefinitionDto { Key = "punchingSpeed", Label = "冲切速度", Type = PassStationFieldTypes.Number, Required = true, Min = 0 }
                    ],
                    ListColumns = ["plcName", "clipSlot", "barcode", "punchingQuantity", "punchingSpeed"],
                    DetailSections = [new PassStationDetailSectionDto { Title = "Base", Fields = ["barcode", "clipSlot"] }],
                    SupportedModes = [PassStationQueryModes.BarcodeProcess]
                }
            ]
        };
        return new PassStationSchemaProvider(Options.Create(options));
    }
}
