using System.Data;
using Dapper;
using IIoT.SharedKernel.Architecture;

namespace IIoT.Dapper;

internal readonly struct ReadOnlyCommandDefinition
{
    private readonly CommandDefinition _command;

    public ReadOnlyCommandDefinition(
        string commandText,
        object? parameters = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null,
        CommandType? commandType = null,
        CommandFlags flags = CommandFlags.Buffered,
        CancellationToken cancellationToken = default)
    {
        _command = new CommandDefinition(
            ReadOnlySqlGuard.Require(commandText),
            parameters,
            transaction,
            commandTimeout,
            commandType,
            flags,
            cancellationToken);
    }

    public static implicit operator CommandDefinition(
        ReadOnlyCommandDefinition command)
        => command._command;
}
