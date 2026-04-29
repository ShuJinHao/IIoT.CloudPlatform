using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Recipes.Events;

public sealed record RecipeCreatedDomainEvent(
    Guid RecipeId,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid DeviceId) : IDomainEvent;

public sealed record RecipeArchivedDomainEvent(
    Guid RecipeId,
    string Version,
    Guid ProcessId,
    Guid DeviceId) : IDomainEvent;

public sealed record RecipeVersionUpgradedDomainEvent(
    Guid SourceRecipeId,
    Guid NewRecipeId,
    string RecipeName,
    string NewVersion,
    Guid ProcessId,
    Guid DeviceId) : IDomainEvent;

public sealed record RecipeDeletedDomainEvent(
    Guid RecipeId,
    Guid ProcessId,
    Guid DeviceId) : IDomainEvent;
