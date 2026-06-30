using FluentAssertions;
using IIoT.HttpApi.Controllers;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.EndToEndTests;

public sealed class RuntimeHeartbeatControllerTests
{
    [Fact]
    public async Task Report_ShouldForwardCommandWithForwardedRemoteIp()
    {
        var sender = new RecordingSender();
        var controller = new EdgeRuntimeHeartbeatController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(sender)
            }
        };
        controller.Request.Headers["X-Forwarded-For"] = "10.10.1.8, 172.16.0.2";

        var command = new ReportDeviceRuntimeHeartbeatCommand(
            Guid.NewGuid(),
            "DEV-TEST",
            "runtime-1",
            "profile-a",
            "1.0.20",
            "1",
            "Running",
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow);

        var result = await controller.Report(command, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        sender.SentRequest.Should().BeOfType<ReportDeviceRuntimeHeartbeatCommand>()
            .Which.RemoteIpAddress.Should().Be("10.10.1.8");
    }

    private static DefaultHttpContext BuildHttpContext(ISender sender)
    {
        var services = new ServiceCollection()
            .AddSingleton(sender)
            .BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.10");
        return context;
    }

    private sealed class RecordingSender : ISender
    {
        public object? SentRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            SentRequest = request;
            var response = Result.Success(new DeviceRuntimeHeartbeatResultDto(
                ((ReportDeviceRuntimeHeartbeatCommand)(object)request).DeviceId,
                DateTime.UtcNow));
            return Task.FromResult((TResponse)(object)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            SentRequest = request;
            return Task.FromResult<object?>(Result.Success(new DeviceRuntimeHeartbeatResultDto(
                ((ReportDeviceRuntimeHeartbeatCommand)request).DeviceId,
                DateTime.UtcNow)));
        }

        Task ISender.Send<TRequest>(TRequest request, CancellationToken cancellationToken)
        {
            SentRequest = request;
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
