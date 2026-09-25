using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Tests.Monetary;

public class MoneyTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    [Fact]
    public void Adds_and_subtracts_in_the_same_currency()
    {
        Assert.Equal(Eur(15.75m), Eur(10.50m) + Eur(5.25m));
        Assert.Equal(Eur(-0.01m), Eur(0.99m) - Eur(1.00m));
    }

    [Fact]
    public void Refuses_to_mix_currencies()
    {
        var usd = Money.Of(1m, Currency.Usd);

        Assert.Throws<CurrencyMismatchException>(() => Eur(1m) + usd);
        Assert.Throws<CurrencyMismatchException>(() => Eur(1m) - usd);
        Assert.Throws<CurrencyMismatchException>(() => Eur(1m) < usd);
    }

    [Theory]
    [InlineData(0.001, "EUR")]
    [InlineData(1.5, "JPY")]
    [InlineData(0.0001, "BHD")]
    public void Rejects_amounts_finer_than_the_currency_allows(decimal amount, string code)
    {
        Assert.Throws<ArgumentException>(() => Money.Of(amount, Currency.FromCode(code)));
    }

    [Fact]
    public void Accepts_trailing_zeros_beyond_the_minor_units()
    {
        Assert.Equal(Eur(1.2m), Eur(1.2000m));
    }

    [Theory]
    [InlineData(10.00, 0.125, 1.25)]
    [InlineData(0.05, 0.5, 0.02)]
    [InlineData(0.15, 0.5, 0.08)]
    public void Multiplying_rounds_half_to_even_by_default(decimal amount, decimal factor, decimal expected)
    {
        Assert.Equal(Eur(expected), Eur(amount).Multiply(factor));
    }

    [Fact]
    public void Multiplying_can_round_away_from_zero_when_a_product_needs_it()
    {
        Assert.Equal(Eur(0.03m), Eur(0.05m).Multiply(0.5m, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Compares_amounts_in_the_same_currency()
    {
        Assert.True(Eur(1m) < Eur(2m));
        Assert.True(Eur(2m) >= Eur(2m));
        Assert.Equal(0, Eur(3m).CompareTo(Eur(3.00m)));
    }

    [Fact]
    public void Negation_flips_the_sign()
    {
        Assert.True((-Eur(5m)).IsNegative);
        Assert.True((Eur(5m) + -Eur(5m)).IsZero);
    }

    [Theory]
    [InlineData(1234.5, "EUR", "EUR 1234.50")]
    [InlineData(1500, "JPY", "JPY 1500")]
    [InlineData(-0.125, "BHD", "BHD -0.125")]
    public void Formats_with_the_currency_code_and_its_decimals(decimal amount, string code, string expected)
    {
        Assert.Equal(expected, Money.Of(amount, Currency.FromCode(code)).ToString());
    }
}
