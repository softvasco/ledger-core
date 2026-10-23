using LedgerCore.Application.Projections;

namespace LedgerCore.Api.Projections;

/// <summary>Keeps the read models close behind the event store.</summary>
internal sealed partial class ProjectionCatchUp(
    ProjectionRunner runner,
    TimeProvider clock,
    ILogger<ProjectionCatchUp> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await runner.CatchUpAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // the runner saved the checkpoint before the failing event, so the next tick retries it
                LogCatchUpFailed(logger, e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Projections failed to catch up")]
    private static partial void LogCatchUpFailed(ILogger logger, Exception exception);
}
