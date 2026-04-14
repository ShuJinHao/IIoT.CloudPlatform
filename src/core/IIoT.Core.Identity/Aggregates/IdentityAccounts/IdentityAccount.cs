using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Identity.Aggregates.IdentityAccounts;

public class IdentityAccount : IAggregateRoot
{
    protected IdentityAccount() { }

    private IdentityAccount(Guid id, string employeeNo, bool isEnabled)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("IdentityAccount Id cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);

        Id = id;
        EmployeeNo = employeeNo.Trim();
        IsEnabled = isEnabled;
    }

    public Guid Id { get; private set; }

    public string EmployeeNo { get; private set; } = null!;

    public bool IsEnabled { get; private set; }

    public static IdentityAccount Create(Guid id, string employeeNo) => new(id, employeeNo, true);

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;
}
