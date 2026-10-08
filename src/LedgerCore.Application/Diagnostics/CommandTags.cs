namespace LedgerCore.Application.Diagnostics;

/// <summary>Tag names on command spans and log scopes.</summary>
public static class CommandTags
{
    public const string Command = "ledger.command";

    public const string Outcome = "ledger.command.outcome";

    public const string ErrorCode = "ledger.error.code";
}

/// <summary>Values of the <see cref="CommandTags.Outcome"/> tag.</summary>
public static class CommandOutcome
{
    public const string Succeeded = "succeeded";

    public const string Rejected = "rejected";

    public const string Invalid = "invalid";

    public const string Failed = "failed";
}
