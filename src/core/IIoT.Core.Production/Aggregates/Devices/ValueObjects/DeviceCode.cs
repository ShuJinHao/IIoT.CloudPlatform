namespace IIoT.Core.Production.Aggregates.Devices.ValueObjects;

public readonly record struct DeviceCode
{
    private DeviceCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static DeviceCode From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new DeviceCode(value.Trim().ToUpperInvariant());
    }

    public override string ToString() => Value;
}
