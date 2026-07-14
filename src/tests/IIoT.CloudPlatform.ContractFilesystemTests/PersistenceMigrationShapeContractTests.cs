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
    }

}
