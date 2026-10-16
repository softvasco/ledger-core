using LedgerCore.Application.Idempotency;
using LedgerCore.Infrastructure.Idempotency;

namespace LedgerCore.Infrastructure.Tests.Idempotency;

public class InMemoryIdempotencyStoreTests : IdempotencyStoreContract
{
    public InMemoryIdempotencyStoreTests() => Store = new InMemoryIdempotencyStore(Options, Time);

    protected override IIdempotencyStore Store { get; }
}
