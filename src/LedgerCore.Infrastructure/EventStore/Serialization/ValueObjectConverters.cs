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
