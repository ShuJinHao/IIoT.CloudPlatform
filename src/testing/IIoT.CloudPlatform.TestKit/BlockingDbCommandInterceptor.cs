using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IIoT.CloudPlatform.TestKit;

internal sealed class BlockingDbCommandInterceptor(Func<DbCommand, bool> shouldBlock)
    : DbCommandInterceptor
{
    private readonly TaskCompletionSource entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilBlockedAsync(CancellationToken cancellationToken) =>
        entered.Task.WaitAsync(cancellationToken);

    public void Release() => released.TrySetResult();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (shouldBlock(command))
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
