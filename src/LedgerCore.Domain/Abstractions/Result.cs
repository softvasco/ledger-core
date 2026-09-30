using System.Diagnostics.CodeAnalysis;

namespace LedgerCore.Domain.Abstractions;

/// <summary>The outcome of an operation that can be refused by a business rule.</summary>
public class Result
{
    private protected Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    public static Result<T> Failure<T>(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}

/// <summary>A value, or the reason there isn't one.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, DomainError? error)
        : base(error) => _value = value;

    // reading the value of a failure is a bug in the caller, not a business outcome
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"No value, the operation failed with {Error}.");
}
