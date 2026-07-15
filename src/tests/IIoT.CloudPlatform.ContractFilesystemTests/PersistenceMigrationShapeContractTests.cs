using FluentAssertions;
using Xunit;

namespace IIoT.CloudPlatform.ContractFilesystemTests;

public sealed class PersistenceMigrationShapeContractTests
{
    [Fact]
    public void RecipeDeviceIdIndexMigration_ShouldExistExactlyOnce()
    {
        var migrationsDirectory = CloudRepositoryPath.Find("src", "infrastructure", "IIoT.EntityFrameworkCore", "Migrations");
        var migrationFiles = Directory.GetFiles(
                migrationsDirectory,
                "*AddRecipeDeviceIdIndex*.cs",
                SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .ToArray();

        migrationFiles.Should().ContainSingle();
        File.ReadAllText(migrationFiles[0]).Should().Contain("ix_recipes_device_id");

        var inboxMigrationFiles = Directory.GetFiles(
                migrationsDirectory,
                "*AddIntegrationEventConsumerInbox*.cs",
                SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .ToArray();
        inboxMigrationFiles.Should().ContainSingle();
        var inboxMigration = File.ReadAllText(inboxMigrationFiles[0]);
        inboxMigration.Should().Contain("integration_event_inbox_states");
        inboxMigration.Should().Contain("consumer_outbox_states");
        inboxMigration.Should().Contain("consumer_outbox_messages");
        inboxMigration.Should().Contain("MessageId, x.ConsumerId");
        var forwardMigration = inboxMigration[..inboxMigration.IndexOf(
            "protected override void Down",
            StringComparison.Ordinal)];
        forwardMigration.Should().NotContain("DropTable(",
            "the forward migration may only add the receiver inbox/outbox schema");
    }

}
