using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// Edge 客户端发布版本保留策略。
/// </summary>
public sealed class ClientReleaseRetentionPolicy : BaseEntity<Guid>, IAggregateRoot<Guid>
{
    public const int MaximumPublishedVersions = 3;

    public static readonly Guid SingletonId = Guid.Parse("11f8c47a-58d0-4998-b2de-fbd299f8cb9d");

    private ClientReleaseRetentionPolicy()
    {
    }

    public ClientReleaseRetentionPolicy(
        int maxVersionsPerComponent,
        DateTime? updatedAtUtc = null)
    {
        Id = SingletonId;
        MaxVersionsPerComponent = maxVersionsPerComponent;
        UpdatedAtUtc = ClientReleaseComponent.NormalizeUtc(
            updatedAtUtc ?? DateTime.UtcNow);
        Validate();
    }

    public int MaxVersionsPerComponent { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public void Update(
        int maxVersionsPerComponent,
        DateTime? updatedAtUtc = null)
    {
        MaxVersionsPerComponent = maxVersionsPerComponent;
        UpdatedAtUtc = ClientReleaseComponent.NormalizeUtc(
            updatedAtUtc ?? DateTime.UtcNow);
        Validate();
    }

    private void Validate()
    {
        if (MaxVersionsPerComponent is < 1 or > MaximumPublishedVersions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxVersionsPerComponent),
                $"每个组件可分发版本数必须在 1 到 {MaximumPublishedVersions} 之间。");
        }
    }
}
