using IIoT.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class UserQueryService(UserManager<ApplicationUser> userManager) : IUserQueryService
{
    public async Task<IList<IdentityAccountDto>> GetAllUsersAsync()
    {
        var users = await userManager.Users.AsNoTracking().ToListAsync();
        var userDtos = new List<IdentityAccountDto>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            userDtos.Add(new IdentityAccountDto(user.Id, user.UserName!, user.IsEnabled, roles));
        }

        return userDtos;
    }

    public async Task<IdentityAccountDto?> GetUserByEmployeeNoAsync(string employeeNo)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return null;

        var roles = await userManager.GetRolesAsync(user);
        return new IdentityAccountDto(user.Id, user.UserName!, user.IsEnabled, roles);
    }
}
