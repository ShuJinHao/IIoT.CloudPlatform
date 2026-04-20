namespace IIoT.Infrastructure.Logging;

public sealed class SeqOptions
{
    public const string SectionName = "Seq";

    public bool Enabled { get; set; }

    public string ServerUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public void Validate()
    {
        if (Enabled && string.IsNullOrWhiteSpace(ServerUrl))
        {
            throw new InvalidOperationException("Seq is enabled but Seq:ServerUrl is missing.");
        }
    }
}
