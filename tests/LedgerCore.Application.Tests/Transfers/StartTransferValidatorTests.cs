using LedgerCore.Application.Transfers;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Domain.Transfers;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Application.Tests.Transfers;

public class StartTransferValidatorTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly StartTransferValidator _validator = new();

    [Fact]
    public void A_transfer_between_two_accounts_is_valid()
    {
        var command = new StartTransfer(TransferId.New(_clock), AccountId.New(_clock), AccountId.New(_clock), Eur(10m));

        Assert.Empty(_validator.Validate(command));
    }

    [Fact]
    public void A_transfer_to_the_same_account_is_refused_on_the_destination()
    {
        var account = AccountId.New(_clock);

        var error = Assert.Single(_validator.Validate(new StartTransfer(TransferId.New(_clock), account, account, Eur(10m))));

        Assert.Equal(("destination", "same_account"), (error.Field, error.Code));
    }

    [Fact]
    public void Missing_accounts_and_a_zero_amount_are_all_reported()
    {
        var errors = _validator.Validate(new StartTransfer(TransferId.New(_clock), default, default, Money.Zero(Currency.Eur)));

        Assert.Equal(
            [("source", "required"), ("destination", "required"), ("amount", "must_be_positive")],
            errors.Select(e => (e.Field, e.Code)));
    }

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);
}
