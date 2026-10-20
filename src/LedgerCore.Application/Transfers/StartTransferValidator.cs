using LedgerCore.Application.Accounts;
using LedgerCore.Application.Validation;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.Application.Transfers;

// Transfer.Request throws on these, so they have to be caught here to stay a 400
public sealed class StartTransferValidator : ICommandValidator<StartTransfer>
{
    public IEnumerable<ValidationError> Validate(StartTransfer command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Source == default)
        {
            yield return new ValidationError("source", "required", "A source account is required.");
        }

        if (command.Destination == default)
        {
            yield return new ValidationError("destination", "required", "A destination account is required.");
        }
        else if (command.Destination == command.Source)
        {
            yield return new ValidationError("destination", "same_account", "The destination must be another account.");
        }

        foreach (var error in AccountValidation.PositiveAmount(command.Amount))
        {
            yield return error;
        }
    }
}
