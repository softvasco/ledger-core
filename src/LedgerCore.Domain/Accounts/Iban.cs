using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace LedgerCore.Domain.Accounts;

/// <summary>An International Bank Account Number, validated by length per country and the ISO 7064 mod-97 check.</summary>
public sealed record Iban
{
    // SEPA countries only for now; the ledger doesn't hold accounts anywhere else
    private static readonly FrozenDictionary<string, int> LengthByCountry = new Dictionary<string, int>
    {
        ["AD"] = 24, ["AT"] = 20, ["BE"] = 16, ["BG"] = 22, ["CH"] = 21, ["CY"] = 28, ["CZ"] = 24,
        ["DE"] = 22, ["DK"] = 18, ["EE"] = 20, ["ES"] = 24, ["FI"] = 18, ["FR"] = 27, ["GB"] = 22,
        ["GI"] = 23, ["GR"] = 27, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IS"] = 26, ["IT"] = 27,
        ["LI"] = 21, ["LT"] = 20, ["LU"] = 20, ["LV"] = 21, ["MC"] = 27, ["MT"] = 31, ["NL"] = 18,
        ["NO"] = 15, ["PL"] = 28, ["PT"] = 25, ["RO"] = 24, ["SE"] = 24, ["SI"] = 19, ["SK"] = 24,
        ["SM"] = 27, ["VA"] = 22,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private Iban(string value) => Value = value;

    /// <summary>Electronic format: upper case, no spaces.</summary>
    public string Value { get; }

    public string CountryCode => Value[..2];

    public static Iban Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return TryParse(value, out var iban)
            ? iban
            : throw new ArgumentException($"'{value}' is not a valid IBAN.", nameof(value));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out Iban? iban)
    {
        iban = null;
        if (value is null)
        {
            return false;
        }

        var compact = Compact(value);
        if (compact.Length < 5
            || !LengthByCountry.TryGetValue(compact[..2], out var expectedLength)
            || compact.Length != expectedLength
            || !char.IsAsciiDigit(compact[2]) || !char.IsAsciiDigit(compact[3])
            || !compact.All(char.IsAsciiLetterOrDigit)
            || Mod97(compact) != 1)
        {
            return false;
        }

        iban = new Iban(compact);
        return true;
    }

    /// <summary>Print format, in groups of four, the way it appears on a statement.</summary>
    public override string ToString() => string.Join(' ', Value.Chunk(4).Select(c => new string(c)));

    private static string Compact(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return sb.ToString();
    }

    // country and check digits move to the end, letters become 10..35, and the whole number mod 97 must be 1
    private static int Mod97(string iban)
    {
        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var digit = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = digit >= 10
                ? ((remainder * 100) + digit) % 97
                : ((remainder * 10) + digit) % 97;
        }

        return remainder;
    }
}
