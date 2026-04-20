using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class RecipeReadQueryService(IIoTDbContext dbContext) : IRecipeReadQueryService
{
    public Task<bool> VersionExistsAsync(
        Guid processId,
        Guid deviceId,
        string recipeName,
        string version,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Recipes
            .AsNoTracking()
            .AnyAsync(
                recipe =>
                    recipe.ProcessId == processId &&
                    recipe.DeviceId == deviceId &&
                    recipe.RecipeName == recipeName &&
                    recipe.Version == version,
                cancellationToken);
    }
}
