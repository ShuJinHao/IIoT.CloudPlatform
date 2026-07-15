using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;

namespace IIoT.CloudPlatform.TestKit;

internal static class TestIdentityData
{
    public static Employee AddEmployeeWithIdentity(
        IIoTDbContext dbContext,
        string employeeNo,
        string realName,
        bool accountEnabled = true,
        bool employeeActive = true)
    {
        var employee = new Employee(Guid.NewGuid(), employeeNo, realName);
        if (!employeeActive)
        {
            employee.Deactivate();
        }
        dbContext.Users.Add(new ApplicationUser
        {
            Id = employee.Id,
            UserName = employee.EmployeeNo,
            IsEnabled = accountEnabled
        });
        dbContext.Employees.Add(employee);
        return employee;
    }
}
