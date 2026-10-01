namespace LedgerCore.Application.EventStore;

/// <summary>Name of one aggregate's stream, such as <c>account-0199a3c2-...</c>.</summary>
public readonly record struct StreamId
{
    private StreamId(string category, Guid id)
    {
        Category = category;
        Id = id;
    }

    public string Category { get; }

    public Guid Id { get; }

    // lowercase letters only, so the category can always be split back off the name
    public static StreamId For(string category, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (!category.All(char.IsAsciiLetterLower))
        {
            throw new ArgumentException($"'{category}' must be lowercase letters only.", nameof(category));
        }

        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stream needs an id.", nameof(id));
        }

        return new StreamId(category, id);
    }

    public override string ToString() => $"{Category}-{Id}";
}
