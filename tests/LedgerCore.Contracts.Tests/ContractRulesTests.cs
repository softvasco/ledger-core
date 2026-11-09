using System.Reflection;
using System.Text.Json;
using LedgerCore.Contracts.Transfers.V1;

namespace LedgerCore.Contracts.Tests;

public class ContractRulesTests
{
    private static readonly HashSet<Type> WireTypes =
        [typeof(Guid), typeof(string), typeof(decimal), typeof(DateTimeOffset), typeof(int), typeof(long), typeof(bool)];

    private static readonly Type[] Events =
        [.. typeof(IntegrationEventAttribute).Assembly.GetExportedTypes()
            .Where(type => type.GetCustomAttribute<IntegrationEventAttribute>() is not null)];

    public static TheoryData<Type> AllEvents => [.. Events];

    [Fact]
    public void Contracts_reference_nothing_from_the_ledger()
    {
        var references = typeof(IntegrationEventAttribute).Assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name!.StartsWith("LedgerCore", StringComparison.Ordinal));

        Assert.Empty(references);
    }

    [Fact]
    public void Every_wire_type_is_unique()
    {
        var types = Events.Select(type => type.GetCustomAttribute<IntegrationEventAttribute>()!.Type).ToArray();

        Assert.NotEmpty(types);
        Assert.Equal(types.Length, types.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void An_event_lives_in_the_namespace_of_its_version(Type type)
    {
        var version = type.GetCustomAttribute<IntegrationEventAttribute>()!.Version;

        Assert.EndsWith($".V{version}", type.Namespace, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void An_event_carries_only_plain_wire_types(Type type)
    {
        var others = type.GetProperties()
            .Where(property => !WireTypes.Contains(property.PropertyType))
            .Select(property => property.Name);

        Assert.Empty(others);
    }

    [Fact]
    public void An_amount_keeps_its_scale_through_json()
    {
        var sent = new TransferCompleted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100.10m, "EUR", DateTimeOffset.UnixEpoch);

        var received = JsonSerializer.Deserialize<TransferCompleted>(JsonSerializer.Serialize(sent));

        Assert.Equal("100.10", received!.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(sent, received);
    }
}
