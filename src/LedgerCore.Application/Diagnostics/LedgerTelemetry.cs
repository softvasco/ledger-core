using System.Diagnostics;

namespace LedgerCore.Application.Diagnostics;

/// <summary>The names a host passes to AddSource and AddMeter for ledger-core.</summary>
public static class LedgerTelemetry
{
    public const string SourceName = "LedgerCore.Application";

    public const string MeterName = "LedgerCore.Application";

    internal static readonly ActivitySource Source = new(SourceName);
}
