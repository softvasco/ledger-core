using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.EventStore.Serialization;

namespace LedgerCore.Infrastructure.Tests.EventStore;

public class JsonEventSerializerTests
{
    private static readonly AccountId SomeAccount = AccountId.Parse("0199a3c2-7b1e-7d40-9a51-3c0f6e2b8d14");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 21, 0, 0, TimeSpan.Zero);

    private readonly JsonEventSerializer _serializer = new();

    public static TheoryData<IDomainEvent> AccountEvents() =>
    [
        new AccountOpened(SomeAccount, Iban.Parse("PT50 0002 0123 1234 5678 9015 4"), Currency.Eur, Now),
        new AccountFrozen(SomeAccount, FreezeReason.CourtOrder, Now),
        new AccountUnfrozen(SomeAccount, Now),
        new AccountClosed(SomeAccount, Now),
    ];

    [Theory]
    [MemberData(nameof(AccountEvents))]
    public void An_event_reads_back_equal_to_what_was_written(IDomainEvent original)
    {
        var stored = _serializer.Serialize(original);

        Assert.Equal(original, _serializer.Deserialize(stored.EventType, stored.Json));
    }

    // stored events never change, so a diff here means old streams may no longer load
    [Fact]
    public void Account_opened_keeps_its_stored_shape()
    {
        var stored = _serializer.Serialize(
            new AccountOpened(SomeAccount, Iban.Parse("PT50 0002 0123 1234 5678 9015 4"), Currency.Eur, Now));

        Assert.Equal("account_opened", stored.EventType);
        Assert.Equal(
            """{"accountId":"0199a3c2-7b1e-7d40-9a51-3c0f6e2b8d14","iban":"PT50000201231234567890154","currency":"EUR","occurredAt":"2026-10-03T21:00:00+00:00"}""",
            stored.Json);
    }

    [Fact]
    public void Freeze_reasons_are_stored_by_name_not_number()
    {
        var stored = _serializer.Serialize(new AccountFrozen(SomeAccount, FreezeReason.CourtOrder, Now));

        Assert.Contains("\"reason\":\"CourtOrder\"", stored.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_domain_event_has_a_stored_name()
    {
        var domainEvents = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IDomainEvent)));

        Assert.Empty(domainEvents.Except(JsonEventSerializer.KnownTypes));
    }

    [Fact]
    public void An_unknown_stored_type_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => _serializer.Deserialize("account_renamed", "{}"));
    }

    [Fact]
    public void An_invalid_stored_iban_is_refused_on_read()
    {
        const string json =
            """{"accountId":"0199a3c2-7b1e-7d40-9a51-3c0f6e2b8d14","iban":"PT50000201231234567890155","currency":"EUR","occurredAt":"2026-10-03T21:00:00+00:00"}""";

        Assert.Throws<ArgumentException>(() => _serializer.Deserialize("account_opened", json));
    }
}
