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

[AuthorizeRequirement("Recipe.Delete")]
[DistributedLock("iiot:lock:recipe-delete:{RecipeId}", TimeoutSeconds = 5)]
public record DeleteRecipeCommand(Guid RecipeId) : IHumanCommand<Result<bool>>;

public class DeleteRecipeHandler(
    IRepository<Recipe> recipeRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IUnitOfWork unitOfWork,
    IRecipeWriteObservationReader observationReader)
    : ICommandHandler<DeleteRecipeCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteRecipeCommand request,
        CancellationToken cancellationToken)
    {
        var accessTarget = await recipeRepository.GetSingleOrDefaultAsync(
            new RecipeByIdSpec(request.RecipeId),
            cancellationToken);
        if (accessTarget is null)
            return Result.Failure("操作失败:目标配方不存在");

        var deviceAccess =
            await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                accessTarget.DeviceId,
                cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(
                deviceAccess.Errors?.ToArray()
                ?? ["越权:您没有该机台的管辖权,禁止删除此配方"]);
        }

        var processId = accessTarget.ProcessId;
        var deviceId = accessTarget.DeviceId;
        var recipeName = accessTarget.RecipeName;
        RecipeWriteState? baseline = null;
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

        async Task<Result<bool>> ExecuteTransactionAsync(
            CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            if (current.Target is null)
            {
                if (writeAttempted)
                {
                    return Result.Success(true);
                }

                return Result.Failure(
                    "操作失败:目标配方不存在");
            }

            baseline ??= current.Target;
            if (current.Target != baseline)
            {
                throw new CloudWriteConflictException();
            }

            if (current.Target.Status != (int)RecipeStatus.Archived)
            {
                return Result.Failure(
                    "操作失败:只有已归档配方可以删除");
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var recipe = await recipeRepository.GetSingleOrDefaultAsync(
                new RecipeByIdSpec(request.RecipeId),
                callbackToken);
            if (recipe is null
                || recipe.RowVersion != baseline.RowVersion)
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            recipe.MarkDeleted();
            recipeRepository.Delete(recipe);
            await recipeRepository.SaveChangesAsync(callbackToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(true);
        }

        async Task<Result<bool>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveRecipeAsync(
                    request.RecipeId,
                    processId,
                    deviceId,
                    recipeName,
                    token));
            if (current is null
                || baseline is null
                || current.Target == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (current.Target is null)
            {
                return Result.Success(true);
            }

            throw new CloudWriteConflictException();
        }

        async Task<RecipeWriteObservation> ObserveAttemptAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveRecipeAsync(
                    request.RecipeId,
                    processId,
                    deviceId,
                    recipeName,
                    token),
                callbackToken);
            return current ?? throw new CloudWriteCommitUnknownException();
        }
    }
}
