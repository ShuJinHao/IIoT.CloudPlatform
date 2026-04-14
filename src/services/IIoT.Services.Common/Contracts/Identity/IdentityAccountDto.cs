namespace IIoT.Services.Common.Contracts;

public record IdentityAccountDto(
    Guid Id,
    string EmployeeNo,
    bool IsEnabled,
    IList<string> Roles);
