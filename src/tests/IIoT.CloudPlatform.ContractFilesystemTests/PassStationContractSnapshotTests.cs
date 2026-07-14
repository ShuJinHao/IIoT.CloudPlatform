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

        var validator = new ReceivePassStationBatchCommandValidator(CreateSchemaProvider());
        var validItem = new PassStationItemInput("BC-001", "PASS", DateTime.UtcNow, ParseJson("{}"));
        Assert.True(validator.Validate(new ReceivePassStationBatchCommand(
            " INJECTION ", Guid.NewGuid(), [validItem], "request-1", 1, "INJECTION")).IsValid);

        AssertFailure(validator, new("injection", Guid.Empty, [validItem]), nameof(ReceivePassStationBatchCommand.DeviceId));
        AssertFailure(validator, new("injection", Guid.NewGuid(), null!), nameof(ReceivePassStationBatchCommand.Items));
        AssertFailure(validator, new("injection", Guid.NewGuid(), []), nameof(ReceivePassStationBatchCommand.Items));
        AssertFailure(validator, new("injection", Guid.NewGuid(), Enumerable.Repeat(validItem, 1001).ToList()), nameof(ReceivePassStationBatchCommand.Items));
        AssertFailure(validator, new("injection", Guid.NewGuid(), [validItem], new string('r', 129)), nameof(ReceivePassStationBatchCommand.RequestId));
        AssertFailure(validator, new("injection", Guid.NewGuid(), [validItem], SchemaVersion: 2), nameof(ReceivePassStationBatchCommand.SchemaVersion));
        AssertFailure(validator, new("injection", Guid.NewGuid(), [validItem], ProcessType: new string('p', 33)), nameof(ReceivePassStationBatchCommand.ProcessType));
        AssertFailure(validator, new("injection", Guid.NewGuid(), [validItem], ProcessType: "coating"), nameof(ReceivePassStationBatchCommand.ProcessType));
        AssertFailure(validator, new("missing", Guid.NewGuid(), [validItem]), nameof(ReceivePassStationBatchCommand.TypeKey));
        AssertItemFailure(validator, validItem with { Barcode = " " }, nameof(PassStationItemInput.Barcode));
        AssertItemFailure(validator, validItem with { Barcode = new string('b', 129) }, nameof(PassStationItemInput.Barcode));
        AssertItemFailure(validator, validItem with { CellResult = " " }, nameof(PassStationItemInput.CellResult));
        AssertItemFailure(validator, validItem with { CellResult = new string('c', 33) }, nameof(PassStationItemInput.CellResult));
        AssertItemFailure(validator, validItem with { CompletedTime = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc) }, nameof(PassStationItemInput.CompletedTime));
        AssertItemFailure(validator, validItem with { CompletedTime = DateTime.UtcNow.AddDays(2) }, nameof(PassStationItemInput.CompletedTime));
        AssertItemFailure(validator, validItem with { Payload = ParseJson("[]") }, nameof(PassStationItemInput.Payload));
        var oversizedPayload = JsonSerializer.Serialize(Enumerable.Range(0, 65).ToDictionary(index => $"f{index}", index => index));
        AssertItemFailure(validator, validItem with { Payload = ParseJson(oversizedPayload) }, nameof(PassStationItemInput.Payload));
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
            new ReceivePassStationBatchCommand("injection", Guid.NewGuid(), [item]),
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
                    TypeKey = "injection",
                    DisplayName = "Injection",
                    Description = "Provider contract fixture",
                    Fields =
                    [
                        new PassStationFieldDefinitionDto
                        {
                            Key = "optionalNote",
                            Label = "Optional note",
                            Type = PassStationFieldTypes.String,
                            Required = false
                        }
                    ],
                    ListColumns = ["barcode"],
                    DetailSections = [new PassStationDetailSectionDto { Title = "Base", Fields = ["barcode"] }],
                    SupportedModes = [PassStationQueryModes.BarcodeProcess]
                }
            ]
        };
        return new PassStationSchemaProvider(Options.Create(options));
    }
}
