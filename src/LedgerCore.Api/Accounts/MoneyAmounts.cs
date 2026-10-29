using LedgerCore.Domain.Monetary;

namespace LedgerCore.Api.Accounts;

internal static class MoneyAmounts
{
    /// <summary>The amount with exactly the currency's minor units: EUR 100 is 100.00.</summary>
    public static decimal ForResponse(this Money money)
    {
        ArgumentNullException.ThrowIfNull(money);

        // a JSON number keeps the decimal's scale, and SQL Server hands back the column's four places
        var scale = (byte)money.Currency.MinorUnits;
        return decimal.Round(money.Amount, scale) + new decimal(0, 0, 0, isNegative: false, scale);
    }
}
