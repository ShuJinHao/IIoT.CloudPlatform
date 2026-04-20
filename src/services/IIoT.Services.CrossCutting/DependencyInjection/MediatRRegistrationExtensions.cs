using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Services.CrossCutting.DependencyInjection;

public static class MediatRRegistrationExtensions
{
    public static IServiceCollection AddConfiguredMediatR(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MediatRServiceConfiguration> configure)
    {
        services.AddMediatR(cfg =>
        {
            var licenseKey = configuration["MediatR:LicenseKey"];
            if (!string.IsNullOrWhiteSpace(licenseKey))
            {
                cfg.LicenseKey = licenseKey;
            }

            configure(cfg);
        });

        return services;
    }
}
