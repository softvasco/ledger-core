using System.Diagnostics.Metrics;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Transfers;

namespace LedgerCore.Application.Diagnostics;

/// <summary>Metrics only the ledger can produce, collected with AddMeter.</summary>
public sealed class LedgerMetrics
{
    public const string TransferOutcomeTag = "ledger.transfer.outcome";
    public const string ProjectionTag = "ledger.projection";

    private readonly Counter<long> _transfers;
    private readonly Histogram<double> _projectionLag;
    private readonly TimeProvider _clock;

    public LedgerMetrics(IMeterFactory meters, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(clock);

        var meter = meters.Create(LedgerTelemetry.MeterName);
        _transfers = meter.CreateCounter<long>("ledger.transfers", "{transfer}", "Transfers that reached a final status.");
        _projectionLag = meter.CreateHistogram<double>(
            "ledger.projection.lag", "s", "Time from an event occurring to a projection applying it.");
        _clock = clock;
    }

    public void TransferEnded(TransferStatus status) =>
        _transfers.Add(1, new KeyValuePair<string, object?>(TransferOutcomeTag, Outcome(status)));

    public void ProjectionApplied(string projection, IDomainEvent applied)
    {
        ArgumentNullException.ThrowIfNull(applied);

        var lag = _clock.GetUtcNow() - applied.OccurredAt;
        _projectionLag.Record(lag.TotalSeconds, new KeyValuePair<string, object?>(ProjectionTag, projection));
    }

    private static string Outcome(TransferStatus status) => status switch
    {
        TransferStatus.Completed => "completed",
        TransferStatus.Failed => "failed",
        TransferStatus.Compensated => "compensated",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Only a final status ends a transfer."),
    };
}
