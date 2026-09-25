using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace LedgerCore.Domain.Monetary;

/// <summary>An ISO 4217 currency and the number of decimal places it settles in.</summary>
public sealed record Currency
{
    private static readonly FrozenDictionary<string, Currency> Known = new[]
    {
        new Currency("EUR", 2), new Currency("USD", 2), new Currency("GBP", 2),
        new Currency("CHF", 2), new Currency("SEK", 2), new Currency("NOK", 2),
        new Currency("DKK", 2), new Currency("PLN", 2), new Currency("CZK", 2),
        new Currency("HUF", 2), new Currency("RON", 2), new Currency("CAD", 2),
        new Currency("AUD", 2), new Currency("BRL", 2), new Currency("AOA", 2),
        new Currency("MZN", 2), new Currency("CVE", 2), new Currency("JPY", 0),
        new Currency("KRW", 0), new Currency("ISK", 0), new Currency("BHD", 3),
        new Currency("KWD", 3), new Currency("TND", 3),
    }.ToFrozenDictionary(c => c.Code, StringComparer.Ordinal);

    public static Currency Eur { get; } = Known["EUR"];
    public static Currency Usd { get; } = Known["USD"];
    public static Currency Gbp { get; } = Known["GBP"];

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    public string Code { get; }

    /// <summary>Digits after the decimal point, 2 for EUR, 0 for JPY, 3 for BHD.</summary>
    public int MinorUnits { get; }

    public static Currency FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return TryFromCode(code, out var currency)
            ? currency
            : throw new ArgumentException($"'{code}' is not a supported ISO 4217 currency code.", nameof(code));
    }

    public static bool TryFromCode(string? code, [NotNullWhen(true)] out Currency? currency)
    {
        currency = null;
        return code is not null && Known.TryGetValue(code.Trim().ToUpperInvariant(), out currency);
    }

    public override string ToString() => Code;
}
