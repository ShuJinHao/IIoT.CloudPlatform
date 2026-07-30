using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.MasterData.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Commands.Processes;

[AuthorizeRequirement("Process.Delete")]
[DistributedLock("iiot:lock:process:{ProcessId}", TimeoutSeconds = 5)]
public record DeleteProcessCommand(Guid ProcessId) : IHumanCommand<Result<bool>>;

public class DeleteProcessHandler(
    IRepository<MfgProcess> processRepository,
    IProcessReadQueryService processReadQueryService,
    IUnitOfWork unitOfWork,
    IProcessWriteObservationReader observationReader
) : ICommandHandler<DeleteProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteProcessCommand request,
        CancellationToken cancellationToken)
    {
        ProcessWriteState? baseline = null;
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
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveProcessAsync(
                    request.ProcessId,
                    baseline?.ProcessCode ?? string.Empty,
                    token),
                callbackToken);
            if (current is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (current.Target is null)
            {
                if (writeAttempted)
                {
                    return Result.Success(true);
                }

                return Result.Failure("未找到目标工序档案");
            }

            baseline ??= current.Target;
            if (current.Target != baseline)
            {
                throw new CloudWriteConflictException();
            }

            if (current.HasDevices)
            {
                return Result.Failure(
                    "删除失败: 该工序下仍有关联设备，请先迁移或停用设备");
            }

            if (current.HasRecipes)
            {
                return Result.Failure(
                    "删除失败: 该工序下仍有关联配方，请先停用或迁移配方");
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var process = await processRepository.GetSingleOrDefaultAsync(
                new MfgProcessByIdSpec(request.ProcessId),
                callbackToken);
            if (process is null
                || process.RowVersion != baseline.RowVersion)
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            if (await processReadQueryService.HasDevicesAsync(
                    request.ProcessId,
                    callbackToken)
                || await processReadQueryService.HasRecipesAsync(
                    request.ProcessId,
                    callbackToken))
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            process.MarkDeleted();
            processRepository.Delete(process);
            await processRepository.SaveChangesAsync(callbackToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(true);
        }

        async Task<Result<bool>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveProcessAsync(
                    request.ProcessId,
                    baseline?.ProcessCode ?? string.Empty,
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
    }
}
