using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.MasterData.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Commands.Processes;

[AuthorizeRequirement("Process.Update")]
[DistributedLock("iiot:lock:process-code:{ProcessCode}", TimeoutSeconds = 5)]
public record UpdateProcessCommand(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName
) : IHumanCommand<Result<bool>>;

public class UpdateProcessHandler(
    IRepository<MfgProcess> processRepository,
    IUnitOfWork unitOfWork,
    IProcessWriteObservationReader observationReader
) : ICommandHandler<UpdateProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateProcessCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.ProcessCode?.Trim() ?? string.Empty;
        var name = request.ProcessName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(code))
        {
            return Result.Failure("工序编码不能为空");
        }

        if (string.IsNullOrEmpty(name))
        {
            return Result.Failure("工序名称不能为空");
        }

        ProcessWriteState? baseline = null;
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

        async Task<Result<bool>> ExecuteTransactionAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveProcessAsync(
                    request.ProcessId,
                    code,
                    token),
                callbackToken);
            if (current is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current.Target))
            {
                return Result.Success(true);
            }

            if (current.Target is null)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure("未找到目标工序档案");
            }

            if (current.ProcessCodeOwnerId is Guid codeOwnerId
                && codeOwnerId != request.ProcessId)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure(
                    $"工序编码 [{code}] 已被其他工序占用");
            }

            baseline ??= current.Target;
            if (!MatchesExact(current.Target, baseline))
            {
                throw new CloudWriteConflictException();
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

            process.Rename(code, name);
            processRepository.Update(process);
            await processRepository.SaveChangesAsync(callbackToken);
            targetRowVersion = process.RowVersion;
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(true);
        }

        async Task<Result<bool>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveProcessAsync(
                    request.ProcessId,
                    code,
                    token));
            if (current is null
                || baseline is null
                || MatchesExact(current.Target, baseline))
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current.Target))
            {
                return Result.Success(true);
            }

            throw new CloudWriteConflictException();
        }

        bool MatchesTarget(ProcessWriteState? state)
            => state is not null
               && targetRowVersion.HasValue
               && state.Id == request.ProcessId
               && state.RowVersion == targetRowVersion.Value
               && string.Equals(
                   state.ProcessCode,
                   code,
                   StringComparison.Ordinal)
               && string.Equals(
                   state.ProcessName,
                   name,
                   StringComparison.Ordinal);
    }

    private static bool MatchesExact(
        ProcessWriteState? current,
        ProcessWriteState expected)
        => current is not null
           && current == expected;
}
