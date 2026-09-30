using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Tests.Monetary;

public class MoneyProperties
{
    private static readonly Currency[] Currencies = [Currency.FromCode("JPY"), Currency.Eur, Currency.FromCode("BHD")];

    private static readonly Arbitrary<(Money A, Money B)> Pairs =
        (from currency in Gen.Elements(Currencies)
         from a in Gen.Choose(-1_000_000, 1_000_000)
         from b in Gen.Choose(-1_000_000, 1_000_000)
         select (Minor(a, currency), Minor(b, currency))).ToArbitrary();

    private static readonly Arbitrary<(Money Amount, decimal Factor)> Scalings =
        (from currency in Gen.Elements(Currencies)
         from units in Gen.Choose(-1_000_000, 1_000_000)
         from factorBasisPoints in Gen.Choose(-50_000, 50_000)
         select (Minor(units, currency), factorBasisPoints / 10_000m)).ToArbitrary();

    [Property(MaxTest = 500)]
    public Property Subtracting_what_was_added_gives_the_original_back() =>
        Prop.ForAll(Pairs, pair => pair.A + pair.B - pair.B == pair.A);

    [Property(MaxTest = 500)]
    public Property Addition_does_not_depend_on_order() =>
        Prop.ForAll(Pairs, pair => pair.A + pair.B == pair.B + pair.A);

    [Property(MaxTest = 500)]
    public Property Scaling_never_leaves_more_decimals_than_the_currency_has() =>
        Prop.ForAll(Scalings, s =>
        {
            var scaled = s.Amount.Multiply(s.Factor);
            return decimal.Round(scaled.Amount, scaled.Currency.MinorUnits) == scaled.Amount;
        });

    private static Money Minor(int units, Currency currency) =>
        Money.Of(units / (decimal)Math.Pow(10, currency.MinorUnits), currency);
}
