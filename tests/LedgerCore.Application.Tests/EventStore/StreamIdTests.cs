using LedgerCore.Application.EventStore;

namespace LedgerCore.Application.Tests.EventStore;

public class StreamIdTests
{
    private static readonly Guid SomeId = Guid.Parse("0199a3c2-7b1e-7d40-9a51-3c0f6e2b8d14");

    [Fact]
    public void The_name_is_the_category_and_the_id()
    {
        var stream = StreamId.For("account", SomeId);

        Assert.Equal("account-0199a3c2-7b1e-7d40-9a51-3c0f6e2b8d14", stream.ToString());
    }

    [Fact]
    public void Same_category_and_id_is_the_same_stream()
    {
        Assert.Equal(StreamId.For("account", SomeId), StreamId.For("account", SomeId));
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("journal-entry")]
    [InlineData("account1")]
    [InlineData(" ")]
    public void Rejects_a_category_that_is_not_lowercase_letters(string category)
    {
        Assert.ThrowsAny<ArgumentException>(() => StreamId.For(category, SomeId));
    }

    [Fact]
    public void Rejects_an_empty_id()
    {
        Assert.Throws<ArgumentException>(() => StreamId.For("account", Guid.Empty));
    }
}
