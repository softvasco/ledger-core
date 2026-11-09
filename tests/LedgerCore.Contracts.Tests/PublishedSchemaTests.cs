using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace LedgerCore.Contracts.Tests;

public class PublishedSchemaTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
        NewLine = "\n",
    };

    public static TheoryData<Type> AllEvents =>
        [.. typeof(IntegrationEventAttribute).Assembly.GetExportedTypes()
            .Where(type => type.GetCustomAttribute<IntegrationEventAttribute>() is not null)];

    // a consumer deployed months ago still reads this shape, so a change has to be a deliberate edit
    [Theory]
    [MemberData(nameof(AllEvents))]
    public void A_published_event_keeps_its_schema(Type type)
    {
        var name = type.GetCustomAttribute<IntegrationEventAttribute>()!.Type;
        var actual = Options.GetJsonSchemaAsNode(type).ToJsonString(Options) + "\n";
        var path = Path.Join(SchemaDirectory(), $"{name}.json");
        var expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;

        if (actual != expected)
        {
            File.WriteAllText(Path.ChangeExtension(path, ".received.json"), actual);
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_snapshot_with_no_event_means_an_event_was_removed()
    {
        var published = AllEvents
            .Select(row => row.Data.GetCustomAttribute<IntegrationEventAttribute>()!.Type)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = Directory.GetFiles(SchemaDirectory(), "*.json")
            .Select(Path.GetFileName)
            .Where(file => !file!.EndsWith(".received.json", StringComparison.Ordinal))
            .Where(file => !published.Contains(Path.GetFileNameWithoutExtension(file)!));

        Assert.Empty(orphans);
    }

    private static string SchemaDirectory([CallerFilePath] string testFile = "") =>
        Path.Join(Path.GetDirectoryName(testFile)!, "Schemas");
}
