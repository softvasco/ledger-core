using System.Text.Json.Serialization;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Transfers.Events;

namespace LedgerCore.Infrastructure.EventStore.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true,
    Converters = [typeof(AccountIdConverter), typeof(TransferIdConverter), typeof(IbanConverter), typeof(CurrencyConverter), typeof(MoneyConverter)])]
[JsonSerializable(typeof(AccountOpened))]
[JsonSerializable(typeof(AccountFrozen))]
[JsonSerializable(typeof(AccountUnfrozen))]
[JsonSerializable(typeof(AccountClosed))]
[JsonSerializable(typeof(MoneyDeposited))]
[JsonSerializable(typeof(MoneyWithdrawn))]
[JsonSerializable(typeof(TransferRequested))]
[JsonSerializable(typeof(TransferSourceDebited))]
[JsonSerializable(typeof(TransferCompleted))]
[JsonSerializable(typeof(TransferFailed))]
[JsonSerializable(typeof(TransferCompensated))]
[JsonSerializable(typeof(AccountSnapshot))]
internal sealed partial class LedgerJsonContext : JsonSerializerContext;
