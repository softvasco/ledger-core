using System.Globalization;

namespace LedgerCore.Domain.Monetary;

/// <summary>An amount in a single currency, never more precise than the currency settles in.</summary>
public sealed record Money : IComparable<Money>
{
    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // a ledger that silently rounds on the way in loses cents nobody can explain later
        if (decimal.Round(amount, currency.MinorUnits) != amount)
        {
            throw new ArgumentException(
                $"{amount} has more than {currency.MinorUnits} decimal places for {currency}.", nameof(amount));
        }

        return new Money(amount, currency);
    }

    public static Money Zero(Currency currency) => Of(0m, currency);

    public Money Add(Money other) => new(Amount + SameCurrency(other).Amount, Currency);

    public Money Subtract(Money other) => new(Amount - SameCurrency(other).Amount, Currency);

    public Money Negate() => new(-Amount, Currency);

    /// <summary>Scales the amount and rounds it back to the currency's minor units.</summary>
    public Money Multiply(decimal factor, MidpointRounding rounding = MidpointRounding.ToEven) =>
        new(decimal.Round(Amount * factor, Currency.MinorUnits, rounding), Currency);

    public int CompareTo(Money? other) =>
        other is null ? 1 : Amount.CompareTo(SameCurrency(other).Amount);

    public static Money operator +(Money left, Money right) => Guard(left).Add(right);

    public static Money operator -(Money left, Money right) => Guard(left).Subtract(right);

    public static Money operator -(Money money) => Guard(money).Negate();

    public static bool operator <(Money left, Money right) => Guard(left).CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => Guard(left).CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => Guard(left).CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => Guard(left).CompareTo(right) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Currency.Code} {Amount.ToString($"F{Currency.MinorUnits}", CultureInfo.InvariantCulture)}");

    private Money SameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Currency == Currency ? other : throw new CurrencyMismatchException(Currency, other.Currency);
    }

    private static Money Guard(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money;
    }
}
