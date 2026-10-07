using System.Text.Json;
using System.Text.Json.Serialization;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Infrastructure.EventStore.Serialization;

// reading goes through the value object's own parsing, so stored data is validated again
internal sealed class AccountIdConverter : JsonConverter<AccountId>
{
    public override AccountId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        AccountId.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, AccountId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class IbanConverter : JsonConverter<Iban>
{
    public override Iban Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Iban.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Iban value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class CurrencyConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Currency.FromCode(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Code);
}

// written as {"amount":12.50,"currency":"EUR"}; the amount stays a JSON number so jsonb can still sum it
internal sealed class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be an object.");
        }

        decimal? amount = null;
        string? currency = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();
            switch (property)
            {
                case "amount":
                    amount = reader.GetDecimal();
                    break;
                case "currency":
                    currency = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (amount is null || currency is null)
        {
            throw new JsonException("Money needs both an amount and a currency.");
        }

        return Money.Of(amount.Value, Currency.FromCode(currency));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency.Code);
        writer.WriteEndObject();
    }
}
