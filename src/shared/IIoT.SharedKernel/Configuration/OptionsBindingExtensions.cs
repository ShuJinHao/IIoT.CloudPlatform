using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.SharedKernel.Configuration;

public static class OptionsBindingExtensions
{
    public static TOptions GetRequiredValidatedOptions<TOptions>(
        this IConfiguration configuration,
        string sectionName,
        Action<TOptions>? validate = null)
        where TOptions : class, new()
    {
        var section = configuration.GetRequiredSection(sectionName);
        var options = section.Get<TOptions>()
            ?? throw new InvalidOperationException($"Missing {sectionName} configuration.");

        validate?.Invoke(options);
        return options;
    }

    public static TOptions AddValidatedOptions<TOptions>(
        this IHostApplicationBuilder builder,
        string sectionName,
        Action<TOptions>? validate = null)
        where TOptions : class, new()
    {
        var section = builder.Configuration.GetRequiredSection(sectionName);
        var options = builder.Configuration.GetRequiredValidatedOptions(sectionName, validate);

        builder.Services.Configure<TOptions>(section);
        return options;
    }
}
