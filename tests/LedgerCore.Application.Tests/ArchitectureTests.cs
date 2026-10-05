using LedgerCore.Application.EventStore;

namespace LedgerCore.Application.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var infrastructureReference = typeof(IEventStore).Assembly
            .GetReferencedAssemblies()
            .FirstOrDefault(reference => reference.Name == "LedgerCore.Infrastructure");

        Assert.Null(infrastructureReference);
    }
}
