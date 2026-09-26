using LedgerCore.Domain.Accounts;

namespace LedgerCore.Domain.Tests.Accounts;

public class IbanTests
{
    [Theory]
    [InlineData("PT50000201231234567890154")]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("NL91ABNA0417164300")]
    [InlineData("BE68539007547034")]
    [InlineData("FR1420041010050500013M02606")]
    public void Accepts_valid_ibans(string value)
    {
        Assert.Equal(value, Iban.Parse(value).Value);
    }

    [Fact]
    public void Accepts_print_format_and_lower_case()
    {
        var iban = Iban.Parse("pt50 0002 0123 1234 5678 9015 4");

        Assert.Equal("PT50000201231234567890154", iban.Value);
        Assert.Equal("PT", iban.CountryCode);
    }

    [Theory]
    [InlineData("PT50000201231234567890155")]
    [InlineData("DE89370400440532013001")]
    [InlineData("GB28WEST12345698765432")]
    public void Rejects_a_wrong_check_digit(string value)
    {
        Assert.False(Iban.TryParse(value, out _));
    }

    [Theory]
    [InlineData("PT5000020123123456789015")]
    [InlineData("DE8937040044053201300000")]
    public void Rejects_the_wrong_length_for_the_country(string value)
    {
        Assert.False(Iban.TryParse(value, out _));
    }

    [Theory]
    [InlineData("XX82WEST12345698765432")]
    [InlineData("PTAB000201231234567890154")]
    [InlineData("PT50-0002-0123-1234-5678-9015-4")]
    [InlineData("PT5")]
    public void Rejects_malformed_input(string value)
    {
        Assert.False(Iban.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => Iban.Parse(value));
    }

    [Fact]
    public void Prints_in_groups_of_four()
    {
        Assert.Equal("PT50 0002 0123 1234 5678 9015 4", Iban.Parse("PT50000201231234567890154").ToString());
    }

    [Fact]
    public void Two_ibans_are_equal_when_their_electronic_form_is()
    {
        Assert.Equal(Iban.Parse("DE89 3704 0044 0532 0130 00"), Iban.Parse("DE89370400440532013000"));
    }
}
