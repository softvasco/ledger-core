using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Tests.Monetary;

public class CurrencyTests
{
    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("BHD", 3)]
    public void Knows_how_many_decimals_each_currency_settles_in(string code, int minorUnits)
    {
        Assert.Equal(minorUnits, Currency.FromCode(code).MinorUnits);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData(" EUR ")]
    public void Accepts_codes_regardless_of_case_and_padding(string code)
    {
        Assert.Same(Currency.Eur, Currency.FromCode(code));
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("EURO")]
    public void Rejects_unknown_codes(string code)
    {
        Assert.Throws<ArgumentException>(() => Currency.FromCode(code));
        Assert.False(Currency.TryFromCode(code, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_blank_codes(string code)
    {
        Assert.Throws<ArgumentException>(() => Currency.FromCode(code));
    }

    [Fact]
    public void Same_code_means_same_currency()
    {
        Assert.Equal(Currency.FromCode("USD"), Currency.Usd);
        Assert.Equal("GBP", Currency.Gbp.ToString());
    }
}
