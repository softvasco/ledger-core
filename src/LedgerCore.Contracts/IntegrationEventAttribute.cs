namespace LedgerCore.Contracts;

/// <summary>Names an event on the wire, so renaming the C# type can't break a consumer.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string name, int version) : Attribute
{
    public string Name { get; } = name;

    public int Version { get; } = version;

    public string Type => $"{Name}.v{Version}";
}
