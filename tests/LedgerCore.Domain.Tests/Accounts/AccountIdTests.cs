using LedgerCore.Domain.Accounts;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Domain.Tests.Accounts;

public class AccountIdTests
{
    [Fact]
    public void New_ids_are_version_7_and_sort_by_creation_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 20, 0, 0, TimeSpan.Zero));

        var first = AccountId.New(clock);
        clock.Advance(TimeSpan.FromMilliseconds(5));
        var second = AccountId.New(clock);

        Assert.Equal(7, first.Value.Version);
        Assert.True(first.Value.CompareTo(second.Value) < 0);
    }

    [Fact]
    public void Round_trips_through_its_string_form()
    {
        var id = AccountId.New(TimeProvider.System);

        Assert.Equal(id, AccountId.Parse(id.ToString()));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Rejects_invalid_or_empty_values(string value)
    {
        Assert.False(AccountId.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => AccountId.Parse(value));
    }

    [Fact]
    public void Refuses_an_empty_guid()
    {
        Assert.Throws<ArgumentException>(() => AccountId.From(Guid.Empty));
    }
}
