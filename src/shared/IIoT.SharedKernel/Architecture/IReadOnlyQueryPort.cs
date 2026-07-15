namespace IIoT.SharedKernel.Architecture;

/// <summary>
/// Marks a cross-project query port whose implementations must remain free of write side effects.
/// </summary>
public interface IReadOnlyQueryPort;
