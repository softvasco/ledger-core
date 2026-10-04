using System.Text.Json.Serialization;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;

namespace LedgerCore.Infrastructure.EventStore.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    Converters = [typeof(AccountIdConverter), typeof(IbanConverter), typeof(CurrencyConverter)])]
[JsonSerializable(typeof(AccountOpened))]
[JsonSerializable(typeof(AccountFrozen))]
[JsonSerializable(typeof(AccountUnfrozen))]
[JsonSerializable(typeof(AccountClosed))]
[JsonSerializable(typeof(AccountSnapshot))]
internal sealed partial class LedgerJsonContext : JsonSerializerContext;
