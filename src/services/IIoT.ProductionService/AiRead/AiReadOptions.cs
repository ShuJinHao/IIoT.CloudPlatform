namespace IIoT.ProductionService.AiRead;

public sealed class AiReadOptions
{
    public const string SectionName = "AiRead";

    public int MaxRows { get; set; } = 100;

    public int MaxTimeRangeDays { get; set; } = 30;

    public int MaxLogMessageLength { get; set; } = 500;

    public void Validate()
    {
        if (MaxRows is < 1 or > 100)
        {
            throw new InvalidOperationException($"{SectionName}:MaxRows must be between 1 and 100.");
        }

        if (MaxTimeRangeDays is < 1 or > 366)
        {
            throw new InvalidOperationException($"{SectionName}:MaxTimeRangeDays must be between 1 and 366.");
        }

        if (MaxLogMessageLength is < 32 or > 4000)
        {
            throw new InvalidOperationException($"{SectionName}:MaxLogMessageLength must be between 32 and 4000.");
        }
    }
}
