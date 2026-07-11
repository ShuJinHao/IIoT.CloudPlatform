using System.Net;
using System.Reflection;
using System.Text;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.EndToEndTests;

public sealed class ClientReleaseUploadBoundaryTests
{
    [Fact]
    public void UploadCommands_ShouldRemainTransportNeutralPermissionMarkers()
    {
        Type[] commandTypes =
        [
            typeof(PublishEdgeReleaseBundleCommand),
            typeof(PublishEdgePluginPackageCommand)
        ];

        foreach (var commandType in commandTypes)
        {
            Assert.Empty(commandType.GetProperties(BindingFlags.Instance | BindingFlags.Public));
            var permission = commandType.GetCustomAttribute<AuthorizeRequirementAttribute>();
            Assert.NotNull(permission);
            Assert.Equal(ClientReleasePermissions.Publish, permission.Permission);
        }
    }

    [Fact]
    public void UploadSourcePort_ShouldExposeOnlyTransportNeutralMembers()
    {
        var portType = typeof(IClientReleaseUploadSource);
        var properties = portType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var methods = portType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => !method.IsSpecialName)
            .ToArray();
        var contractParts = new List<string>();
        foreach (var property in properties)
        {
            contractParts.Add(property.Name);
            contractParts.Add(property.PropertyType.FullName ?? property.PropertyType.Name);
        }

        foreach (var method in methods)
        {
            contractParts.Add(method.Name);
            contractParts.Add(method.ReturnType.FullName ?? method.ReturnType.Name);
            foreach (var parameter in method.GetParameters())
            {
                contractParts.Add(parameter.Name ?? string.Empty);
                contractParts.Add(parameter.ParameterType.FullName ?? parameter.ParameterType.Name);
            }
        }

        var contractText = string.Join(' ', contractParts);

        Assert.Equal(
            [nameof(IClientReleaseUploadSource.AuditSource), nameof(IClientReleaseUploadSource.DeclaredLength)],
            properties.Select(property => property.Name).Order().ToArray());
        Assert.Equal([nameof(IClientReleaseUploadSource.ReadAsync)], methods.Select(method => method.Name));
        Assert.DoesNotContain("Http", contractText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Request", contractText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Body", contractText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContentType", contractText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RemoteIp", contractText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(properties, property => typeof(Stream).IsAssignableFrom(property.PropertyType));
        var auditSource = properties.Single(property => property.Name == nameof(IClientReleaseUploadSource.AuditSource));
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(auditSource).ReadState);
    }

    [Fact]
    public void UploadEndpoints_ShouldKeepConfiguredRequestSizeLimit()
    {
        var controllerType = typeof(HumanClientReleaseController);
        var hostAttribute = controllerType
            .GetMethod(nameof(HumanClientReleaseController.PublishEdgeReleaseBundle))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();
        var pluginAttribute = controllerType
            .GetMethod(nameof(HumanClientReleaseController.PublishEdgePluginPackage))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();

        Assert.NotNull(hostAttribute);
        Assert.NotNull(pluginAttribute);
        Assert.Equal(
            EdgeReleaseUploadOptions.DefaultMaxBundleBytes,
            ((IRequestSizeLimitMetadata)hostAttribute).MaxRequestBodySize);
        Assert.Equal(
            EdgeReleaseUploadOptions.DefaultMaxBundleBytes,
            ((IRequestSizeLimitMetadata)pluginAttribute).MaxRequestBodySize);
    }

    [Fact]
    public async Task CurrentUploadSource_ShouldAdaptCurrentRequestWithoutLeakingItIntoThePort()
    {
        var payload = Encoding.UTF8.GetBytes("client-release");
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(payload);
        context.Request.ContentLength = payload.LongLength;
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var source = new CurrentClientReleaseUploadSource(
            new HttpContextAccessor { HttpContext = context });
        var buffer = new byte[payload.Length];

        var read = await source.ReadAsync(buffer, CancellationToken.None);

        Assert.Equal(payload.Length, read);
        Assert.Equal(payload.LongLength, source.DeclaredLength);
        Assert.Equal("192.0.2.10", source.AuditSource);
        Assert.Equal(payload, buffer);
    }
}
