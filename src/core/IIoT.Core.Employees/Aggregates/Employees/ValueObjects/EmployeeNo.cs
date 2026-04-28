namespace IIoT.Core.Employees.Aggregates.Employees.ValueObjects;

public readonly record struct EmployeeNo
{
    private EmployeeNo(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EmployeeNo From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new EmployeeNo(value.Trim());
    }

    public override string ToString() => Value;
}
