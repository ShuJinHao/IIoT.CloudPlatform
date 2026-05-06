namespace IIoT.Services.Contracts.AiRead;

public interface IAiReadResponseMetadata
{
    DateTimeOffset AsOfUtc { get; }

    string Source { get; }

    string QueryScope { get; }

    int RowCount { get; }

    bool Truncated { get; }

    string? NextCursor { get; }
}

public sealed record AiReadListResponse<T>(
    IReadOnlyList<T> Items,
    DateTimeOffset AsOfUtc,
    string Source,
    string QueryScope,
    int RowCount,
    bool Truncated,
    string? NextCursor = null) : IAiReadResponseMetadata;
