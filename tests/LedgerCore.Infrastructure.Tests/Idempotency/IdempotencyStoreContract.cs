using LedgerCore.Application.Idempotency;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Infrastructure.Tests.Idempotency;

public abstract class IdempotencyStoreContract
{
    protected static readonly IdempotencyOptions Options = new()
    {
        Retention = TimeSpan.FromHours(24),
        LockTimeout = TimeSpan.FromMinutes(1),
    };

    private static readonly byte[] Fingerprint = [1, 2, 3];
    private static readonly StoredResponse Created = new(201, "application/json", "/accounts/1", "{\"id\":1}"u8.ToArray());

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero));

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected abstract IIdempotencyStore Store { get; }

    [Fact]
    public async Task A_new_key_is_claimed()
    {
        Assert.Equal(IdempotencyClaim.Claimed, await Store.ClaimAsync(NewKey(), Fingerprint, Token));
    }

    [Fact]
    public async Task A_key_still_running_is_in_progress()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);

        Assert.Equal(IdempotencyClaim.InProgress, await Store.ClaimAsync(key, Fingerprint, Token));
    }

    [Fact]
    public async Task A_completed_key_replays_the_stored_response()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);
        await Store.CompleteAsync(key, Created, Token);

        var claim = Assert.IsType<IdempotencyClaim.Completed>(await Store.ClaimAsync(key, Fingerprint, Token));
        Assert.Equal(Created.StatusCode, claim.Response.StatusCode);
        Assert.Equal(Created.ContentType, claim.Response.ContentType);
        Assert.Equal(Created.Location, claim.Response.Location);
        Assert.Equal(Created.Body, claim.Response.Body);
    }

    [Fact]
    public async Task The_same_key_for_a_different_request_is_reused()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);
        await Store.CompleteAsync(key, Created, Token);

        Assert.Equal(IdempotencyClaim.Reused, await Store.ClaimAsync(key, [9, 9, 9], Token));
    }

    [Fact]
    public async Task A_released_key_can_be_claimed_again()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);
        await Store.ReleaseAsync(key, Token);

        Assert.Equal(IdempotencyClaim.Claimed, await Store.ClaimAsync(key, Fingerprint, Token));
    }

    [Fact]
    public async Task Releasing_never_forgets_a_completed_response()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);
        await Store.CompleteAsync(key, Created, Token);
        await Store.ReleaseAsync(key, Token);

        Assert.IsType<IdempotencyClaim.Completed>(await Store.ClaimAsync(key, Fingerprint, Token));
    }

    // the process that claimed it died before finishing
    [Fact]
    public async Task A_claim_older_than_the_lock_timeout_can_be_taken_over()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);

        Time.Advance(Options.LockTimeout);

        Assert.Equal(IdempotencyClaim.Claimed, await Store.ClaimAsync(key, Fingerprint, Token));
    }

    [Fact]
    public async Task A_completed_key_is_kept_past_the_lock_timeout()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);
        await Store.CompleteAsync(key, Created, Token);

        Time.Advance(Options.LockTimeout * 10);

        Assert.IsType<IdempotencyClaim.Completed>(await Store.ClaimAsync(key, Fingerprint, Token));
    }

    [Fact]
    public async Task A_completed_key_is_forgotten_after_the_retention()
    {
        var key = NewKey();
        await Store.ClaimAsync(key, Fingerprint, Token);
        await Store.CompleteAsync(key, Created, Token);

        Time.Advance(Options.Retention);

        Assert.Equal(IdempotencyClaim.Claimed, await Store.ClaimAsync(key, [9, 9, 9], Token));
    }

    private static string NewKey() => Guid.CreateVersion7().ToString();
}
