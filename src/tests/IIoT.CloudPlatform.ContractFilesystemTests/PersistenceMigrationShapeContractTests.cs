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

    [Fact]
    public void EmployeeDeviceAccessForeignKeyMigration_ShouldFailOnOrphansWithoutRepairingData()
    {
        var migrationsDirectory = CloudRepositoryPath.Find(
            "src",
            "infrastructure",
            "IIoT.EntityFrameworkCore",
            "Migrations");
        var migrationFile = Assert.Single(
            Directory.GetFiles(
                migrationsDirectory,
                "*AddEmployeeDeviceAccessDeviceForeignKey*.cs",
                SearchOption.TopDirectoryOnly),
            file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var source = File.ReadAllText(migrationFile);
        var forwardMigration = source[..source.IndexOf(
            "protected override void Down",
            StringComparison.Ordinal)];

        forwardMigration.Should().Contain("SELECT COUNT(*)");
        forwardMigration.Should().Contain("LEFT JOIN devices");
        forwardMigration.Should().Contain("WHERE device.id IS NULL");
        forwardMigration.Should().Contain("RAISE EXCEPTION");
        forwardMigration.Should().Contain("发现 %s 条孤儿设备授权");
        forwardMigration.Should().Contain("migrationBuilder.AddForeignKey(");
        forwardMigration.Should().Contain("ReferentialAction.NoAction");
        forwardMigration.IndexOf("SELECT COUNT(*)", StringComparison.Ordinal)
            .Should().BeLessThan(
                forwardMigration.IndexOf("migrationBuilder.AddForeignKey(", StringComparison.Ordinal));
        forwardMigration.Contains(
                "DELETE FROM employee_device_accesses",
                StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        forwardMigration.Contains(
                "UPDATE employee_device_accesses",
                StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        forwardMigration.Contains(
                "INSERT INTO devices",
                StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
    }

}
