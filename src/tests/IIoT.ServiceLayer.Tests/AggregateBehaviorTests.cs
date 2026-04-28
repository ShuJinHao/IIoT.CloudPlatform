using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Devices.Events;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Aggregates.Recipes.Events;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class AggregateBehaviorTests
{
    [Fact]
    public void Recipe_ShouldValidateInputsBeforeTrimming()
    {
        var processId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        Assert.ThrowsAny<ArgumentException>(() => new Recipe(null!, processId, deviceId, "{\"speed\":120}"));
        Assert.ThrowsAny<ArgumentException>(() => new Recipe("  ", processId, deviceId, "{\"speed\":120}"));
        Assert.ThrowsAny<ArgumentException>(() => new Recipe("Recipe", processId, deviceId, null!));
        Assert.ThrowsAny<ArgumentException>(() => new Recipe("Recipe", processId, deviceId, "   "));
    }

    [Fact]
    public void Recipe_ShouldStartAtInitialVersion()
    {
        var recipe = new Recipe("Recipe", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");

        Assert.Equal("V1.0", recipe.Version);
        Assert.Equal(RecipeStatus.Active, recipe.Status);
        Assert.Contains(recipe.DomainEvents, e => e is RecipeCreatedDomainEvent);
    }

    [Fact]
    public void Recipe_CreateNextVersion_ShouldReuseValidation()
    {
        var recipe = new Recipe("Recipe", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");

        Assert.ThrowsAny<ArgumentException>(() => recipe.CreateNextVersion(null!, "{\"speed\":140}"));
        Assert.ThrowsAny<ArgumentException>(() => recipe.CreateNextVersion("V1.1", null!));
    }

    [Fact]
    public void Recipe_ShouldRejectInvalidVersionFormat()
    {
        var recipe = new Recipe("Recipe", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");

        Assert.ThrowsAny<ArgumentException>(() => recipe.CreateNextVersion("1.1", "{\"speed\":140}"));
    }

    [Fact]
    public void Employee_ShouldRaiseLifecycleDomainEvents()
    {
        var employee = new Employee(Guid.NewGuid(), "E1001", "Operator");

        Assert.Contains(employee.DomainEvents, e => e is EmployeeOnboardedDomainEvent);

        employee.ClearDomainEvents();
        employee.Rename("E1002", "Senior Operator");
        employee.Deactivate();
        employee.Activate();
        employee.Terminate();

        Assert.Contains(employee.DomainEvents, e => e is EmployeeRenamedDomainEvent);
        Assert.Contains(employee.DomainEvents, e => e is EmployeeDeactivatedDomainEvent);
        Assert.Contains(employee.DomainEvents, e => e is EmployeeActivatedDomainEvent);
        Assert.Contains(employee.DomainEvents, e => e is EmployeeTerminatedDomainEvent);
    }

    [Fact]
    public void Device_ShouldNormalizeCode_AndKeepItImmutableAcrossRename()
    {
        var device = new Device("Press-01", " dev-0001 ", Guid.NewGuid());

        Assert.Equal("DEV-0001", device.Code);
        Assert.Contains(device.DomainEvents, e => e is DeviceRegisteredDomainEvent);

        device.ClearDomainEvents();
        device.Rename("Press-02");
        device.MarkDeleted();

        Assert.Equal("DEV-0001", device.Code);
        Assert.Contains(device.DomainEvents, e => e is DeviceRenamedDomainEvent);
        Assert.Contains(device.DomainEvents, e => e is DeviceDeletedDomainEvent);
    }

    [Fact]
    public void Recipe_ShouldRaiseArchiveAndUpgradeDomainEvents()
    {
        var recipe = new Recipe("Recipe", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");

        Assert.Contains(recipe.DomainEvents, e => e is RecipeCreatedDomainEvent);

        recipe.ClearDomainEvents();
        var nextVersion = recipe.CreateNextVersion("V1.1", "{\"speed\":140}");
        recipe.Archive();

        Assert.Equal(RecipeStatus.Archived, recipe.Status);
        Assert.Equal("V1.1", nextVersion.Version);
        Assert.Equal(RecipeStatus.Active, nextVersion.Status);
        Assert.Contains(recipe.DomainEvents, e => e is RecipeArchivedDomainEvent);
        Assert.Contains(nextVersion.DomainEvents, e => e is RecipeCreatedDomainEvent);
        Assert.Contains(nextVersion.DomainEvents, e => e is RecipeVersionUpgradedDomainEvent);
    }
}
