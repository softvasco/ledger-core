using LedgerCore.Application.Commands;

namespace LedgerCore.Application.Validation;

/// <summary>Runs every validator for the command and stops it before the handler if any of them found a problem.</summary>
public sealed class ValidationBehavior<TCommand, TResult>(IEnumerable<ICommandValidator<TCommand>> validators)
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public Task<TResult> HandleAsync(TCommand command, Func<Task<TResult>> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        // all errors at once, so a client can fix a form in one round trip
        var errors = validators.SelectMany(v => v.Validate(command)).ToArray();
        return errors.Length == 0
            ? continuation()
            : throw new CommandValidationException(typeof(TCommand).Name, errors);
    }
}
