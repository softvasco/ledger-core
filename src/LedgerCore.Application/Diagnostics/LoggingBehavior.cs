using LedgerCore.Application.Commands;
using LedgerCore.Application.Validation;
using LedgerCore.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace LedgerCore.Application.Diagnostics;

/// <summary>Logs one line per command with how it ended and how long it took.</summary>
public sealed class LoggingBehavior<TCommand, TResult>(ILogger<LoggingBehavior<TCommand, TResult>> logger, TimeProvider clock)
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, Func<Task<TResult>> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        var name = typeof(TCommand).Name;
        var started = clock.GetTimestamp();
        try
        {
            var result = await continuation().ConfigureAwait(false);
            var elapsed = clock.GetElapsedTime(started).TotalMilliseconds;
            if (result is Result { IsSuccess: false } rejected)
            {
                CommandLog.Rejected(logger, name, rejected.Error.Code, elapsed);
            }
            else
            {
                CommandLog.Succeeded(logger, name, elapsed);
            }

            return result;
        }
        catch (CommandValidationException invalid)
        {
            CommandLog.Invalid(logger, name, invalid.Errors.Count);
            throw;
        }
        catch (Exception exception)
        {
            CommandLog.Failed(logger, exception, name, clock.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}

internal static partial class CommandLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "{Command} succeeded in {ElapsedMs} ms")]
    public static partial void Succeeded(ILogger logger, string command, double elapsedMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "{Command} was rejected with {ErrorCode} in {ElapsedMs} ms")]
    public static partial void Rejected(ILogger logger, string command, string errorCode, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "{Command} was invalid with {ErrorCount} errors")]
    public static partial void Invalid(ILogger logger, string command, int errorCount);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "{Command} failed after {ElapsedMs} ms")]
    public static partial void Failed(ILogger logger, Exception exception, string command, double elapsedMs);
}
