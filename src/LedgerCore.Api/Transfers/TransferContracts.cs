using LedgerCore.Api.Accounts;
using LedgerCore.Application.Transfers;

namespace LedgerCore.Api.Transfers;

public sealed record TransferRequest(Guid Source, Guid Destination, decimal Amount, string? Currency);

public sealed record TransferResponse(
    Guid Id, Guid Source, Guid Destination, decimal Amount, string Currency, string Status, string? Reason)
{
    public static TransferResponse From(TransferSummary summary) => new(
        summary.Id.Value,
        summary.Source.Value,
        summary.Destination.Value,
        summary.Amount.ForResponse(),
        summary.Amount.Currency.Code,
        summary.Status.ToString(),
        summary.Reason);
}
