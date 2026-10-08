using System.Diagnostics;
using LedgerCore.Application.Commands;
using LedgerCore.Application.Validation;
using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Application.Diagnostics;

/// <summary>Wraps each command in a span named after it, tagged with how it ended.</summary>
public sealed class TracingBehavior<TCommand, TResult> : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, Func<Task<TResult>> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        using var activity = LedgerTelemetry.Source.StartActivity(typeof(TCommand).Name);
        if (activity is null)
        {
            return await continuation().ConfigureAwait(false);
        }

        activity.SetTag(CommandTags.Command, typeof(TCommand).Name);
        try
        {
            var result = await continuation().ConfigureAwait(false);
            Tag(activity, result);
            return result;
        }
        catch (CommandValidationException)
        {
            // the caller sent bad input; the service itself did nothing wrong
            activity.SetTag(CommandTags.Outcome, CommandOutcome.Invalid);
            throw;
        }
        catch (Exception exception)
        {
            activity.SetTag(CommandTags.Outcome, CommandOutcome.Failed);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
            throw;
        }
    }

    // a business rule saying no is a normal answer, so tag it but don't mark the span as an error
    private static void Tag(Activity activity, TResult result)
    {
        if (result is Result { IsSuccess: false } rejected)
        {
            activity.SetTag(CommandTags.Outcome, CommandOutcome.Rejected);
            activity.SetTag(CommandTags.ErrorCode, rejected.Error.Code);
        }
        else
        {
            activity.SetTag(CommandTags.Outcome, CommandOutcome.Succeeded);
        }
    }
}
