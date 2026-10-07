namespace LedgerCore.Application.Validation;

/// <summary>Checks a command's input before its handler runs. Business rules stay in the domain.</summary>
public interface ICommandValidator<in TCommand>
{
    IEnumerable<ValidationError> Validate(TCommand command);
}
