using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.Snapshots;

namespace LedgerCore.Infrastructure.Tests.Snapshots;

public class InMemorySnapshotStoreTests() : SnapshotStoreContract(new InMemorySnapshotStore<AccountSnapshot>());
