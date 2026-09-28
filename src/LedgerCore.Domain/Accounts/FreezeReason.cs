namespace LedgerCore.Domain.Accounts;

// a fixed list instead of free text, so nobody types a customer's name into an event
public enum FreezeReason
{
    CustomerRequest,
    SuspectedFraud,
    CourtOrder,
    Sanctions,
}
