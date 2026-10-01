using LedgerCore.Infrastructure.EventStore;

namespace LedgerCore.Infrastructure.Tests.EventStore;

public class InMemoryEventStoreTests() : EventStoreContract(new InMemoryEventStore());
