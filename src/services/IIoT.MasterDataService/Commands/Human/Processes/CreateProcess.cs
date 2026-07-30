using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Commands.Processes;

[AuthorizeRequirement("Process.Create")]
[DistributedLock("iiot:lock:process-code:{ProcessCode}", TimeoutSeconds = 5)]
public record CreateProcessCommand(
    string ProcessCode,
    string ProcessName
) : IHumanCommand<Result<Guid>>;

public class CreateProcessHandler(
    IRepository<MfgProcess> processRepository,
    IUnitOfWork unitOfWork,
    IProcessWriteObservationReader observationReader
) : ICommandHandler<CreateProcessCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateProcessCommand request,
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

        var processId = Guid.NewGuid();
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
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveProcessAsync(
                    processId,
                    code,
                    token),
                callbackToken);
            if (current is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current))
            {
                return Result.Success(processId);
            }

            if (current.Target is not null
                || current.ProcessCodeOwnerId is not null)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure(
                    $"工序创建失败: 编码 [{code}] 已存在");
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var process = new MfgProcess(processId, code, name);
            processRepository.Add(process);
            await processRepository.SaveChangesAsync(callbackToken);
            targetRowVersion = process.RowVersion;
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(processId);
        }

        async Task<Result<Guid>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveProcessAsync(
                    processId,
                    code,
                    token));
            if (current is null
                || current.Target is null
                || !targetRowVersion.HasValue)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current))
            {
                return Result.Success(processId);
            }

            throw new CloudWriteConflictException();
        }

        bool MatchesTarget(ProcessWriteObservation observation)
            => observation.Target is not null
               && observation.Target.Id == processId
               && string.Equals(
                   observation.Target.ProcessCode,
                   code,
                   StringComparison.Ordinal)
               && string.Equals(
                   observation.Target.ProcessName,
                   name,
                   StringComparison.Ordinal)
               && (!targetRowVersion.HasValue
                   || observation.Target.RowVersion == targetRowVersion.Value)
               && observation.ProcessCodeOwnerId == processId;
    }
}
