using LedgerCore.Infrastructure.Accounts;

namespace LedgerCore.Api.Accounts;

/// <summary>Keeps the account read model close behind the event store.</summary>
internal sealed partial class AccountSummariesCatchUp(
    InMemoryAccountSummaries summaries,
    TimeProvider clock,
    ILogger<AccountSummariesCatchUp> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await summaries.CatchUpAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // the checkpoint only moves after an event is applied, so the next tick retries it
                LogCatchUpFailed(logger, e, summaries.Checkpoint);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Account read model failed to catch up after position {Checkpoint}")]
    private static partial void LogCatchUpFailed(ILogger logger, Exception exception, long checkpoint);
}
