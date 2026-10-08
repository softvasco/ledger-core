using System.Diagnostics;

namespace LedgerCore.Application.Diagnostics;

/// <summary>The source name a host passes to AddSource to collect ledger-core's spans.</summary>
public static class LedgerTelemetry
{
    public const string SourceName = "LedgerCore.Application";

    internal static readonly ActivitySource Source = new(SourceName);
}
