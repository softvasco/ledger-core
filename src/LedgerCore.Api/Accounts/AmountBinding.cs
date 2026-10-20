using LedgerCore.Domain.Monetary;

namespace LedgerCore.Api.Accounts;

internal static class AmountBinding
{
    public static Money? ReadAmount(decimal amount, string? currencyCode, Dictionary<string, string[]> errors)
    {
        if (!Currency.TryFromCode(currencyCode, out var currency))
        {
            errors["currency"] = ["Not a supported ISO 4217 currency code."];
            return null;
        }

        // Money refuses extra decimals with an exception; here it's the client's mistake, so a 400
        if (decimal.Round(amount, currency.MinorUnits) != amount)
        {
            errors["amount"] = [$"{currency.Code} amounts have at most {currency.MinorUnits} decimal places."];
            return null;
        }

        return Money.Of(amount, currency);
    }
}
