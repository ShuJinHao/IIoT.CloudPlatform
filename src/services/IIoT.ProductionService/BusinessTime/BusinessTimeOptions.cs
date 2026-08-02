using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.BusinessTime;

public sealed class BusinessTimeOptions
{
    public const string SectionName = "BusinessTime";

    public string TimeZoneId { get; set; } = "Asia/Shanghai";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
            throw Invalid("BusinessTime:TimeZoneId is required.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw Invalid($"BusinessTime:TimeZoneId [{TimeZoneId}] is invalid.");
        }
        catch (InvalidTimeZoneException)
        {
            throw Invalid($"BusinessTime:TimeZoneId [{TimeZoneId}] is invalid.");
        }
    }

    private static OptionsValidationException Invalid(string error)
        => new(SectionName, typeof(BusinessTimeOptions), [error]);
}

public interface IBusinessTimeProvider
{
    DateOnly Today();
}

public sealed class BusinessTimeProvider(
    TimeProvider timeProvider,
    IOptions<BusinessTimeOptions> options) : IBusinessTimeProvider
{
    private readonly TimeZoneInfo _timeZone =
        TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public DateOnly Today()
    {
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
