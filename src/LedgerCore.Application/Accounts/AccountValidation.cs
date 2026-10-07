using LedgerCore.Application.Validation;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

internal static class AccountValidation
{
    public static IEnumerable<ValidationError> AccountId(AccountId id)
    {
        if (id == default)
        {
            yield return new ValidationError("accountId", "required", "An account id is required.");
        }
    }

    // the domain throws on zero or less, so stop it here where it can still be a 400
    public static IEnumerable<ValidationError> PositiveAmount(Money? amount)
    {
        if (amount is null)
        {
            yield return new ValidationError("amount", "required", "An amount is required.");
        }
        else if (amount.IsNegative || amount.IsZero)
        {
            yield return new ValidationError("amount", "must_be_positive", "The amount must be greater than zero.");
        }
    }
}

public sealed class OpenAccountValidator : ICommandValidator<OpenAccount>
{
    public IEnumerable<ValidationError> Validate(OpenAccount command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Iban is null)
        {
            yield return new ValidationError("iban", "required", "An IBAN is required.");
        }

        if (command.Currency is null)
        {
            yield return new ValidationError("currency", "required", "A currency is required.");
        }
    }
}

public sealed class DepositValidator : ICommandValidator<Deposit>
{
    public IEnumerable<ValidationError> Validate(Deposit command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return AccountValidation.AccountId(command.AccountId).Concat(AccountValidation.PositiveAmount(command.Amount));
    }
}

public sealed class WithdrawValidator : ICommandValidator<Withdraw>
{
    public IEnumerable<ValidationError> Validate(Withdraw command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return AccountValidation.AccountId(command.AccountId).Concat(AccountValidation.PositiveAmount(command.Amount));
    }
}
