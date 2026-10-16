using LedgerCore.Infrastructure.Idempotency;

namespace LedgerCore.Infrastructure.Tests.Idempotency;

public class InMemoryIdempotencyStoreTests() : IdempotencyStoreContract(time => new InMemoryIdempotencyStore(Options, time));
