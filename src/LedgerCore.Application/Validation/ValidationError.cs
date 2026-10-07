namespace LedgerCore.Application.Validation;

/// <summary>One thing wrong with a command's input. <see cref="Field"/> and <see cref="Code"/> are stable for clients.</summary>
public sealed record ValidationError(string Field, string Code, string Message);
