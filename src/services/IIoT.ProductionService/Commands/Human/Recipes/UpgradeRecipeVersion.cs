using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

[AuthorizeRequirement("Recipe.Update")]
[DistributedLock("iiot:lock:recipe-upgrade:{SourceRecipeId}", TimeoutSeconds = 5)]
public record UpgradeRecipeVersionCommand(
    Guid SourceRecipeId,
    string NewVersion,
    string ParametersJsonb
) : IHumanCommand<Result<Guid>>;

public class UpgradeRecipeVersionHandler(
    IRepository<Recipe> recipeRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IUnitOfWork unitOfWork,
    IRecipeWriteObservationReader observationReader)
    : ICommandHandler<UpgradeRecipeVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        UpgradeRecipeVersionCommand request,
        CancellationToken cancellationToken)
    {
        var newVersion = request.NewVersion?.Trim() ?? string.Empty;
        var parametersJsonb = request.ParametersJsonb?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(newVersion))
            return Result.Failure("版本号不能为空");
        if (string.IsNullOrEmpty(parametersJsonb))
            return Result.Failure("配方参数不能为空");

        var accessTarget = await recipeRepository.GetSingleOrDefaultAsync(
            new RecipeByIdSpec(request.SourceRecipeId),
            cancellationToken);
        if (accessTarget is null)
            return Result.Failure("升级失败: 源配方不存在");

        var deviceAccess =
            await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                accessTarget.DeviceId,
                cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(
                deviceAccess.Errors?.ToArray()
                ?? ["越权: 当前账号无权操作该设备"]);
        }

        var processId = accessTarget.ProcessId;
        var deviceId = accessTarget.DeviceId;
        var recipeName = accessTarget.RecipeName;
        var newRecipeId = Guid.NewGuid();
        IReadOnlyList<RecipeWriteState>? baselineFamily = null;
        IReadOnlyList<RecipeWriteState>? baselineWrites = null;
        IReadOnlyList<RecipeWriteState>? targetWrites = null;
        var writeAttempted = false;
        var commitAttempted = false;
        try
        {
            return await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested
                  && !commitAttempted)
        {
            throw;
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch (Exception) when (commitAttempted)
        {
            return await ResolveCommitAsync();
        }

        async Task<Result<Guid>> ExecuteTransactionAsync(
            CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            if (ContainsStates(current.Family, targetWrites))
            {
                return Result.Success(newRecipeId);
            }

            if (current.Target is null)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure(
                    "升级失败: 源配方不存在");
            }

            if (current.Family.Any(recipe => string.Equals(
                    recipe.Version,
                    newVersion,
                    StringComparison.OrdinalIgnoreCase)))
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure(
                    $"升级失败: 版本号 [{newVersion}] 已存在");
            }

            baselineFamily ??= current.Family;
            baselineWrites ??= current.Family
                .Where(state => state.Status == (int)RecipeStatus.Active)
                .OrderBy(state => state.Id)
                .ToArray();
            if (!ContainsStates(current.Family, baselineWrites)
                || current.Family.Any(state => state.Id == newRecipeId))
            {
                throw new CloudWriteConflictException();
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var source = await recipeRepository.GetSingleOrDefaultAsync(
                new RecipeByIdSpec(request.SourceRecipeId),
                callbackToken);
            var activeVersions = await recipeRepository.GetListAsync(
                new RecipeActiveVersionsSpec(
                    recipeName,
                    processId,
                    deviceId),
                callbackToken);
            if (source is null
                || !TrackedFamilyMatches(
                    source,
                    activeVersions,
                    baselineFamily))
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            foreach (var active in activeVersions)
            {
                active.Archive();
                recipeRepository.Update(active);
            }

            var newRecipe = source.CreateNextVersion(
                newRecipeId,
                newVersion,
                parametersJsonb);
            recipeRepository.Add(newRecipe);
            await recipeRepository.SaveChangesAsync(callbackToken);

            targetWrites = activeVersions
                .Select(ToState)
                .Append(ToState(newRecipe))
                .OrderBy(state => state.Id)
                .ToArray();
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(newRecipeId);
        }

        async Task<Result<Guid>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveRecipeAsync(
                    request.SourceRecipeId,
                    processId,
                    deviceId,
                    recipeName,
                    token));
            if (current is null
                || baselineWrites is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (ContainsStates(current.Family, targetWrites))
            {
                return Result.Success(newRecipeId);
            }

            if (ContainsStates(current.Family, baselineWrites)
                && current.Family.All(state => state.Id != newRecipeId))
            {
                throw new CloudWriteCommitUnknownException();
            }

            throw new CloudWriteConflictException();
        }

        async Task<RecipeWriteObservation> ObserveAttemptAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveRecipeAsync(
                    request.SourceRecipeId,
                    processId,
                    deviceId,
                    recipeName,
                    token),
                callbackToken);
            return current ?? throw new CloudWriteCommitUnknownException();
        }
    }

    private static bool TrackedFamilyMatches(
        Recipe source,
        IReadOnlyList<Recipe> activeVersions,
        IReadOnlyList<RecipeWriteState> baseline)
    {
        var sourceBaseline = baseline.SingleOrDefault(
            state => state.Id == source.Id);
        if (sourceBaseline is null
            || source.RowVersion != sourceBaseline.RowVersion)
        {
            return false;
        }

        var expectedActiveIds = baseline
            .Where(state => state.Status == (int)RecipeStatus.Active)
            .Select(state => state.Id)
            .OrderBy(id => id);
        var actualActiveIds = activeVersions
            .Select(recipe => recipe.Id)
            .OrderBy(id => id);
        return expectedActiveIds.SequenceEqual(actualActiveIds)
               && activeVersions.All(recipe =>
                   baseline.Single(state => state.Id == recipe.Id).RowVersion
                   == recipe.RowVersion);
    }

    private static bool ContainsStates(
        IReadOnlyList<RecipeWriteState> current,
        IReadOnlyList<RecipeWriteState>? expected)
    {
        if (expected is null)
        {
            return false;
        }

        var currentById = current.ToDictionary(state => state.Id);
        return expected.All(state =>
            currentById.TryGetValue(state.Id, out var candidate)
            && MatchesState(candidate, state));
    }

    private static bool MatchesState(
        RecipeWriteState current,
        RecipeWriteState expected)
        => current.Id == expected.Id
           && string.Equals(
               current.RecipeName,
               expected.RecipeName,
               StringComparison.Ordinal)
           && string.Equals(
               current.Version,
               expected.Version,
               StringComparison.Ordinal)
           && current.ProcessId == expected.ProcessId
           && current.DeviceId == expected.DeviceId
           && CloudWriteCommitRecovery.JsonEquals(
               current.ParametersJsonb,
               expected.ParametersJsonb)
           && current.Status == expected.Status
           && current.RowVersion == expected.RowVersion;

    private static RecipeWriteState ToState(Recipe recipe)
        => new(
            recipe.Id,
            recipe.RecipeName,
            recipe.Version,
            recipe.ProcessId,
            recipe.DeviceId,
            recipe.ParametersJsonb,
            (int)recipe.Status,
            recipe.RowVersion);
}
