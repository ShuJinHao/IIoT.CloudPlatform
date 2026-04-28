namespace IIoT.Services.Contracts.Identity;

public record IdentityAccountDto(
    Guid Id,
    string UserName,
    bool IsEnabled,
    IList<string> Roles);
