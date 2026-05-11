namespace IIoT.Services.Contracts.Identity;

public sealed class BootstrapAuthOptions
{
    public const string SectionName = "BootstrapAuth";

    public bool RequireSecret { get; set; } = true;

    public void Validate()
    {
    }
}
