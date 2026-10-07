namespace LedgerCore.Application.Validation;

// not a Result (ADR-0003): the input never reached the domain, and the API turns this into a 400 with every error listed
public sealed class CommandValidationException : Exception
{
    public CommandValidationException()
    {
    }

    public CommandValidationException(string message)
        : base(message)
    {
    }

    public CommandValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CommandValidationException(string command, IReadOnlyList<ValidationError> errors)
        : base(Describe(command, errors))
    {
        Command = command;
        Errors = errors;
    }

    public string Command { get; } = "";

    public IReadOnlyList<ValidationError> Errors { get; } = [];

    private static string Describe(string command, IReadOnlyList<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return $"{command} is not valid: {string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}"))}";
    }
}
