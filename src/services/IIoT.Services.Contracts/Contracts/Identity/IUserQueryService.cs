namespace IIoT.Services.Contracts.Identity;

public interface IUserQueryService
{
    Task<IList<IdentityAccountDto>> GetAllUsersAsync();

    Task<IdentityAccountDto?> GetUserByEmployeeNoAsync(string employeeNo);
}
