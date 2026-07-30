using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

[AuthorizeRequirement("Recipe.Create")]
[DistributedLock("iiot:lock:recipe-create:{ProcessId}:{DeviceId}:{RecipeName}", TimeoutSeconds = 5)]
public record CreateRecipeCommand(
    string RecipeName,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb
) : IHumanCommand<Result<Guid>>;

public class CreateRecipeHandler(
    IRepository<Recipe> recipeRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IUnitOfWork unitOfWork,
    IRecipeWriteObservationReader observationReader)
    : ICommandHandler<CreateRecipeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateRecipeCommand request,
        CancellationToken cancellationToken)
    {
        var recipeName = request.RecipeName?.Trim() ?? string.Empty;
        var parametersJsonb = request.ParametersJsonb?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(recipeName))
            return Result.Failure("配方名称不能为空");
        if (string.IsNullOrEmpty(parametersJsonb))
            return Result.Failure("配方参数不能为空");
        if (request.ProcessId == Guid.Empty)
            return Result.Failure("工序不能为空");
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("设备不能为空");

        var deviceAccess =
            await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                request.DeviceId,
                cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(
                deviceAccess.Errors?.ToArray()
                ?? ["越权: 未授权该设备"]);
        }

        var recipeId = Guid.NewGuid();
        uint? targetRowVersion = null;
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
            if (MatchesTarget(current.Target))
            {
                return Result.Success(recipeId);
            }

            if (!current.ProcessExists
                || !current.DeviceExistsInProcess)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return !current.ProcessExists
                    ? Result.Failure(
                        "配方创建失败: 指定工序不存在")
                    : Result.Failure(
                        "配方创建失败: 指定设备不存在或不属于该工序");
            }

            var existingVersion = current.Family.SingleOrDefault(
                recipe => string.Equals(
                    recipe.Version,
                    "V1.0",
                    StringComparison.OrdinalIgnoreCase));
            if (existingVersion is not null)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure(
                    $"配方创建失败: 已存在同名初始版本配方 [{recipeName}]");
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var recipe = new Recipe(
                recipeId,
                recipeName,
                request.ProcessId,
                request.DeviceId,
                parametersJsonb);
            recipeRepository.Add(recipe);
            await recipeRepository.SaveChangesAsync(callbackToken);
            targetRowVersion = recipe.RowVersion;
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(recipeId);
        }

        async Task<Result<Guid>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveRecipeAsync(
                    recipeId,
                    request.ProcessId,
                    request.DeviceId,
                    recipeName,
                    token));
            if (current?.Target is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current.Target))
            {
                return Result.Success(recipeId);
            }

            throw new CloudWriteConflictException();
        }

        async Task<RecipeWriteObservation> ObserveAttemptAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveRecipeAsync(
                    recipeId,
                    request.ProcessId,
                    request.DeviceId,
                    recipeName,
                    token),
                callbackToken);
            return current ?? throw new CloudWriteCommitUnknownException();
        }

        bool MatchesTarget(RecipeWriteState? state)
            => state is not null
               && state.Id == recipeId
               && string.Equals(
                   state.RecipeName,
                   recipeName,
                   StringComparison.Ordinal)
               && string.Equals(
                   state.Version,
                   "V1.0",
                   StringComparison.OrdinalIgnoreCase)
               && state.ProcessId == request.ProcessId
               && state.DeviceId == request.DeviceId
               && CloudWriteCommitRecovery.JsonEquals(
                   state.ParametersJsonb,
                   parametersJsonb)
               && state.Status == (int)RecipeStatus.Active
               && (!targetRowVersion.HasValue
                   || state.RowVersion == targetRowVersion.Value);
    }
}
