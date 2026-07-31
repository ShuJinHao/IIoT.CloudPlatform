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
        if (commandType is not null and not CommandType.Text)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandType),
                commandType,
                "Read-only commands must use CommandType.Text.");
        }

        _command = new CommandDefinition(
            ReadOnlySqlGuard.Require(commandText),
            parameters,
            transaction,
            commandTimeout,
            CommandType.Text,
            flags,
            cancellationToken);
    }

    public static implicit operator CommandDefinition(
        ReadOnlyCommandDefinition command)
        => command._command;
}
