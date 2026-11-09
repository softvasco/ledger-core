namespace LedgerCore.Contracts.Accounts.V1;

// no IBAN: a consumer that needs it can ask the API, and a broker keeps messages out of our reach
[IntegrationEvent("ledger.account.opened", 1)]
public sealed record AccountOpened(Guid AccountId, string Currency, DateTimeOffset OccurredAt);
