using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Transfers;

public static class TransferErrors
{
    public const string NotFoundCode = "transfer.not_found";

    public static DomainError NotFound(TransferId id) => new(NotFoundCode, $"Transfer {id} does not exist.");
}
